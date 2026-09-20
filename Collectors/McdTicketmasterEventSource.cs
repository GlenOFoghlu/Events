using System.Globalization;
using System.Text.Json;
using System.Web;
using DublinEventsCollector.Models;
using DublinEventsCollector.Services;
using Microsoft.Extensions.Options;

namespace DublinEventsCollector.Collectors;

public sealed class McdTicketmasterEventSource(
    IHttpClientFactory httpClientFactory,
    IOptions<CollectorOptions> options) : IEventSource
{
    private const string McdPromoterId = "966";

    public string Name => "mcd";
    public string Description => "MCD Productions events identified by promoter through the Ticketmaster Discovery API.";
    public bool RequiresConfiguration => string.IsNullOrWhiteSpace(options.Value.TicketmasterApiKey);

    public async Task<EventSourceResult> CollectAsync(EventWindow window, CancellationToken cancellationToken)
    {
        var apiKey = options.Value.TicketmasterApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return EventSourceResult.Empty(Name, "Set Collector:TicketmasterApiKey or COLLECTOR__TICKETMASTERAPIKEY to enable MCD via Ticketmaster.");
        }

        var client = httpClientFactory.CreateClient();
        var events = new List<EventItem>();
        var warnings = new List<string>();

        for (var page = 0; page < 5; page++)
        {
            using var response = await client.GetAsync(BuildUri(apiKey, window, page), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                warnings.Add($"Ticketmaster returned {(int)response.StatusCode} for MCD.");
                break;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var pageEvents = document.RootElement
                .Property("_embedded")?
                .Array("events")
                .Where(IsMcdEvent)
                .Select(ParseEvent)
                .OfType<EventItem>()
                .ToArray() ?? [];

            events.AddRange(pageEvents);

            var totalPages = document.RootElement.Property("page")?.Int32("totalPages");
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
        query["promoterId"] = McdPromoterId;
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

    private static bool IsMcdEvent(JsonElement item) =>
        item.Array("promoters").Any(promoter => promoter.String("id") == McdPromoterId);

    private static EventItem? ParseEvent(JsonElement item)
    {
        var dates = item.Property("dates");
        var start = dates?.Property("start");
        if (!DateOnly.TryParse(start?.String("localDate"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return null;
        }

        TimeOnly? time = null;
        if (TimeOnly.TryParse(start?.String("localTime"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTime))
        {
            time = parsedTime;
        }

        var venue = item.Property("_embedded")?.Array("venues").FirstOrDefault().String("name");
        var priceRange = item.Array("priceRanges").FirstOrDefault();
        var image = item.Array("images")
            .OrderByDescending(candidate => candidate.Int32("width") ?? 0)
            .FirstOrDefault();

        return new EventItem
        {
            Source = "mcd",
            SourceEventId = item.String("id") ?? item.String("url") ?? Guid.NewGuid().ToString("n"),
            Title = item.String("name") ?? "Untitled MCD event",
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
