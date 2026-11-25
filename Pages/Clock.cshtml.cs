using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PayrollApp.Web.Data;
using PayrollApp.Web.Services;

namespace PayrollApp.Web.Pages;

[Authorize]
public class ClockModel : PageModel
{
    private readonly SignInManager<IdentityUser> _signIn;
    private readonly UserManager<IdentityUser> _users;
    private readonly AppDbContext _db;
    private readonly PunchService _punchService;

    public ClockModel(
        SignInManager<IdentityUser> signIn,
        UserManager<IdentityUser> users,
        AppDbContext db,
        PunchService punchService)
    {
        _signIn = signIn;
        _users = users;
        _db = db;
        _punchService = punchService;
    }

    public Worker? CurrentWorker { get; set; }
    public string WorkerName { get; set; } = "";
    public string EmployeeId { get; set; } = "";
    public PunchType? NextPunch { get; set; }
    public decimal HoursWorkedToday { get; set; } = 0;
    public decimal GrossEarnedToday { get; set; } = 0;
    public decimal TotalAccumulatedWages { get; set; } = 0; // Total period or all-time earnings
    public List<Punch> TodayPunches { get; set; } = new();
    public List<AdvancePaymentRequest> MyAdvances { get; set; } = new();
    public List<LeaveRequest> MyLeaves { get; set; } = new();
    public List<Payslip> MyPayslips { get; set; } = new();
    public List<Announcement> StoreAnnouncements { get; set; } = new();
    public Dictionary<string, int> DateSlotCounts { get; set; } = new(); // Date string -> count of leaves on that day

    public string ClientIp { get; set; } = "";
    public bool IsIpAllowed { get; set; } = true;
    public string CurrentShiftName { get; set; } = "Morning Shift";

    [TempData] public string? SuccessMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return RedirectToPage("/Login");

        EmployeeId = user.UserName ?? "EMP-001";

        // Find worker record linked to this identity user
        CurrentWorker = await _db.Workers.FirstOrDefaultAsync(w => w.UserId == user.Id && w.IsActive);
        if (CurrentWorker == null)
        {
            // Fallback: match by full name or username
            CurrentWorker = await _db.Workers.FirstOrDefaultAsync(w => w.IsActive);
        }

        if (CurrentWorker == null)
        {
            ErrorMessage = "Worker profile not found. Please contact your manager.";
            return Page();
        }

        WorkerName = CurrentWorker.FullName;

        // Remote IP address
        ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

        var biz = await _db.Businesses.Include(b => b.AllowedIps).FirstOrDefaultAsync(b => b.Id == CurrentWorker.BusinessId);
        if (biz != null && biz.RequireIpForPunch)
        {
            IsIpAllowed = biz.AllowedIps.Any(a => a.Address == ClientIp || ClientIp == "127.0.0.1" || ClientIp == "::1");
        }

        // Shift calculation
        var phNow = DateTime.UtcNow.AddHours(8);
        var phHour = phNow.Hour;
        if (phHour >= 6 && phHour < 14) CurrentShiftName = "Morning (06:00 - 14:00)";
        else if (phHour >= 14 && phHour < 22) CurrentShiftName = "Afternoon (14:00 - 22:00)";
        else CurrentShiftName = "Night (22:00 - 06:00)";

        // Get Today's Range in PH time
        var (startUtc, endUtc) = PunchService.TodayRangeUtc(DateTime.UtcNow);
        TodayPunches = await _db.Punches
            .Where(p => p.BusinessId == CurrentWorker.BusinessId && p.WorkerId == CurrentWorker.Id && p.TimestampUtc >= startUtc && p.TimestampUtc < endUtc)
            .OrderBy(p => p.TimestampUtc)
            .ToListAsync();

        var workedTime = PayCalculator.WorkedTime(TodayPunches, DateTime.UtcNow);
        HoursWorkedToday = Math.Round((decimal)workedTime.TotalHours, 2);
        GrossEarnedToday = PayCalculator.Earned(workedTime, CurrentWorker.HourlyRate);

        // Calculate all-time / month-to-date accumulated earnings for this worker
        var monthStartUtc = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var allMonthPunches = await _db.Punches
            .Where(p => p.BusinessId == CurrentWorker.BusinessId && p.WorkerId == CurrentWorker.Id && p.TimestampUtc >= monthStartUtc)
            .ToListAsync();
        var totalWorkedTime = PayCalculator.WorkedTime(allMonthPunches, DateTime.UtcNow);
        TotalAccumulatedWages = PayCalculator.Earned(totalWorkedTime, CurrentWorker.HourlyRate);

        // Fetch Employee's Vale / Cash Advance requests
        MyAdvances = await _db.AdvancePaymentRequests
            .Where(a => a.WorkerId == CurrentWorker.Id)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(10)
            .ToListAsync();

        // Fetch Employee's Leave / PTO requests
        MyLeaves = await _db.LeaveRequests
            .Where(l => l.WorkerId == CurrentWorker.Id)
            .OrderByDescending(l => l.CreatedAtUtc)
            .Take(10)
            .ToListAsync();

        // Fetch Employee's Payslips issued by Owner
        MyPayslips = await _db.Payslips
            .Where(p => p.WorkerId == CurrentWorker.Id)
            .OrderByDescending(p => p.IssuedAtUtc)
            .Take(10)
            .ToListAsync();

        // Fetch Owner Announcements / Memos
        StoreAnnouncements = await _db.Announcements
            .Where(a => a.BusinessId == CurrentWorker.BusinessId && a.IsActive)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(5)
            .ToListAsync();

