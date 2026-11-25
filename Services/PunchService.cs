using Microsoft.EntityFrameworkCore;
using PayrollApp.Web.Data;

namespace PayrollApp.Web.Services;

public record PunchResult(bool Ok, string Message, PunchType? Next);

public class PunchService
{
    private readonly AppDbContext _db;
    private static readonly TimeSpan Ph = TimeSpan.FromHours(8); // Philippines: UTC+8, no DST

    public PunchService(AppDbContext db) => _db = db;

    // Start and end of "today" in Philippine time, expressed in UTC.
    public static (DateTime startUtc, DateTime endUtc) TodayRangeUtc(DateTime nowUtc)
    {
        var phNow = nowUtc + Ph;
        var startPhUtc = DateTime.SpecifyKind(phNow.Date, DateTimeKind.Utc) - Ph;
        return (startPhUtc, startPhUtc.AddDays(1));
    }

    // The only button the worker should see next. Null = day is finished.
    public static PunchType? NextValid(PunchType? last) => last switch
    {
        null => PunchType.TimeIn,
        PunchType.TimeIn => PunchType.LunchOut,
        PunchType.LunchOut => PunchType.LunchIn,
        PunchType.LunchIn => PunchType.TimeOut,
        _ => null
    };

    public async Task<PunchType?> GetNextForWorkerAsync(int businessId, int workerId)
    {
        var (s, e) = TodayRangeUtc(DateTime.UtcNow);
        var last = await _db.Punches
            .Where(p => p.BusinessId == businessId && p.WorkerId == workerId
                     && p.TimestampUtc >= s && p.TimestampUtc < e)
            .OrderByDescending(p => p.TimestampUtc)
            .Select(p => (PunchType?)p.Type)
            .FirstOrDefaultAsync();
        return NextValid(last);
    }

    public async Task<PunchResult> PunchAsync(int businessId, int workerId, PunchType requested, string? clientIp)
    {
        var now = DateTime.UtcNow;

        var worker = await _db.Workers.FirstOrDefaultAsync(w =>
            w.Id == workerId && w.BusinessId == businessId && w.IsActive);
        if (worker is null) return new(false, "Worker not found.", null);

        var business = await _db.Businesses.Include(b => b.AllowedIps)
            .FirstAsync(b => b.Id == businessId);

        // 1) Network lock (only for punches, not the rest of the app)
        if (business.RequireIpForPunch)
        {
            var allowed = business.AllowedIps.Any(a => a.Address == clientIp);
            if (!allowed)
            {
                _db.RejectedPunches.Add(new RejectedPunch
                {
                    BusinessId = businessId, WorkerId = workerId, TimestampUtc = now,
                    IpAddress = clientIp, Reason = "Not on store network"
                });
                await _db.SaveChangesAsync();
                return new(false, "You must be connected to the store Wi-Fi to clock in.", null);
            }
        }

        // 2) Order rule: only the next valid punch is accepted
        var next = await GetNextForWorkerAsync(businessId, workerId);
        if (next is null) return new(false, "You already clocked out today.", null);
        if (next != requested) return new(false, $"Your next punch must be {next}.", next);

        // 3) Save the raw timestamp
        _db.Punches.Add(new Punch
        {
            BusinessId = businessId, WorkerId = workerId, Type = requested,
            TimestampUtc = now, IpAddress = clientIp
        });
        await _db.SaveChangesAsync();

        return new(true, $"{requested} recorded.", NextValid(requested));
    }
}