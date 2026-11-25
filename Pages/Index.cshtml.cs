using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PayrollApp.Web.Data;
using PayrollApp.Web.Services;

namespace PayrollApp.Web.Pages;

public class IndexModel : PageModel
{
    private readonly SignInManager<IdentityUser> _signIn;
    private readonly UserManager<IdentityUser> _users;
    private readonly AppDbContext _db;

    public IndexModel(SignInManager<IdentityUser> signIn, UserManager<IdentityUser> users, AppDbContext db)
    {
        _signIn = signIn;
        _users = users;
        _db = db;
    }

    public string DisplayName { get; set; } = "";
    public string RoleName { get; set; } = "";
    public int BusinessId { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string Range { get; set; } = "daily";

    // Financial Metrics: Sales, Capital/Cost, Estimated Profit
    public decimal PeriodSales { get; set; } = 0;
    public decimal PeriodCapital { get; set; } = 0;   // Cost of goods + expenses
    public decimal PeriodProfit { get; set; } = 0;    // Sales - Capital
    public decimal ProfitMarginPct { get; set; } = 0;
    public int PeriodSalesCount { get; set; } = 0;

    // Workforce Metrics (Compact)
    public int TotalWorkers { get; set; } = 0;
    public int ClockedInCount { get; set; } = 0;
    public int AbsentCount { get; set; } = 0;
    public decimal LiveLaborCostRatePerHour { get; set; } = 0; // Cumulative hourly rate of currently clocked-in staff
    public decimal TotalLaborCostSoFar { get; set; } = 0;
    public decimal LaborCostRatioPct { get; set; } = 0; // Labor cost as % of Sales

    // Stock metrics (Compact)
    public int OverallStockCount { get; set; } = 0;
    public int LowStockAlertCount { get; set; } = 0;
    public List<Product> LowStockProducts { get; set; } = new();
    public List<Product> AllProducts { get; set; } = new();

    // Announcements & Cash Drawer
    public List<Announcement> Announcements { get; set; } = new();
    public CashDrawerClosing? LatestClosing { get; set; }

    // Phase 4: Pending Employee Leave Requests & Cash Advance Requests
    public List<LeaveRequest> PendingLeaveRequests { get; set; } = new();
    public List<AdvancePaymentRequest> PendingAdvanceRequests { get; set; } = new();

    public string CurrentStoreShift { get; set; } = "Morning Shift";
    public StoreShiftSession? ActiveShiftSession { get; set; }
    public bool IsShiftActive { get; set; } = false;

    public class WorkerAttendanceRow
    {
        public int WorkerId { get; set; }
        public string FullName { get; set; } = "";
        public string EmployeeId { get; set; } = "";
        public PayType PayType { get; set; }
        public decimal Rate { get; set; }
        public decimal HoursWorked { get; set; }
        public decimal EarnedAmount { get; set; }
        public bool IsClockedIn { get; set; }
        public string LatestPunch { get; set; } = "None";
        public string LatestPunchTime { get; set; } = "--";
        public string LiveStatus { get; set; } = "Not Yet In"; // Working | On Lunch Break | Clocked Out | Not Yet In
        public string ShiftStart { get; set; } = "09:00";
        public string ShiftEnd { get; set; } = "18:00";
        public string LunchStart { get; set; } = "12:00";
        public string LunchEnd { get; set; } = "13:00";
        public string ComplianceStatus { get; set; } = "Pending"; // On Schedule | Off-Schedule | Pending
        public string ComplianceNote { get; set; } = "Not yet clocked in";
    }

    public List<WorkerAttendanceRow> WorkerAttendanceList { get; set; } = new();

    public List<string> ChartLabels { get; set; } = new();
    public List<decimal> ChartSales { get; set; } = new();
    public List<decimal> ChartCapital { get; set; } = new();
    public List<decimal> ChartProfit { get; set; } = new();

    [TempData] public string? SuccessMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (User.Identity?.IsAuthenticated != true) return RedirectToPage("/Login");

        var user = await _users.GetUserAsync(User);
        DisplayName = user?.UserName ?? "Owner";
        RoleName = User.IsInRole("Owner") ? "Owner" : "Employee";

        var biz = await _db.Businesses.FirstOrDefaultAsync();
        if (biz != null) BusinessId = biz.Id;

        // Active Store Shift Session
        ActiveShiftSession = await _db.StoreShiftSessions
            .Where(s => s.BusinessId == BusinessId && s.IsActive)
            .OrderByDescending(s => s.StartedAtUtc)
            .FirstOrDefaultAsync();

        if (ActiveShiftSession != null)
        {
            IsShiftActive = true;
            CurrentStoreShift = $"{ActiveShiftSession.ShiftName} (Started {ActiveShiftSession.StartedAtUtc.AddHours(8):hh:mm tt})";
        }
        else
        {
            IsShiftActive = false;
            CurrentStoreShift = "Store Shift Ended / Off Duty";
        }

        await LoadDataAsync();
        return Page();
    }

