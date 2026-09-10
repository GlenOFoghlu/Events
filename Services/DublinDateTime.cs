using System.Globalization;
using DublinEventsCollector.Models;

namespace DublinEventsCollector.Services;

public static class DublinDateTime
{
    private static readonly TimeZoneInfo DublinTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin");

    public static DateTimeOffset FromLocal(DateOnly date, TimeOnly? time = null)
    {
        var local = date.ToDateTime(time ?? TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, DublinTimeZone.GetUtcOffset(local));
    }

    public static DateTimeOffset? ParseIsoOrLocal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var offset))
        {
            return offset.ToOffset(DublinTimeZone.GetUtcOffset(offset));
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var local))
        {
            return new DateTimeOffset(local, DublinTimeZone.GetUtcOffset(local));
        }

        return null;
    }

    public static bool IsInside(EventWindow window, DateTimeOffset date)
    {
        var from = FromLocal(window.From);
        var to = FromLocal(window.To.AddDays(1));
        return date >= from && date < to;
    }
}
