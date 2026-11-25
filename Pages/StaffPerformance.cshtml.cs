using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PayrollApp.Web.Data;
using PayrollApp.Web.Services;

namespace PayrollApp.Web.Pages;

[Authorize]
public class StaffPerformanceModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly UserManager<IdentityUser> _users;

    public StaffPerformanceModel(AppDbContext db, UserManager<IdentityUser> users)
    {
        _db = db;
        _users = users;
    }

    [BindProperty(SupportsGet = true)]
    public string Range { get; set; } = "monthly";

    public class WorkerPerformanceRow
    {
        public int WorkerId { get; set; }
        public string FullName { get; set; } = "";
        public string EmployeeId { get; set; } = "";
        public PayType PayType { get; set; }
        public decimal Rate { get; set; }
        public decimal HoursWorked { get; set; }
        public decimal EarnedAmount { get; set; }
        public int TotalPunches { get; set; }
        public int ShiftsCompleted { get; set; }
        public int Rank { get; set; }
    }

    public List<WorkerPerformanceRow> Leaderboard { get; set; } = new();
    public decimal TotalTeamHours { get; set; } = 0;
    public decimal TotalPayrollWages { get; set; } = 0;
    public string TopPerformerName { get; set; } = "None";

    public async Task<IActionResult> OnGetAsync()
    {
        var biz = await _db.Businesses.FirstOrDefaultAsync();
        var bizId = biz?.Id ?? 1;

        var nowUtc = DateTime.UtcNow;
        DateTime startUtc = Range.ToLower() switch
        {
            "daily" => nowUtc.Date,
            "weekly" => nowUtc.AddDays(-7),
            "yearly" => nowUtc.AddYears(-1),
            _ => new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc) // Monthly
        };

        var workers = await _db.Workers
            .Where(w => w.BusinessId == bizId && w.IsActive)
            .ToListAsync();

        var punches = await _db.Punches
            .Where(p => p.BusinessId == bizId && p.TimestampUtc >= startUtc)
            .ToListAsync();

        var userIds = workers.Where(w => !string.IsNullOrEmpty(w.UserId)).Select(w => w.UserId).ToList();
        var idMap = await _users.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName ?? "");

        var list = new List<WorkerPerformanceRow>();

        foreach (var w in workers)
        {
            var wPunches = punches.Where(p => p.WorkerId == w.Id).OrderBy(p => p.TimestampUtc).ToList();
            var workedTime = PayCalculator.WorkedTime(wPunches, nowUtc);
            var hours = Math.Round((decimal)workedTime.TotalHours, 2);
            var earned = PayCalculator.Earned(workedTime, w.HourlyRate);
            var timeOuts = wPunches.Count(p => p.Type == PunchType.TimeOut);

            string empId = w.UserId != null && idMap.TryGetValue(w.UserId, out var u) ? u : $"EMP-{w.Id:D3}";

            list.Add(new WorkerPerformanceRow
            {
                WorkerId = w.Id,
                FullName = w.FullName,
                EmployeeId = empId,
                PayType = w.PayType,
                Rate = w.Rate,
                HoursWorked = hours,
                EarnedAmount = earned,
                TotalPunches = wPunches.Count,
                ShiftsCompleted = timeOuts
            });
        }

        // Rank by Hours Worked descending
        Leaderboard = list.OrderByDescending(x => x.HoursWorked).ToList();
        for (int i = 0; i < Leaderboard.Count; i++)
        {
            Leaderboard[i].Rank = i + 1;
        }

        TotalTeamHours = Leaderboard.Sum(x => x.HoursWorked);
        TotalPayrollWages = Leaderboard.Sum(x => x.EarnedAmount);
        TopPerformerName = Leaderboard.FirstOrDefault()?.FullName ?? "None";

        return Page();
    }

    // Real-Time JSON Endpoint for live ticker / auto-refresh
    public async Task<IActionResult> OnGetDataAsync([FromQuery] string range)
    {
        Range = string.IsNullOrWhiteSpace(range) ? "monthly" : range;
        await OnGetAsync();
        return new JsonResult(new
        {
            totalTeamHours = TotalTeamHours,
            totalPayrollWages = TotalPayrollWages,
            topPerformerName = TopPerformerName,
            leaderboard = Leaderboard
        });
    }
}