    // Live real-time polling endpoint for Owner Dashboard
    public async Task<IActionResult> OnGetLiveStatsAsync()
    {
        var biz = await _db.Businesses.FirstOrDefaultAsync();
        var bizId = biz?.Id ?? 1;

        var nowUtc = DateTime.UtcNow;
        var (startUtc, endUtc) = PunchService.TodayRangeUtc(nowUtc);

        var workers = await _db.Workers.Where(w => w.BusinessId == bizId && w.IsActive).OrderBy(w => w.FullName).ToListAsync();
        var punches = await _db.Punches.Where(p => p.BusinessId == bizId && p.TimestampUtc >= startUtc && p.TimestampUtc < endUtc).ToListAsync();

        int clockedInCount = 0;
        decimal activeHourlyBurn = 0;
        decimal totalLaborCostToday = 0;
        var workerStates = new List<object>();

        foreach (var w in workers)
        {
            var wPunches = punches.Where(p => p.WorkerId == w.Id).OrderBy(p => p.TimestampUtc).ToList();
            var worked = PayCalculator.WorkedTime(wPunches, nowUtc);
            var earned = PayCalculator.Earned(worked, w.HourlyRate);
            totalLaborCostToday += earned;

            var last = wPunches.LastOrDefault();
            var liveStatus = last?.Type switch
            {
                PunchType.TimeIn or PunchType.LunchIn => "Working",
                PunchType.LunchOut => "On Lunch Break",
                PunchType.TimeOut => "Clocked Out",
                _ => "Not Yet In"
            };

            if (last != null && (last.Type == PunchType.TimeIn || last.Type == PunchType.LunchIn))
            {
                clockedInCount++;
                activeHourlyBurn += w.HourlyRate;
            }

            var (compStatus, compNote) = EvaluateCompliance(w, wPunches);

            workerStates.Add(new
            {
                workerId = w.Id,
                liveStatus,
                latestPunch = last != null ? last.Type.ToString() : "None",
                latestPunchTime = last != null ? last.TimestampUtc.AddHours(8).ToString("hh:mm tt") : "--",
                hoursWorked = Math.Round((decimal)worked.TotalHours, 2).ToString("0.00"),
                earnedAmount = earned.ToString("N2"),
                complianceStatus = compStatus,
                complianceNote = compNote
            });
        }

        var todaySalesTotal = await _db.Sales
            .Where(s => s.BusinessId == bizId && s.TimestampUtc >= startUtc && s.TimestampUtc < endUtc)
            .SumAsync(s => (decimal?)s.TotalAmount) ?? 0m;

        var salesCount = await _db.Sales
            .CountAsync(s => s.BusinessId == bizId && s.TimestampUtc >= startUtc && s.TimestampUtc < endUtc);

        return new JsonResult(new
        {
            ok = true,
            clockedInCount,
            totalWorkers = workers.Count,
            activeHourlyBurn = activeHourlyBurn.ToString("N2"),
            totalLaborCostToday = totalLaborCostToday.ToString("N2"),
            todaySalesTotal = todaySalesTotal.ToString("N2"),
            salesCount,
            workerStates
        });
    }