        // Calculate leave slot availability (e.g. limit to 2 workers on leave per day)
        var todayUtcDate = DateTime.UtcNow.Date;
        var upcomingLeaves = await _db.LeaveRequests
            .Where(l => l.BusinessId == CurrentWorker.BusinessId && l.Status != LeaveStatus.Rejected && l.TargetDate >= todayUtcDate)
            .ToListAsync();
        DateSlotCounts = upcomingLeaves
            .GroupBy(l => l.TargetDate.ToString("yyyy-MM-dd"))
            .ToDictionary(g => g.Key, g => g.Count());

        return Page();
    }

    // Live real-time polling endpoint for Employee Dashboard
    public async Task<IActionResult> OnGetLiveMetricsAsync()
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return new JsonResult(new { ok = false });

        var worker = await _db.Workers.FirstOrDefaultAsync(w => w.UserId == user.Id && w.IsActive)
                     ?? await _db.Workers.FirstOrDefaultAsync(w => w.IsActive);

        if (worker == null) return new JsonResult(new { ok = false });

        var nowUtc = DateTime.UtcNow;
        var (startUtc, endUtc) = PunchService.TodayRangeUtc(nowUtc);
        var todayPunches = await _db.Punches
            .Where(p => p.BusinessId == worker.BusinessId && p.WorkerId == worker.Id && p.TimestampUtc >= startUtc && p.TimestampUtc < endUtc)
            .OrderBy(p => p.TimestampUtc)
            .ToListAsync();

        var workedTime = PayCalculator.WorkedTime(todayPunches, nowUtc);
        var hoursToday = Math.Round((decimal)workedTime.TotalHours, 2);
        var grossToday = PayCalculator.Earned(workedTime, worker.HourlyRate);

        var monthStartUtc = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var allMonthPunches = await _db.Punches
            .Where(p => p.BusinessId == worker.BusinessId && p.WorkerId == worker.Id && p.TimestampUtc >= monthStartUtc)
            .ToListAsync();
        var totalWorkedTime = PayCalculator.WorkedTime(allMonthPunches, nowUtc);
        var accumulatedWages = PayCalculator.Earned(totalWorkedTime, worker.HourlyRate);

        var lastPunch = todayPunches.LastOrDefault();
        var isOnClock = lastPunch != null && (lastPunch.Type == PunchType.TimeIn || lastPunch.Type == PunchType.LunchIn);

        return new JsonResult(new
        {
            ok = true,
            hoursToday = hoursToday.ToString("0.00"),
            rawHours = workedTime.TotalHours,
            grossToday = grossToday.ToString("N2"),
            accumulatedWages = accumulatedWages.ToString("N2"),
            hourlyRate = worker.HourlyRate,
            isOnClock,
            punchesCount = todayPunches.Count
        });
    }

    public async Task<IActionResult> OnPostPunchAsync([FromForm] PunchType requestedPunch)
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return RedirectToPage("/Login");

        var worker = await _db.Workers.FirstOrDefaultAsync(w => w.UserId == user.Id && w.IsActive)
                     ?? await _db.Workers.FirstOrDefaultAsync(w => w.IsActive);

        if (worker == null)
        {
            ErrorMessage = "Worker not found.";
            return RedirectToPage();
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        var result = await _punchService.PunchAsync(worker.BusinessId, worker.Id, requestedPunch, ip);

        if (result.Ok)
        {
            SuccessMessage = $"✓ {requestedPunch} recorded successfully!";
        }
        else
        {
            ErrorMessage = result.Message;
        }

        return RedirectToPage();
    }

    // Submit Cash Advance (Vale) Request
    public async Task<IActionResult> OnPostRequestAdvanceAsync([FromForm] decimal amount, [FromForm] string reason)
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return RedirectToPage("/Login");

        var worker = await _db.Workers.FirstOrDefaultAsync(w => w.UserId == user.Id && w.IsActive)
                     ?? await _db.Workers.FirstOrDefaultAsync(w => w.IsActive);

        if (worker == null)
        {
            ErrorMessage = "Worker profile not found.";
            return RedirectToPage();
        }

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

    // Submit Leave / PTO / VTO Request with Slot Limit Checking
    public async Task<IActionResult> OnPostRequestLeaveAsync([FromForm] int leaveType, [FromForm] DateTime targetDate, [FromForm] string reason)
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return RedirectToPage("/Login");

        var worker = await _db.Workers.FirstOrDefaultAsync(w => w.UserId == user.Id && w.IsActive)
                     ?? await _db.Workers.FirstOrDefaultAsync(w => w.IsActive);

        if (worker == null)
        {
            ErrorMessage = "Worker profile not found.";
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            ErrorMessage = "You must specify a reason for your leave request.";
            return RedirectToPage();
        }

        // Convert targetDate cleanly to UTC
        var dateUtc = DateTime.SpecifyKind(targetDate.Date, DateTimeKind.Utc);
        var nextDayUtc = dateUtc.AddDays(1);

        var existingCount = await _db.LeaveRequests
            .CountAsync(l => l.BusinessId == worker.BusinessId && l.TargetDate >= dateUtc && l.TargetDate < nextDayUtc && l.Status != LeaveStatus.Rejected);

        const int MAX_SLOTS_PER_DAY = 2; // Maximum 2 staff members on leave per day
        if (existingCount >= MAX_SLOTS_PER_DAY)
        {
            ErrorMessage = $"⚠️ Date {dateUtc:MMM dd, yyyy} is full ({existingCount}/{MAX_SLOTS_PER_DAY} slots taken). No more leave slots available for that day.";
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

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await _signIn.SignOutAsync();
        return RedirectToPage("/Login");
    }
}
