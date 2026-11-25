using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PayrollApp.Web.Data;
using PayrollApp.Web.Services;

namespace PayrollApp.Web.Pages;

[Authorize]
public class PayslipsModel : PageModel
{
    private readonly AppDbContext _db;

    public PayslipsModel(AppDbContext db) => _db = db;

    public List<Payslip> IssuedPayslips { get; set; } = new();
    public List<Worker> ActiveWorkers { get; set; } = new();
    public Dictionary<int, string> WorkerNames { get; set; } = new();
    public Dictionary<int, decimal> WorkerHoursWorkedMonth { get; set; } = new();
    public Dictionary<int, decimal> WorkerPendingAdvances { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string ViewMode { get; set; } = "active"; // active | archived

    [BindProperty(SupportsGet = true)]
    public int FilterYear { get; set; } = DateTime.UtcNow.Year;

    [BindProperty(SupportsGet = true)]
    public string Range { get; set; } = "monthly"; // daily | weekly | monthly | yearly

    public int ArchivedCount { get; set; } = 0;

    [TempData] public string? SuccessMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var biz = await _db.Businesses.FirstOrDefaultAsync();
        var bizId = biz?.Id ?? 1;

        ActiveWorkers = await _db.Workers
            .Where(w => w.BusinessId == bizId && w.IsActive)
            .OrderBy(w => w.FullName)
            .ToListAsync();

        WorkerNames = ActiveWorkers.ToDictionary(w => w.Id, w => w.FullName);

        var nowUtc = DateTime.UtcNow;
        var monthStartUtc = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var punches = await _db.Punches
            .Where(p => p.BusinessId == bizId && p.TimestampUtc >= monthStartUtc)
            .ToListAsync();

        var advances = await _db.AdvancePaymentRequests
            .Where(a => a.BusinessId == bizId && a.Status == AdvanceStatus.Approved)
            .ToListAsync();

        WorkerHoursWorkedMonth = ActiveWorkers.ToDictionary(
            w => w.Id,
            w => Math.Round((decimal)PayCalculator.WorkedTime(punches.Where(p => p.WorkerId == w.Id).OrderBy(p => p.TimestampUtc).ToList(), nowUtc).TotalHours, 2)
        );

        WorkerPendingAdvances = ActiveWorkers.ToDictionary(
            w => w.Id,
            w => advances.Where(a => a.WorkerId == w.Id).Sum(a => a.AmountRequested)
        );

        var yearStart = new DateTime(FilterYear, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var yearEnd = yearStart.AddYears(1);

        ArchivedCount = await _db.Payslips.CountAsync(p => p.BusinessId == bizId && p.IsArchived);

        var query = _db.Payslips
            .Where(p => p.BusinessId == bizId && p.IssuedAtUtc >= yearStart && p.IssuedAtUtc < yearEnd);

        query = ViewMode == "archived"
            ? query.Where(p => p.IsArchived)
            : query.Where(p => !p.IsArchived);

        if (Range == "daily")
            query = query.Where(p => p.IssuedAtUtc >= nowUtc.Date);
        else if (Range == "weekly")
            query = query.Where(p => p.IssuedAtUtc >= nowUtc.AddDays(-7));
        else if (Range == "monthly")
            query = query.Where(p => p.IssuedAtUtc >= monthStartUtc);

        IssuedPayslips = await query
            .OrderByDescending(p => p.IssuedAtUtc)
            .ToListAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostToggleArchiveAsync([FromForm] int payslipId, [FromForm] string viewMode, [FromForm] int filterYear, [FromForm] string range)
    {
        var ps = await _db.Payslips.FirstOrDefaultAsync(p => p.Id == payslipId);
        if (ps != null)
        {
            ps.IsArchived = !ps.IsArchived;
            await _db.SaveChangesAsync();
            SuccessMessage = ps.IsArchived ? "✓ Payment slip moved to Archives." : "✓ Payment slip restored from Archives.";
        }
        return RedirectToPage(new { ViewMode = viewMode, FilterYear = filterYear, Range = range });
    }

    public async Task<IActionResult> OnPostIssuePayslipAsync(
        [FromForm] int workerId,
        [FromForm] string payPeriod,
        [FromForm] decimal hoursWorked,
        [FromForm] decimal hourlyRate,
        [FromForm] decimal advanceDeductions,
        [FromForm] decimal otherDeductions,
        [FromForm] string paymentMethod,
        [FromForm] string? remarks)
    {
        var worker = await _db.Workers.FirstOrDefaultAsync(w => w.Id == workerId);
        if (worker == null)
        {
            ErrorMessage = "Selected worker was not found.";
            return RedirectToPage();
        }

        var gross = hoursWorked * (hourlyRate > 0 ? hourlyRate : worker.HourlyRate);
        var net = Math.Max(0, gross - advanceDeductions - otherDeductions);

        var payslip = new Payslip
        {
            BusinessId = worker.BusinessId,
            WorkerId = worker.Id,
            PayPeriod = string.IsNullOrWhiteSpace(payPeriod) ? "Current Pay Period" : payPeriod.Trim(),
            HoursWorked = hoursWorked,
            HourlyRate = hourlyRate > 0 ? hourlyRate : worker.HourlyRate,
            GrossPay = gross,
            CashAdvanceDeductions = advanceDeductions,
            OtherDeductions = otherDeductions,
            NetPay = net,
            PaymentMethod = string.IsNullOrWhiteSpace(paymentMethod) ? "Cash" : paymentMethod,
            Remarks = remarks,
            IssuedAtUtc = DateTime.UtcNow
        };

        _db.Payslips.Add(payslip);

        // If cash advance deductions applied, mark matching pending/approved advances as PaidOut
        if (advanceDeductions > 0)
        {
            var adv = await _db.AdvancePaymentRequests
                .Where(a => a.WorkerId == worker.Id && a.Status == AdvanceStatus.Approved)
                .OrderBy(a => a.CreatedAtUtc)
                .FirstOrDefaultAsync();
            if (adv != null) adv.Status = AdvanceStatus.PaidOut;
        }

        await _db.SaveChangesAsync();

        SuccessMessage = $"✓ Payslip for {worker.FullName} (Net: ₱{net:N2}) issued and sent to their portal!";
        return RedirectToPage();
    }
}