    public async Task<IActionResult> OnPostUpdateScheduleAsync([FromForm] int workerId, [FromForm] string shiftStart, [FromForm] string shiftEnd, [FromForm] string lunchStart, [FromForm] string lunchEnd)
    {
        var w = await _db.Workers.FirstOrDefaultAsync(x => x.Id == workerId);
        if (w != null)
        {
            w.ShiftStart = string.IsNullOrWhiteSpace(shiftStart) ? "09:00" : shiftStart;
            w.ShiftEnd = string.IsNullOrWhiteSpace(shiftEnd) ? "18:00" : shiftEnd;
            w.LunchStart = string.IsNullOrWhiteSpace(lunchStart) ? "12:00" : lunchStart;
            w.LunchEnd = string.IsNullOrWhiteSpace(lunchEnd) ? "13:00" : lunchEnd;
            await _db.SaveChangesAsync();
            SuccessMessage = $"✓ Updated shift schedule for {w.FullName}.";
        }
        return RedirectToPage(new { Range });
    }

    private static (string Status, string Note) EvaluateCompliance(Worker w, List<Punch> todayPunches)
    {
        if (todayPunches.Count == 0)
            return ("Pending", "Not yet clocked in");

        var violations = new List<string>();
        TimeSpan.TryParse(w.ShiftStart, out var shiftStart);
        TimeSpan.TryParse(w.ShiftEnd, out var shiftEnd);
        TimeSpan.TryParse(w.LunchStart, out var lunchStart);
        TimeSpan.TryParse(w.LunchEnd, out var lunchEnd);

        var timeIn = todayPunches.FirstOrDefault(p => p.Type == PunchType.TimeIn);
        if (timeIn != null)
        {
            var inPh = timeIn.TimestampUtc.AddHours(8).TimeOfDay;
            if (inPh > shiftStart.Add(TimeSpan.FromMinutes(15)))
                violations.Add($"Late In ({timeIn.TimestampUtc.AddHours(8):hh:mm tt})");
        }

        var lunchOut = todayPunches.FirstOrDefault(p => p.Type == PunchType.LunchOut);
        if (lunchOut != null)
        {
            var loPh = lunchOut.TimestampUtc.AddHours(8).TimeOfDay;
            if (loPh < lunchStart.Subtract(TimeSpan.FromMinutes(10)) || loPh > lunchEnd)
                violations.Add($"Off-Schedule Lunch ({lunchOut.TimestampUtc.AddHours(8):hh:mm tt})");
        }

        var timeOut = todayPunches.FirstOrDefault(p => p.Type == PunchType.TimeOut);
        if (timeOut != null)
        {
            var outPh = timeOut.TimestampUtc.AddHours(8).TimeOfDay;
            if (outPh < shiftEnd.Subtract(TimeSpan.FromMinutes(10)))
                violations.Add($"Early Out ({timeOut.TimestampUtc.AddHours(8):hh:mm tt})");
        }

        return violations.Count > 0
            ? ("Off-Schedule", string.Join(" • ", violations))
            : ("On Schedule", "Following assigned shift");
    }

