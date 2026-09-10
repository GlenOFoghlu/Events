namespace DublinEventsCollector.Models;

public sealed record EventItem
{
    public required string Source { get; init; }
    public required string SourceEventId { get; init; }
    public required string Title { get; init; }
    public string? Venue { get; init; }
    public string City { get; init; } = "Dublin";
    public required DateTimeOffset StartsAt { get; init; }
    public DateTimeOffset? EndsAt { get; init; }
    public string? Url { get; init; }
    public string? ImageUrl { get; init; }
    public string? Category { get; init; }
    public decimal? PriceMin { get; init; }
    public decimal? PriceMax { get; init; }
    public string? Currency { get; init; }
    public string? Status { get; init; }
}
