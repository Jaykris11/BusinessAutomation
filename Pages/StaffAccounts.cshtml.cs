using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PayrollApp.Web.Data;

namespace PayrollApp.Web.Pages;

[Authorize(Roles = "Owner")]
public class StaffAccountsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly UserManager<IdentityUser> _users;
    private readonly RoleManager<IdentityRole> _roles;

    public StaffAccountsModel(AppDbContext db, UserManager<IdentityUser> users, RoleManager<IdentityRole> roles)
    {
        _db = db;
        _users = users;
        _roles = roles;
    }

    public class StaffAccountRow
    {
        public int WorkerId { get; set; }
        public string EmployeeId { get; set; } = "";
        public string FullName { get; set; } = "";
        public PayType PayType { get; set; }
        public decimal Rate { get; set; }
        public string ShiftStart { get; set; } = "09:00";
        public string ShiftEnd { get; set; } = "18:00";
        public string LunchStart { get; set; } = "12:00";
        public string LunchEnd { get; set; } = "13:00";
        public bool IsActive { get; set; }
    }

    public List<StaffAccountRow> Accounts { get; set; } = new();

    [TempData] public string? SuccessMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var biz = await _db.Businesses.FirstOrDefaultAsync();
        var bizId = biz?.Id ?? 1;

        var workers = await _db.Workers
            .Where(w => w.BusinessId == bizId)
            .OrderByDescending(w => w.IsActive)
            .ThenBy(w => w.FullName)
            .ToListAsync();

        var userIds = workers.Where(w => !string.IsNullOrEmpty(w.UserId)).Select(w => w.UserId).ToList();
        var userMap = await _users.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName ?? "");

        Accounts = workers.Select(w => new StaffAccountRow
        {
            WorkerId = w.Id,
            EmployeeId = w.UserId != null && userMap.TryGetValue(w.UserId, out var u) ? u : $"EMP-{w.Id:D3}",
            FullName = w.FullName,
            PayType = w.PayType,
            Rate = w.Rate,
            ShiftStart = w.ShiftStart,
            ShiftEnd = w.ShiftEnd,
            LunchStart = w.LunchStart,
            LunchEnd = w.LunchEnd,
            IsActive = w.IsActive
        }).ToList();

        return Page();
    }

    public async Task<IActionResult> OnPostCreateAccountAsync(
        [FromForm] string employeeId,
        [FromForm] string fullName,
        [FromForm] string password,
        [FromForm] int payType,
        [FromForm] decimal rate,
        [FromForm] string shiftStart,
        [FromForm] string shiftEnd,
        [FromForm] string lunchStart,
        [FromForm] string lunchEnd)
    {
        if (string.IsNullOrWhiteSpace(employeeId) || string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(password))
        {
            ErrorMessage = "Employee ID, Full Name, and Password are required.";
            return RedirectToPage();
        }

        var empIdClean = employeeId.Trim();
        var existingUser = await _users.FindByNameAsync(empIdClean);
        if (existingUser != null)
        {
            ErrorMessage = $"Employee ID '{empIdClean}' already exists.";
            return RedirectToPage();
        }

        if (password.Length < 6)
        {
            ErrorMessage = "Password must be at least 6 characters.";
            return RedirectToPage();
        }

        var user = new IdentityUser
        {
            UserName = empIdClean,
            Email = $"{empIdClean.ToLower().Replace(" ", "")}@laufey.com",
            EmailConfirmed = true
        };

        var createRes = await _users.CreateAsync(user, password);
        if (!createRes.Succeeded)
        {
            ErrorMessage = createRes.Errors.FirstOrDefault()?.Description ?? "Failed to create account.";
            return RedirectToPage();
        }

        if (!await _roles.RoleExistsAsync("Employee"))
            await _roles.CreateAsync(new IdentityRole("Employee"));

        await _users.AddToRoleAsync(user, "Employee");

        var biz = await _db.Businesses.FirstOrDefaultAsync();
        var bizId = biz?.Id ?? 1;

        var worker = new Worker
        {
            BusinessId = bizId,
            UserId = user.Id,
            FullName = fullName.Trim(),
            PayType = (PayType)payType,
            Rate = rate > 0 ? rate : 150m,
            HoursPerDay = 8m,
            IsActive = true,
            ShiftStart = string.IsNullOrWhiteSpace(shiftStart) ? "09:00" : shiftStart,
            ShiftEnd = string.IsNullOrWhiteSpace(shiftEnd) ? "18:00" : shiftEnd,
            LunchStart = string.IsNullOrWhiteSpace(lunchStart) ? "12:00" : lunchStart,
            LunchEnd = string.IsNullOrWhiteSpace(lunchEnd) ? "13:00" : lunchEnd
        };

        _db.Workers.Add(worker);
        await _db.SaveChangesAsync();

        SuccessMessage = $"✓ Created Employee Account '{empIdClean}' for {worker.FullName}!";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleStatusAsync([FromForm] int workerId)
    {
        var w = await _db.Workers.FirstOrDefaultAsync(x => x.Id == workerId);
        if (w != null)
        {
            w.IsActive = !w.IsActive;
            await _db.SaveChangesAsync();
            SuccessMessage = w.IsActive ? $"✓ Activated {w.FullName}." : $"✓ Deactivated {w.FullName}.";
        }
        return RedirectToPage();
    }
}
