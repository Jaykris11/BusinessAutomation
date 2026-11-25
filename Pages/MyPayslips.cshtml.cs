using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PayrollApp.Web.Data;

namespace PayrollApp.Web.Pages;

[Authorize]
public class MyPayslipsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly UserManager<IdentityUser> _users;

    public MyPayslipsModel(AppDbContext db, UserManager<IdentityUser> users)
    {
        _db = db;
        _users = users;
    }

    public Worker? CurrentWorker { get; set; }
    public string WorkerName { get; set; } = "";
    public string EmployeeId { get; set; } = "";
    public List<Payslip> Payslips { get; set; } = new();
    public decimal TotalNetEarningsReceived { get; set; } = 0;

    [TempData] public string? SuccessMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return RedirectToPage("/Login");

        EmployeeId = user.UserName ?? "EMP-001";
        CurrentWorker = await _db.Workers.FirstOrDefaultAsync(w => w.UserId == user.Id && w.IsActive)
                     ?? await _db.Workers.FirstOrDefaultAsync(w => w.IsActive);

        if (CurrentWorker == null) return RedirectToPage("/Login");
        WorkerName = CurrentWorker.FullName;

        Payslips = await _db.Payslips
            .Where(p => p.WorkerId == CurrentWorker.Id)
            .OrderByDescending(p => p.IssuedAtUtc)
            .ToListAsync();

        TotalNetEarningsReceived = Payslips.Sum(p => p.NetPay);

        return Page();
    }

    public async Task<IActionResult> OnPostAcknowledgeAsync([FromForm] int payslipId)
    {
        var ps = await _db.Payslips.FirstOrDefaultAsync(p => p.Id == payslipId);
        if (ps != null)
        {
            ps.IsAcknowledged = true;
            ps.AcknowledgedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            SuccessMessage = $"✓ Payslip for {ps.PayPeriod} acknowledged.";
        }
        return RedirectToPage();
    }
}
