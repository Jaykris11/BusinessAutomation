using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PayrollApp.Web.Data;

namespace PayrollApp.Web.Pages;

[Authorize]
public class MyRequestsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly UserManager<IdentityUser> _users;

    public MyRequestsModel(AppDbContext db, UserManager<IdentityUser> users)
    {
        _db = db;
        _users = users;
    }

    public Worker? CurrentWorker { get; set; }
    public string WorkerName { get; set; } = "";
    public string EmployeeId { get; set; } = "";
    public List<LeaveRequest> MyLeaves { get; set; } = new();
    public List<AdvancePaymentRequest> MyAdvances { get; set; } = new();
    public Dictionary<string, int> DateSlotCounts { get; set; } = new();

    [TempData] public string? SuccessMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return RedirectToPage("/Login");

        EmployeeId = user.UserName ?? "EMP-001";
        CurrentWorker = await _db.Workers.FirstOrDefaultAsync(w => w.UserId == user.Id && w.IsActive)
                     ?? await _db.Workers.FirstOrDefaultAsync(w => w.IsActive);

        if (CurrentWorker == null) return RedirectToPage("/Login");
        WorkerName = CurrentWorker.FullName;

        MyLeaves = await _db.LeaveRequests
            .Where(l => l.WorkerId == CurrentWorker.Id)
            .OrderByDescending(l => l.CreatedAtUtc)
            .ToListAsync();

        MyAdvances = await _db.AdvancePaymentRequests
            .Where(a => a.WorkerId == CurrentWorker.Id)
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToListAsync();

        var todayUtcDate = DateTime.UtcNow.Date;
        var upcoming = await _db.LeaveRequests
            .Where(l => l.BusinessId == CurrentWorker.BusinessId && l.Status != LeaveStatus.Rejected && l.TargetDate >= todayUtcDate)
            .ToListAsync();

        DateSlotCounts = upcoming
            .GroupBy(l => l.TargetDate.ToString("yyyy-MM-dd"))
            .ToDictionary(g => g.Key, g => g.Count());

        return Page();
    }

    public async Task<IActionResult> OnPostRequestLeaveAsync([FromForm] int leaveType, [FromForm] DateTime targetDate, [FromForm] string reason)
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return RedirectToPage("/Login");

        var worker = await _db.Workers.FirstOrDefaultAsync(w => w.UserId == user.Id && w.IsActive)
                     ?? await _db.Workers.FirstOrDefaultAsync(w => w.IsActive);
        if (worker == null) return RedirectToPage("/Login");

        if (string.IsNullOrWhiteSpace(reason))
        {
            ErrorMessage = "You must specify a reason for your leave request.";
            return RedirectToPage();
        }

        var dateUtc = DateTime.SpecifyKind(targetDate.Date, DateTimeKind.Utc);
        var nextDayUtc = dateUtc.AddDays(1);

        var existingCount = await _db.LeaveRequests
            .CountAsync(l => l.BusinessId == worker.BusinessId && l.TargetDate >= dateUtc && l.TargetDate < nextDayUtc && l.Status != LeaveStatus.Rejected);

        const int MAX_SLOTS = 2;
        if (existingCount >= MAX_SLOTS)
        {
            ErrorMessage = $"⚠️ Date {dateUtc:MMM dd, yyyy} is full ({existingCount}/{MAX_SLOTS} slots taken). Pick another date.";
            return RedirectToPage();
        }

        var leave = new LeaveRequest
        {
            BusinessId = worker.BusinessId,
            WorkerId = worker.Id,
            Type = (LeaveType)leaveType,
            TargetDate = dateUtc,
            Reason = reason.Trim(),
            Status = LeaveStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.LeaveRequests.Add(leave);
        await _db.SaveChangesAsync();

        SuccessMessage = $"✓ Leave request for {dateUtc:MMM dd, yyyy} ({leave.Type}) submitted successfully!";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRequestAdvanceAsync([FromForm] decimal amount, [FromForm] string reason)
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return RedirectToPage("/Login");

        var worker = await _db.Workers.FirstOrDefaultAsync(w => w.UserId == user.Id && w.IsActive)
                     ?? await _db.Workers.FirstOrDefaultAsync(w => w.IsActive);
        if (worker == null) return RedirectToPage("/Login");

        if (amount <= 0 || string.IsNullOrWhiteSpace(reason))
        {
            ErrorMessage = "Please provide a valid advance amount and clear reason.";
            return RedirectToPage();
        }

        var adv = new AdvancePaymentRequest
        {
            BusinessId = worker.BusinessId,
            WorkerId = worker.Id,
            AmountRequested = amount,
            Reason = reason.Trim(),
            Status = AdvanceStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.AdvancePaymentRequests.Add(adv);
        await _db.SaveChangesAsync();

        SuccessMessage = $"✓ Cash advance request for ₱{amount:N2} submitted for owner approval.";
        return RedirectToPage();
    }
}
