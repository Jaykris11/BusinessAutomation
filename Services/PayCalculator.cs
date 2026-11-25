using PayrollApp.Web.Data;

namespace PayrollApp.Web.Services;

// Computes hours and earnings from raw punches. Lunch is excluded automatically.
public static class PayCalculator
{
    public static TimeSpan WorkedTime(IEnumerable<Punch> dayPunches, DateTime nowUtc)
    {
        var p = dayPunches.OrderBy(x => x.TimestampUtc).ToList();
        DateTime? timeIn = p.FirstOrDefault(x => x.Type == PunchType.TimeIn)?.TimestampUtc;
        DateTime? lunchOut = p.FirstOrDefault(x => x.Type == PunchType.LunchOut)?.TimestampUtc;
        DateTime? lunchIn = p.FirstOrDefault(x => x.Type == PunchType.LunchIn)?.TimestampUtc;
        DateTime? timeOut = p.FirstOrDefault(x => x.Type == PunchType.TimeOut)?.TimestampUtc;

        if (timeIn is null) return TimeSpan.Zero;

        var total = TimeSpan.Zero;

        // Morning block: TimeIn -> LunchOut (or now / TimeOut if no lunch yet)
        var morningEnd = lunchOut ?? timeOut ?? nowUtc;
        total += morningEnd - timeIn.Value;

        // Afternoon block: LunchIn -> TimeOut (or now if still working)
        if (lunchIn is not null)
            total += (timeOut ?? nowUtc) - lunchIn.Value;

        return total < TimeSpan.Zero ? TimeSpan.Zero : total;
    }

    public static decimal Earned(TimeSpan worked, decimal hourlyRate)
        => Math.Round((decimal)worked.TotalHours * hourlyRate, 2);
}