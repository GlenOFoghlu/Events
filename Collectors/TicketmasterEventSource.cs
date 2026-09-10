using System.Globalization;
using System.Text.Json;
using System.Web;
using DublinEventsCollector.Models;
using DublinEventsCollector.Services;
using Microsoft.Extensions.Options;

namespace DublinEventsCollector.Collectors;

public sealed class TicketmasterEventSource(
    IHttpClientFactory httpClientFactory,
    IOptions<CollectorOptions> options) : IEventSource
{
    private static readonly string[] VenueSourcesHandledSeparately = ["3Olympia Theatre"];

    public string Name => "ticketmaster";
    public string Description => "Ticketmaster Discovery API for Dublin, Ireland.";
    public bool RequiresConfiguration => string.IsNullOrWhiteSpace(options.Value.TicketmasterApiKey);

    public async Task<EventSourceResult> CollectAsync(EventWindow window, CancellationToken cancellationToken)
    {
        var apiKey = options.Value.TicketmasterApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return EventSourceResult.Empty(Name, "Set Collector:TicketmasterApiKey or COLLECTOR__TICKETMASTERAPIKEY to enable Ticketmaster.");
        }

        var client = httpClientFactory.CreateClient();
        var events = new List<EventItem>();
        var warnings = new List<string>();

        for (var page = 0; page < 5; page++)
        {
            var uri = BuildUri(apiKey, window, page);
            using var response = await client.GetAsync(uri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                warnings.Add($"Ticketmaster returned {(int)response.StatusCode}.");
                break;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var pageEvents = document.RootElement
                .Property("_embedded")?
                .Array("events")
                .Select(ParseEvent)
                .OfType<EventItem>()
                .ToArray() ?? [];

            events.AddRange(pageEvents);

            var pageInfo = document.RootElement.Property("page");
            var totalPages = pageInfo?.Int32("totalPages");
            if (pageEvents.Length == 0 || totalPages is null || page + 1 >= totalPages)
            {
                break;
            }
        }

        return new EventSourceResult(Name, events, warnings);
    }

    private static Uri BuildUri(string apiKey, EventWindow window, int page)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["apikey"] = apiKey;
        query["city"] = "Dublin";
        query["countryCode"] = "IE";
        query["startDateTime"] = $"{window.From:yyyy-MM-dd}T00:00:00Z";
        query["endDateTime"] = $"{window.To:yyyy-MM-dd}T23:59:59Z";
        query["size"] = "200";
        query["page"] = page.ToString(CultureInfo.InvariantCulture);
        query["sort"] = "date,asc";

        return new UriBuilder("https://app.ticketmaster.com/discovery/v2/events.json")
        {
            Query = query.ToString()
        }.Uri;
    }

    private EventItem? ParseEvent(JsonElement item)
    {
        var dates = item.Property("dates");
        var start = dates?.Property("start");
        var localDate = start?.String("localDate");
        if (!DateOnly.TryParse(localDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return null;
        }

        TimeOnly? time = null;
        if (TimeOnly.TryParse(start?.String("localTime"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTime))
        {
            time = parsedTime;
        }

        var venue = item.Property("_embedded")?
            .Array("venues")
            .FirstOrDefault()
            .String("name");

        if (VenueSourcesHandledSeparately.Contains(venue, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var priceRange = item.Array("priceRanges").FirstOrDefault();
        var image = item.Array("images")
            .OrderByDescending(image => image.Property("width")?.GetInt32() ?? 0)
            .FirstOrDefault();

        return new EventItem
        {
            Source = Name,
            SourceEventId = item.String("id") ?? item.String("url") ?? Guid.NewGuid().ToString("n"),
            Title = item.String("name") ?? "Untitled Ticketmaster event",
            Venue = venue,
            StartsAt = DublinDateTime.FromLocal(date, time),
            Url = item.String("url"),
            ImageUrl = image.String("url"),
            Category = item.Property("classifications")?.EnumerateArray().FirstOrDefault().Property("segment")?.String("name"),
            PriceMin = priceRange.Decimal("min"),
            PriceMax = priceRange.Decimal("max"),
            Currency = priceRange.String("currency"),
            Status = dates?.Property("status")?.String("code")
        };
    }
}
