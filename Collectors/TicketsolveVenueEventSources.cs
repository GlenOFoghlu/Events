using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DublinEventsCollector.Models;
using DublinEventsCollector.Services;

namespace DublinEventsCollector.Collectors;

public abstract partial class TicketsolveVenueEventSource(IHttpClientFactory httpClientFactory) : IEventSource
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    protected abstract string FeedUrl { get; }
    protected abstract string DefaultVenue { get; }
    public bool RequiresConfiguration => false;

    public async Task<EventSourceResult> CollectAsync(EventWindow window, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        using var response = await client.GetAsync(FeedUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return EventSourceResult.Empty(Name, $"{DefaultVenue} Ticketsolve feed returned {(int)response.StatusCode}.");
        }

        var document = XDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return new EventSourceResult(Name, ParseFeed(document, window).ToArray(), []);
    }

    private IEnumerable<EventItem> ParseFeed(XDocument document, EventWindow window)
    {
        foreach (var venue in document.Descendants("venue"))
        {
            var venueName = Text(venue.Element("name")) ?? DefaultVenue;
            foreach (var show in venue.Descendants("show"))
            {
                var title = Text(show.Element("name"));
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                var category = Text(show.Element("event_category"));
                var imageUrl = show.Descendants("image")
                    .SelectMany(image => image.Elements("url"))
                    .OrderByDescending(url => ImageSize(url.Attribute("size")?.Value))
                    .FirstOrDefault()?.Value.Trim();
                var description = Text(show.Element("description"));
                var (priceMin, priceMax) = ParsePriceRange(description);

                foreach (var performance in show.Element("events")?.Elements("event") ?? [])
                {
                    var eventId = performance.Attribute("id")?.Value ?? Text(performance.Element("event_number"));
                    var startsAt = ParseOffset(Text(performance.Element("date_time_iso")));
                    if (string.IsNullOrWhiteSpace(eventId) || startsAt is null || !DublinDateTime.IsInside(window, startsAt.Value))
                    {
                        continue;
                    }

                    yield return new EventItem
                    {
                        Source = Name,
                        SourceEventId = $"{Name}-{eventId}",
                        Title = title,
                        Blurb = VenueHtmlParsing.Blurb(description),
                        Venue = VenueWithLayout(venueName, Text(performance.Element("venue_layout"))),
                        StartsAt = startsAt.Value,
                        Url = Text(performance.Element("url")) ?? Text(show.Element("url")),
                        ImageUrl = imageUrl,
                        Category = category,
                        PriceMin = priceMin,
                        PriceMax = priceMax,
                        Currency = priceMin is null && priceMax is null ? null : "EUR",
                        Status = Text(performance.Element("status"))
                    };
                }
            }
        }
    }

    private static int ImageSize(string? size) => size?.ToLowerInvariant() switch
    {
        "large" => 3,
        "medium" => 2,
        "small" => 1,
        _ => 0
    };

    private static DateTimeOffset? ParseOffset(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;

    private static string VenueWithLayout(string venue, string? layout) =>
        string.IsNullOrWhiteSpace(layout)
            ? venue.Trim()
            : $"{venue.Trim()} - {layout.Trim().Replace('_', ' ')}";

    private static string? Text(XElement? element) =>
        string.IsNullOrWhiteSpace(element?.Value) ? null : element.Value.Trim();

    private static (decimal? Min, decimal? Max) ParsePriceRange(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, null);
        }

        var prices = PriceRegex().Matches(text)
            .Select(match => decimal.TryParse(match.Groups["price"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) ? price : (decimal?)null)
            .OfType<decimal>()
            .ToArray();

        return prices.Length switch
        {
            0 => (null, null),
            1 => (prices[0], prices[0]),
            _ => (prices.Min(), prices.Max())
        };
    }

    [GeneratedRegex(@"(?:€|&euro;)\s*(?<price>\d+(?:\.\d{1,2})?)", RegexOptions.IgnoreCase)]
    private static partial Regex PriceRegex();
}

public sealed class DraiochtEventSource(IHttpClientFactory factory) : TicketsolveVenueEventSource(factory)
{
    public override string Name => "draiocht";
    public override string Description => "Draíocht performances from its public Ticketsolve feed.";
    protected override string FeedUrl => "https://draiocht.ticketsolve.com/shows.xml";
    protected override string DefaultVenue => "Draíocht";
}

public sealed class CivicTheatreEventSource(IHttpClientFactory factory) : TicketsolveVenueEventSource(factory)
{
    public override string Name => "civic-theatre";
    public override string Description => "Civic Theatre performances from its public Ticketsolve feed.";
    protected override string FeedUrl => "https://civictheatre.ticketsolve.com/shows.xml";
    protected override string DefaultVenue => "Civic Theatre";
}

public sealed class MillTheatreEventSource(IHttpClientFactory factory) : TicketsolveVenueEventSource(factory)
{
    public override string Name => "mill-theatre";
    public override string Description => "dlr Mill Theatre performances from its public Ticketsolve feed.";
    protected override string FeedUrl => "https://milltheatre.ticketsolve.com/shows.xml";
    protected override string DefaultVenue => "dlr Mill Theatre Dundrum";
}

public sealed class AxisBallymunEventSource(IHttpClientFactory factory) : TicketsolveVenueEventSource(factory)
{
    public override string Name => "axis-ballymun";
    public override string Description => "Axis Ballymun performances from its public Ticketsolve feed.";
    protected override string FeedUrl => "https://axisballymun.ticketsolve.com/shows.xml";
    protected override string DefaultVenue => "Axis Ballymun";
}
