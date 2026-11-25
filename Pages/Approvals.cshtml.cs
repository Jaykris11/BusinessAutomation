using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PayrollApp.Web.Data;

namespace PayrollApp.Web.Pages;

[Authorize]
public class ApprovalsModel : PageModel
{
    private readonly AppDbContext _db;

    public ApprovalsModel(AppDbContext db) => _db = db;

    public List<LeaveRequest> LeaveRequests { get; set; } = new();
    public List<AdvancePaymentRequest> AdvanceRequests { get; set; } = new();
    public Dictionary<int, string> WorkerNames { get; set; } = new();

    [TempData] public string? SuccessMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var biz = await _db.Businesses.FirstOrDefaultAsync();
        var bizId = biz?.Id ?? 1;

        WorkerNames = await _db.Workers
            .Where(w => w.BusinessId == bizId)
            .ToDictionaryAsync(w => w.Id, w => w.FullName);

        LeaveRequests = await _db.LeaveRequests
            .Where(l => l.BusinessId == bizId)
            .OrderByDescending(l => l.CreatedAtUtc)
            .ToListAsync();

        AdvanceRequests = await _db.AdvancePaymentRequests
            .Where(a => a.BusinessId == bizId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToListAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostReviewLeaveAsync([FromForm] int leaveId, [FromForm] int status, [FromForm] string? note)
    {
        var leave = await _db.LeaveRequests.FirstOrDefaultAsync(l => l.Id == leaveId);
        if (leave != null)
        {
            leave.Status = (LeaveStatus)status;
            leave.OwnerNote = note;
            leave.ReviewedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            SuccessMessage = $"Leave request marked as {leave.Status}.";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostReviewAdvanceAsync([FromForm] int advanceId, [FromForm] int status, [FromForm] string? note)
    {
        var adv = await _db.AdvancePaymentRequests.FirstOrDefaultAsync(a => a.Id == advanceId);
        if (adv != null)
        {
            adv.Status = (AdvanceStatus)status;
            adv.OwnerNote = note;
            adv.ReviewedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            SuccessMessage = $"Cash advance marked as {adv.Status}.";
        }
        return RedirectToPage();
    }
}
