using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DublinEventsCollector.Models;
using DublinEventsCollector.Services;

namespace DublinEventsCollector.Collectors;

public sealed partial class DublinFringeFestivalEventSource(IHttpClientFactory httpClientFactory) : IEventSource
{
    private const string TicketsolveFeedUrl = "https://fringefest.ticketsolve.com/shows.xml";

    public string Name => "fringefest";
    public string Description => "Dublin Fringe Festival Ticketsolve feed.";
    public bool RequiresConfiguration => false;

    public async Task<EventSourceResult> CollectAsync(EventWindow window, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        using var response = await client.GetAsync(TicketsolveFeedUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return EventSourceResult.Empty(Name, $"Dublin Fringe Festival Ticketsolve feed returned {(int)response.StatusCode}.");
        }

        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
        var document = XDocument.Parse(xml);
        var events = ParseTicketsolveFeed(document, window).ToArray();

        return new EventSourceResult(Name, events, []);
    }

    private IEnumerable<EventItem> ParseTicketsolveFeed(XDocument document, EventWindow window)
    {
        foreach (var venue in document.Descendants("venue"))
        {
            var venueName = Text(venue.Element("name")) ?? "Dublin Fringe Festival";

            foreach (var show in venue.Descendants("show"))
            {
                var showId = show.Attribute("id")?.Value;
                var title = Text(show.Element("name"));
                var category = Text(show.Element("event_category"));
                var description = Text(show.Element("description"));
                var imageUrl = show
                    .Descendants("image")
                    .SelectMany(image => image.Elements("url"))
                    .FirstOrDefault(url => string.Equals(url.Attribute("size")?.Value, "medium", StringComparison.OrdinalIgnoreCase))
                    ?.Value
                    ?.Trim();
                var (priceMin, priceMax) = ParsePriceRange(description);

                if (string.IsNullOrWhiteSpace(showId) || string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                foreach (var eventElement in show.Element("events")?.Elements("event") ?? [])
                {
                    var eventId = eventElement.Attribute("id")?.Value ?? Text(eventElement.Element("event_number"));
                    var startsAt = ParseOffset(Text(eventElement.Element("date_time_iso")));

                    if (string.IsNullOrWhiteSpace(eventId)
                        || startsAt is null
                        || !DublinDateTime.IsInside(window, startsAt.Value))
                    {
                        continue;
                    }

                    yield return new EventItem
                    {
                        Source = Name,
                        SourceEventId = $"fringefest-{eventId}",
                        Title = title,
                        Venue = VenueWithLayout(venueName, Text(eventElement.Element("venue_layout"))),
                        StartsAt = startsAt.Value,
                        Url = Text(eventElement.Element("url")) ?? Text(show.Element("url")),
                        ImageUrl = imageUrl,
                        Category = category,
                        PriceMin = priceMin,
                        PriceMax = priceMax,
                        Currency = priceMin is null && priceMax is null ? null : "EUR",
                        Status = Text(eventElement.Element("status"))
                    };
                }
            }
        }
    }

    private static DateTimeOffset? ParseOffset(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    private static string VenueWithLayout(string venueName, string? layout)
    {
        if (string.IsNullOrWhiteSpace(layout))
        {
            return venueName;
        }

        return $"{venueName} - {layout.Trim()}";
    }

    private static string? Text(XElement? element)
    {
        return string.IsNullOrWhiteSpace(element?.Value)
            ? null
            : element.Value.Trim();
    }

    private static (decimal? Min, decimal? Max) ParsePriceRange(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, null);
        }

        var prices = PriceRegex()
            .Matches(text)
            .Select(match => decimal.TryParse(match.Groups["price"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) ? price : (decimal?)null)
            .Where(price => price is not null)
            .Select(price => price!.Value)
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
