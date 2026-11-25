using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PayrollApp.Web.Data;
using PayrollApp.Web.Services;

namespace PayrollApp.Web.Pages;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly SignInManager<IdentityUser> _signIn;
    private readonly UserManager<IdentityUser> _users;
    private readonly RoleManager<IdentityRole> _roles;
    private readonly AppDbContext _db;
    private readonly PunchService _punchService;

    public LoginModel(
        SignInManager<IdentityUser> signIn, 
        UserManager<IdentityUser> users,
        RoleManager<IdentityRole> roles,
        AppDbContext db,
        PunchService punchService)
    {
        _signIn = signIn;
        _users = users;
        _roles = roles;
        _db = db;
        _punchService = punchService;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty] public string? OwnerPassword { get; set; }
    [BindProperty] public int EmployeeStep { get; set; } = 1; // 1 = Enter ID, 2 = Enter Password
    public string? ErrorMessage { get; set; }
    [TempData] public string? KioskSuccessMessage { get; set; }
    [TempData] public string? KioskErrorMessage { get; set; }

    public class InputModel
    {
        public string Role { get; set; } = "Owner";          // "Owner" or "Employee"
        public string Identifier { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public async Task<IActionResult> OnGetLogoutAsync()
    {
        await _signIn.SignOutAsync();
        return RedirectToPage("/Login");
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await _signIn.SignOutAsync();
        return RedirectToPage("/Login");
    }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            if (User.IsInRole("Employee")) return RedirectToPage("/Clock");
            return RedirectToPage("/Index");
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var role = Input.Role == "Employee" ? "Employee" : "Owner";
        Input.Role = role;

        const string generic = "Incorrect login details, or the wrong account type was selected.";

        try
        {
            if (role == "Owner")
            {
                if (string.IsNullOrWhiteSpace(OwnerPassword))
                {
                    ErrorMessage = "Please enter your password.";
                    return Page();
                }

                // Find owner user
                var owners = await _users.GetUsersInRoleAsync("Owner");
                IdentityUser? ownerUser = owners.FirstOrDefault();

                if (ownerUser is null)
                {
                    ownerUser = await _users.FindByNameAsync("owner")
                                ?? await _users.FindByEmailAsync("owner@laufey.com");
                }

                // If owner account doesn't exist yet, create it with this password
                if (ownerUser is null)
                {
                    ownerUser = new IdentityUser { UserName = "owner", Email = "owner@laufey.com" };
                    var createRes = await _users.CreateAsync(ownerUser, OwnerPassword);
                    if (createRes.Succeeded)
                    {
                        if (!await _roles.RoleExistsAsync("Owner")) await _roles.CreateAsync(new IdentityRole("Owner"));
                        await _users.AddToRoleAsync(ownerUser, "Owner");
                        await _signIn.SignInAsync(ownerUser, isPersistent: false);
                        return RedirectToPage("/Index");
                    }
                    else
                    {
                        ErrorMessage = createRes.Errors.FirstOrDefault()?.Description ?? generic;
                        return Page();
                    }
                }

                var result = await _signIn.PasswordSignInAsync(
                    ownerUser, OwnerPassword, isPersistent: false, lockoutOnFailure: true);

                if (result.IsLockedOut)
                {
                    ErrorMessage = "Too many attempts. Please try again in a few minutes.";
                    return Page();
                }
                if (!result.Succeeded)
                {
                    ErrorMessage = generic;
                    return Page();
                }

                return RedirectToPage("/Index");
            }
            else
            {
                // Employee flow
                if (string.IsNullOrWhiteSpace(Input.Identifier))
                {
                    ErrorMessage = "Please enter your employee ID.";
                    EmployeeStep = 1;
                    return Page();
                }

                var user = await _users.FindByNameAsync(Input.Identifier.Trim())
                           ?? await _users.FindByEmailAsync(Input.Identifier.Trim());

                if (user is null || !await _users.IsInRoleAsync(user, "Employee"))
                {
                    ErrorMessage = "Employee ID not found. Please verify your ID.";
                    EmployeeStep = 1;
                    return Page();
                }

                if (EmployeeStep == 1 || string.IsNullOrWhiteSpace(Input.Password))
                {
                    EmployeeStep = 2;
                    return Page();
                }

                var result = await _signIn.PasswordSignInAsync(
                    user, Input.Password, isPersistent: false, lockoutOnFailure: true);

                if (result.IsLockedOut)
                {
                    ErrorMessage = "Too many attempts. Please try again in a few minutes.";
                    EmployeeStep = 2;
                    return Page();
                }
                if (!result.Succeeded)
                {
                    ErrorMessage = generic;
                    EmployeeStep = 2;
                    return Page();
                }

                return RedirectToPage("/Clock");
            }
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException?.Message ?? ex.Message;
            if (msg.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
            {
                ErrorMessage = "Database tables are not set up yet in Supabase. Run the SQL script in your Supabase SQL Editor.";
            }
            else if (msg.Contains("unreachable", StringComparison.OrdinalIgnoreCase) || msg.Contains("Network", StringComparison.OrdinalIgnoreCase))
            {
                ErrorMessage = "Database host unreachable (IPv6). On Render, use your Supabase Session Pooler host (aws-0-...pooler.supabase.com) instead of db.*.supabase.co.";
            }
            else
            {
                ErrorMessage = $"Database connection error: {msg}";
            }
            return Page();
        }
    }

    // Direct Kiosk Clock Punch handler directly from Login page (Time In / Lunch Out / Lunch In / Time Out)
    public async Task<IActionResult> OnPostKioskPunchAsync([FromForm] string employeeId, [FromForm] PunchType punchType)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(employeeId))
            {
                KioskErrorMessage = "Please enter your Employee ID to clock.";
                return RedirectToPage();
            }

            var empIdClean = employeeId.Trim();
            var user = await _users.FindByNameAsync(empIdClean) ?? await _users.FindByEmailAsync(empIdClean);
            
            Worker? worker = null;
            if (user != null)
            {
                worker = await _db.Workers.FirstOrDefaultAsync(w => w.UserId == user.Id && w.IsActive);
            }
            if (worker == null)
            {
                worker = await _db.Workers.FirstOrDefaultAsync(w => 
                    (w.FullName.ToLower() == empIdClean.ToLower() || ("EMP-" + w.Id.ToString("D3")).ToLower() == empIdClean.ToLower()) && w.IsActive);
            }

            if (worker == null)
            {
                KioskErrorMessage = $"Invalid Employee ID '{employeeId}'. Please verify your credentials.";
                return RedirectToPage();
            }

            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            var result = await _punchService.PunchAsync(worker.BusinessId, worker.Id, punchType, clientIp);

            if (!result.Ok)
            {
                KioskErrorMessage = $"{worker.FullName}: {result.Message}";
            }
            else
            {
                KioskSuccessMessage = $"✓ {worker.FullName} - {punchType} recorded successfully at {DateTime.UtcNow.AddHours(8):hh:mm tt}!";
            }
        }
        catch (Exception ex)
        {
            KioskErrorMessage = $"Punch error: {ex.Message}";
        }

        return RedirectToPage();
    }

    // AJAX Kiosk Punch endpoint (returns instant JSON so invalid ID errors show immediately inside modal)
    public async Task<IActionResult> OnPostKioskPunchJsonAsync([FromForm] string employeeId, [FromForm] PunchType punchType)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(employeeId))
            {
                return new JsonResult(new { ok = false, message = "Please enter your Employee ID to clock." });
            }

            var empIdClean = employeeId.Trim();
            var user = await _users.FindByNameAsync(empIdClean) ?? await _users.FindByEmailAsync(empIdClean);

            Worker? worker = null;
            if (user != null)
            {
                worker = await _db.Workers.FirstOrDefaultAsync(w => w.UserId == user.Id && w.IsActive);
            }
            if (worker == null)
            {
                worker = await _db.Workers.FirstOrDefaultAsync(w =>
                    (w.FullName.ToLower() == empIdClean.ToLower() || ("EMP-" + w.Id.ToString("D3")).ToLower() == empIdClean.ToLower()) && w.IsActive);
            }

            if (worker == null)
            {
                return new JsonResult(new { ok = false, message = $"Invalid Employee ID '{employeeId}'. No active employee found with that ID." });
            }

            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            var result = await _punchService.PunchAsync(worker.BusinessId, worker.Id, punchType, clientIp);

            if (!result.Ok)
            {
                return new JsonResult(new { ok = false, message = $"{worker.FullName}: {result.Message}" });
            }

            return new JsonResult(new
            {
                ok = true,
                message = $"✓ {worker.FullName} — {punchType} recorded successfully at {DateTime.UtcNow.AddHours(8):hh:mm tt}!"
            });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { ok = false, message = $"Punch error: {ex.Message}" });
        }
    }
}