    private async Task LoadDataAsync()
    {
        try
        {
            var nowUtc = DateTime.UtcNow;
            var nowPh = nowUtc.AddHours(8);
            var (todayStartUtc, todayEndUtc) = PunchService.TodayRangeUtc(nowUtc);

            // Calculate PH-aligned period start UTC
            int diffToMon = ((int)nowPh.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            var weekStartPh = DateTime.SpecifyKind(nowPh.Date.AddDays(-diffToMon), DateTimeKind.Utc);
            var monthStartPh = new DateTime(nowPh.Year, nowPh.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var yearStartPh = new DateTime(nowPh.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            DateTime startUtc = Range.ToLower() switch
            {
                "weekly" => weekStartPh.AddHours(-8),
                "monthly" => monthStartPh.AddHours(-8),
                "yearly" => yearStartPh.AddHours(-8),
                _ => todayStartUtc
            };

            var workers = await _db.Workers
                .Where(w => w.BusinessId == BusinessId && w.IsActive)
                .OrderBy(w => w.FullName)
                .ToListAsync();

            TotalWorkers = workers.Count;

            var punches = await _db.Punches
                .Where(p => p.BusinessId == BusinessId && p.TimestampUtc >= startUtc)
                .ToListAsync();

            var todayPunchesAll = Range.ToLower() == "daily"
                ? punches
                : await _db.Punches.Where(p => p.BusinessId == BusinessId && p.TimestampUtc >= todayStartUtc && p.TimestampUtc < todayEndUtc).ToListAsync();

            var userIds = workers.Where(w => !string.IsNullOrEmpty(w.UserId)).Select(w => w.UserId).ToList();
            var identityUsers = await _users.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName ?? "");

            WorkerAttendanceList.Clear();
            int clockedInCounter = 0;
            decimal activeHourlyBurn = 0;

            foreach (var worker in workers)
            {
                var workerPunches = punches.Where(p => p.WorkerId == worker.Id).OrderBy(p => p.TimestampUtc).ToList();
                var workedTime = PayCalculator.WorkedTime(workerPunches, nowUtc);
                var hours = Math.Round((decimal)workedTime.TotalHours, 2);
                var earned = PayCalculator.Earned(workedTime, worker.HourlyRate);

                var todayWorkerPunches = todayPunchesAll.Where(p => p.WorkerId == worker.Id).OrderBy(p => p.TimestampUtc).ToList();
                var latest = todayWorkerPunches.LastOrDefault();
                var liveStatus = latest?.Type switch
                {
                    PunchType.TimeIn or PunchType.LunchIn => "Working",
                    PunchType.LunchOut => "On Lunch Break",
                    PunchType.TimeOut => "Clocked Out",
                    _ => "Not Yet In"
                };

                bool isClockedIn = latest != null && (latest.Type == PunchType.TimeIn || latest.Type == PunchType.LunchIn);
                if (isClockedIn)
                {
                    clockedInCounter++;
                    activeHourlyBurn += worker.HourlyRate;
                }

                var (compStatus, compNote) = EvaluateCompliance(worker, todayWorkerPunches);

                string empId = worker.UserId != null && identityUsers.TryGetValue(worker.UserId, out var uname) 
                    ? uname 
                    : $"EMP-{worker.Id:D3}";

                WorkerAttendanceList.Add(new WorkerAttendanceRow
                {
                    WorkerId = worker.Id,
                    FullName = worker.FullName,
                    EmployeeId = empId,
                    PayType = worker.PayType,
                    Rate = worker.Rate,
                    HoursWorked = hours,
                    EarnedAmount = earned,
                    IsClockedIn = isClockedIn,
                    LatestPunch = latest != null ? latest.Type.ToString() : "None",
                    LatestPunchTime = latest != null ? latest.TimestampUtc.AddHours(8).ToString("hh:mm tt") : "--",
                    LiveStatus = liveStatus,
                    ShiftStart = worker.ShiftStart,
                    ShiftEnd = worker.ShiftEnd,
                    LunchStart = worker.LunchStart,
                    LunchEnd = worker.LunchEnd,
                    ComplianceStatus = compStatus,
                    ComplianceNote = compNote
                });
            }

            ClockedInCount = clockedInCounter;
            AbsentCount = TotalWorkers - ClockedInCount;
            LiveLaborCostRatePerHour = activeHourlyBurn;
            TotalLaborCostSoFar = WorkerAttendanceList.Sum(w => w.EarnedAmount);

            // Sales, Capital & Profit Computation
            var sales = await _db.Sales
                .Where(s => s.BusinessId == BusinessId && s.TimestampUtc >= startUtc)
                .ToListAsync();

            PeriodSales = sales.Sum(s => s.TotalAmount);
            PeriodSalesCount = sales.Count;

            // Labor cost as % of sales
            LaborCostRatioPct = PeriodSales > 0 ? Math.Round((TotalLaborCostSoFar / PeriodSales) * 100, 1) : 0;

            // Compute Estimated Capital (Cost of goods ~ 42% + Labor cost for period)
            var estimatedCogs = PeriodSales * 0.42m;
            PeriodCapital = Math.Round(estimatedCogs + TotalLaborCostSoFar, 2);
            
            if (PeriodCapital == 0 && PeriodSales > 0) PeriodCapital = PeriodSales * 0.45m;
            PeriodProfit = PeriodSales - PeriodCapital;
            ProfitMarginPct = PeriodSales > 0 ? Math.Round((PeriodProfit / PeriodSales) * 100, 1) : 0;

            // Build Real Chart.js Buckets (Daily / Weekly / Monthly / Yearly)
            ChartLabels.Clear();
            ChartSales.Clear();
            ChartCapital.Clear();
            ChartProfit.Clear();

            var rangeKey = Range.ToLower();
            if (rangeKey == "weekly")
            {
                string[] days = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
                for (int i = 0; i < 7; i++)
                {
                    var dPh = weekStartPh.AddDays(i).Date;
                    var daySales = sales.Where(s => s.TimestampUtc.AddHours(8).Date == dPh).Sum(s => s.TotalAmount);
                    var dayCap = Math.Round(daySales * 0.42m, 2);
                    ChartLabels.Add(days[i]);
                    ChartSales.Add(daySales);
                    ChartCapital.Add(dayCap);
                    ChartProfit.Add(daySales - dayCap);
                }
            }
            else if (rangeKey == "monthly")
            {
                for (int w = 1; w <= 4; w++)
                {
                    int minDay = (w - 1) * 7 + 1;
                    int maxDay = w < 4 ? w * 7 : 31;
                    var wkSales = sales.Where(s => { var d = s.TimestampUtc.AddHours(8).Day; return d >= minDay && d <= maxDay; }).Sum(s => s.TotalAmount);
                    var wkCap = Math.Round(wkSales * 0.42m, 2);
                    ChartLabels.Add($"Week {w}");
                    ChartSales.Add(wkSales);
                    ChartCapital.Add(wkCap);
                    ChartProfit.Add(wkSales - wkCap);
                }
            }
            else if (rangeKey == "yearly")
            {
                string[] months = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
                for (int m = 1; m <= 12; m++)
                {
                    var mSales = sales.Where(s => s.TimestampUtc.AddHours(8).Month == m && s.TimestampUtc.AddHours(8).Year == nowPh.Year).Sum(s => s.TotalAmount);
                    var mCap = Math.Round(mSales * 0.42m, 2);
                    ChartLabels.Add(months[m - 1]);
                    ChartSales.Add(mSales);
                    ChartCapital.Add(mCap);
                    ChartProfit.Add(mSales - mCap);
                }
            }
            else
            {
                // Daily: 6 time windows
                int[] hourBuckets = { 6, 9, 12, 15, 18, 21 };
                string[] hourLabels = { "6AM-9AM", "9AM-12PM", "12PM-3PM", "3PM-6PM", "6PM-9PM", "9PM+" };
                for (int b = 0; b < hourBuckets.Length; b++)
                {
                    int hMin = hourBuckets[b];
                    int hMax = b < hourBuckets.Length - 1 ? hourBuckets[b + 1] : 24;
                    var bSales = sales.Where(s => { var hr = s.TimestampUtc.AddHours(8).Hour; return hr >= hMin && hr < hMax; }).Sum(s => s.TotalAmount);
                    var bCap = Math.Round(bSales * 0.42m, 2);
                    ChartLabels.Add(hourLabels[b]);
                    ChartSales.Add(bSales);
                    ChartCapital.Add(bCap);
                    ChartProfit.Add(bSales - bCap);
                }
            }

            // Inventory & Stock
            AllProducts = await _db.Products
                .Where(p => p.BusinessId == BusinessId && p.IsActive)
                .OrderBy(p => p.Name)
                .ToListAsync();

            OverallStockCount = AllProducts.Sum(p => p.CurrentStock);
            LowStockProducts = AllProducts.Where(p => p.CurrentStock <= p.LowStockAlert).ToList();
            LowStockAlertCount = LowStockProducts.Count;

            // Announcements & Cash Drawer
            Announcements = await _db.Announcements
                .Where(a => a.BusinessId == BusinessId && a.IsActive)
                .OrderByDescending(a => a.CreatedAtUtc)
                .Take(5)
                .ToListAsync();

            LatestClosing = await _db.CashDrawerClosings
                .Where(c => c.BusinessId == BusinessId)
                .OrderByDescending(c => c.ClosedAtUtc)
                .FirstOrDefaultAsync();

            // Load Pending Leaves and Advances for Owner Review
            PendingLeaveRequests = await _db.LeaveRequests
                .Where(l => l.BusinessId == BusinessId && l.Status == LeaveStatus.Pending)
                .OrderBy(l => l.TargetDate)
                .ToListAsync();

            PendingAdvanceRequests = await _db.AdvancePaymentRequests
                .Where(a => a.BusinessId == BusinessId && a.Status == AdvanceStatus.Pending)
                .OrderByDescending(a => a.CreatedAtUtc)
                .ToListAsync();
        }
        catch
        {
            WorkerAttendanceList = new();
            LowStockProducts = new();
        }
    }

    // 0. Toggle Store Shift (Start / Stop)
    public async Task<IActionResult> OnPostToggleStoreShiftAsync([FromForm] string? shiftName)
    {
        var biz = await _db.Businesses.FirstOrDefaultAsync();
        var bizId = biz?.Id ?? 1;

        var active = await _db.StoreShiftSessions
            .Where(s => s.BusinessId == bizId && s.IsActive)
            .OrderByDescending(s => s.StartedAtUtc)
            .FirstOrDefaultAsync();

        if (active != null)
        {
            // End the current active shift
            active.IsActive = false;
            active.EndedAtUtc = DateTime.UtcNow;
            active.EndedBy = DisplayName;
            await _db.SaveChangesAsync();
            SuccessMessage = $"✓ Store Shift '{active.ShiftName}' has been ended.";
        }
        else
        {
            // Start a new shift
            var newShift = new StoreShiftSession
            {
                BusinessId = bizId,
                ShiftName = string.IsNullOrWhiteSpace(shiftName) ? "Store Shift" : shiftName.Trim(),
                StartedAtUtc = DateTime.UtcNow,
                IsActive = true,
                StartedBy = DisplayName
            };
            _db.StoreShiftSessions.Add(newShift);
            await _db.SaveChangesAsync();
            SuccessMessage = $"✓ New Store Shift '{newShift.ShiftName}' started!";
        }

        return RedirectToPage(new { range = Range });
    }

    public async Task<IActionResult> OnPostAddWorkerAsync(
        [FromForm] string fullName, 
        [FromForm] string employeeId, 
        [FromForm] int payType, 
        [FromForm] decimal rate, 
        [FromForm] decimal hoursPerDay)
    {
        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(employeeId))
        {
            ErrorMessage = "Employee Name and ID are required.";
            return RedirectToPage(new { range = Range });
        }

        var biz = await _db.Businesses.FirstOrDefaultAsync();
        var bizId = biz?.Id ?? 1;

        var existingUser = await _users.FindByNameAsync(employeeId.Trim());
        if (existingUser != null)
        {
            ErrorMessage = $"Employee ID '{employeeId}' already exists.";
            return RedirectToPage(new { range = Range });
        }

        var newIdentityUser = new IdentityUser
        {
            UserName = employeeId.Trim(),
            Email = $"{employeeId.Trim().ToLower()}@laufey.com"
        };
        var createResult = await _users.CreateAsync(newIdentityUser, "password123");
        if (!createResult.Succeeded)
        {
            ErrorMessage = createResult.Errors.FirstOrDefault()?.Description ?? "Failed to create employee login.";
            return RedirectToPage(new { range = Range });
        }
        await _users.AddToRoleAsync(newIdentityUser, "Employee");

        var newWorker = new Worker
        {
            BusinessId = bizId,
            UserId = newIdentityUser.Id,
            FullName = fullName.Trim(),
            PayType = (PayType)payType,
            Rate = rate > 0 ? rate : 150m,
            HoursPerDay = hoursPerDay > 0 ? hoursPerDay : 8m,
            IsActive = true
        };

        _db.Workers.Add(newWorker);
        await _db.SaveChangesAsync();

        SuccessMessage = $"Staff member '{fullName}' (ID: {employeeId}) registered successfully!";
        return RedirectToPage(new { range = Range });
    }

    public async Task<IActionResult> OnPostEditWorkerAsync(
        [FromForm] int workerId,
        [FromForm] string fullName,
        [FromForm] int payType,
        [FromForm] decimal rate,
        [FromForm] decimal hoursPerDay)
    {
        var worker = await _db.Workers.FirstOrDefaultAsync(w => w.Id == workerId);
        if (worker == null)
        {
            ErrorMessage = "Worker not found.";
            return RedirectToPage(new { range = Range });
        }

        worker.FullName = fullName.Trim();
        worker.PayType = (PayType)payType;
        worker.Rate = rate;
        worker.HoursPerDay = hoursPerDay;

        await _db.SaveChangesAsync();
        SuccessMessage = $"Staff member '{worker.FullName}' updated.";
        return RedirectToPage(new { range = Range });
    }

    public async Task<IActionResult> OnPostDeactivateWorkerAsync([FromForm] int workerId)
    {
        var worker = await _db.Workers.FirstOrDefaultAsync(w => w.Id == workerId);
        if (worker != null)
        {
            worker.IsActive = false;
            await _db.SaveChangesAsync();
            SuccessMessage = $"Worker '{worker.FullName}' deactivated.";
        }
        return RedirectToPage(new { range = Range });
    }

    // 1. Quick POS: Record Sale Handler
    public async Task<IActionResult> OnPostRecordSaleAsync(
        [FromForm] int? productId,
        [FromForm] decimal totalAmount,
        [FromForm] string paymentMethod,
        [FromForm] string? referenceNumber,
        [FromForm] int quantity)
    {
        var biz = await _db.Businesses.FirstOrDefaultAsync();
        var bizId = biz?.Id ?? 1;

        if (totalAmount <= 0)
        {
            ErrorMessage = "Sale amount must be greater than 0.";
            return RedirectToPage(new { range = Range });
        }

        string notes = "";
        if (productId.HasValue && productId.Value > 0)
        {
            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId.Value);
            if (product != null)
            {
                var qty = quantity > 0 ? quantity : 1;
                product.CurrentStock = Math.Max(0, product.CurrentStock - qty);
                notes = $"{qty}x {product.Name}";
            }
        }

        var sale = new Sale
        {
            BusinessId = bizId,
            TotalAmount = totalAmount,
            PaymentMethod = string.IsNullOrWhiteSpace(paymentMethod) ? "Cash" : paymentMethod,
            ReferenceNumber = referenceNumber,
            Notes = notes,
            TimestampUtc = DateTime.UtcNow
        };

        _db.Sales.Add(sale);
        await _db.SaveChangesAsync();

        SuccessMessage = $"Sale of ₱{totalAmount:N2} via {sale.PaymentMethod} recorded successfully!";
        return RedirectToPage(new { range = Range });
    }

