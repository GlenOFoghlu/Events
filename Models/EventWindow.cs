namespace DublinEventsCollector.Models;

public sealed record EventWindow(DateOnly From, DateOnly To)
{
    public static EventWindow Create(DateOnly? from, DateOnly? to, int days)
    {
        if (days is < 1 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(days), "Days must be between 1 and 180.");
        }

        var start = from ?? DateOnly.FromDateTime(DateTime.Today);
        var end = to ?? start.AddDays(days);

        if (end < start)
        {
            throw new ArgumentException("The end date must be on or after the start date.");
        }

        return new EventWindow(start, end);
    }
}