    // 2. Quick Restock Handler
    public async Task<IActionResult> OnPostRestockAsync(
        [FromForm] int productId,
        [FromForm] int addStock)
    {
        if (addStock <= 0)
        {
            ErrorMessage = "Restock quantity must be positive.";
            return RedirectToPage(new { range = Range });
        }

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId);
        if (product != null)
        {
            product.CurrentStock += addStock;
            await _db.SaveChangesAsync();
            SuccessMessage = $"Restocked {product.Name} (+{addStock} units). New stock: {product.CurrentStock}.";
        }

        return RedirectToPage(new { range = Range });
    }

    // 3. End of Day Cash Drawer Reconciliation Handler
    public async Task<IActionResult> OnPostReconcileDrawerAsync(
        [FromForm] decimal cashActual,
        [FromForm] string? notes)
    {
        var biz = await _db.Businesses.FirstOrDefaultAsync();
        var bizId = biz?.Id ?? 1;

        var (startUtc, endUtc) = PunchService.TodayRangeUtc(DateTime.UtcNow);
        var expectedCashSales = await _db.Sales
            .Where(s => s.BusinessId == bizId && s.PaymentMethod == "Cash" && s.TimestampUtc >= startUtc && s.TimestampUtc < endUtc)
            .SumAsync(s => s.TotalAmount);

        var diff = cashActual - expectedCashSales;
        string status = diff == 0 ? "Balanced" : (diff > 0 ? "Overage" : "Shortage");

        var closing = new CashDrawerClosing
        {
            BusinessId = bizId,
            CashDrawerActual = cashActual,
            ExpectedSales = expectedCashSales,
            Difference = diff,
            Status = status,
            ClosedBy = DisplayName,
            Notes = notes,
            ClosedAtUtc = DateTime.UtcNow
        };

        _db.CashDrawerClosings.Add(closing);
        await _db.SaveChangesAsync();

        SuccessMessage = $"Cash drawer reconciled: {status} (Actual: ₱{cashActual:N2}, Expected: ₱{expectedCashSales:N2}, Diff: ₱{diff:N2}).";
        return RedirectToPage(new { range = Range });
    }

    // 4. Add Announcement Handler
    public async Task<IActionResult> OnPostAddAnnouncementAsync(
        [FromForm] string title,
        [FromForm] string message,
        [FromForm] string priority)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
        {
            ErrorMessage = "Title and message are required.";
            return RedirectToPage(new { range = Range });
        }

        var biz = await _db.Businesses.FirstOrDefaultAsync();
        var bizId = biz?.Id ?? 1;

        var ann = new Announcement
        {
            BusinessId = bizId,
            Title = title.Trim(),
            Message = message.Trim(),
            Priority = string.IsNullOrWhiteSpace(priority) ? "Normal" : priority,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true
        };

        _db.Announcements.Add(ann);
        await _db.SaveChangesAsync();

        SuccessMessage = $"Announcement '{ann.Title}' posted!";
        return RedirectToPage(new { range = Range });
    }

    // 5. Add New Stock / Product with dedicated cost, price, and initial stock
    public async Task<IActionResult> OnPostAddProductAsync(
        [FromForm] string name,
        [FromForm] string category,
        [FromForm] decimal price,
        [FromForm] decimal cost,
        [FromForm] int initialStock,
        [FromForm] int alertThreshold)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorMessage = "Product name is required.";
            return RedirectToPage(new { range = Range });
        }

        var biz = await _db.Businesses.FirstOrDefaultAsync();
        var bizId = biz?.Id ?? 1;

        var product = new Product
        {
            BusinessId = bizId,
            Name = name.Trim(),
            Category = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim(),
            Price = price > 0 ? price : 0m,
            Cost = cost > 0 ? cost : 0m,
            CurrentStock = initialStock >= 0 ? initialStock : 0,
            LowStockAlert = alertThreshold > 0 ? alertThreshold : 5,
            IsActive = true
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        SuccessMessage = $"✓ New stock item '{product.Name}' added with Selling Price ₱{product.Price:N2} and Cost ₱{product.Cost:N2}!";
        return RedirectToPage(new { range = Range });
    }

    // 6. Approve / Reject Leave Request
    public async Task<IActionResult> OnPostReviewLeaveAsync([FromForm] int leaveId, [FromForm] int status)
    {
        var leave = await _db.LeaveRequests.FirstOrDefaultAsync(l => l.Id == leaveId);
        if (leave != null)
        {
            leave.Status = (LeaveStatus)status;
            leave.ReviewedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            SuccessMessage = $"Leave request marked as {leave.Status}.";
        }
        return RedirectToPage(new { range = Range });
    }

    // 7. Approve / Reject Cash Advance Request
    public async Task<IActionResult> OnPostReviewAdvanceAsync([FromForm] int advanceId, [FromForm] int status)
    {
        var adv = await _db.AdvancePaymentRequests.FirstOrDefaultAsync(a => a.Id == advanceId);
        if (adv != null)
        {
            adv.Status = (AdvanceStatus)status;
            adv.ReviewedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            SuccessMessage = $"Advance payment marked as {adv.Status}.";
        }
        return RedirectToPage(new { range = Range });
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await _signIn.SignOutAsync();
        return RedirectToPage("/Login");
    }
}
