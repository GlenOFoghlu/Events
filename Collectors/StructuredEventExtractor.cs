using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using DublinEventsCollector.Models;
using DublinEventsCollector.Services;

namespace DublinEventsCollector.Collectors;

internal static partial class StructuredEventExtractor
{
    public static IReadOnlyList<EventItem> ExtractJsonLdEvents(
        string source,
        string html,
        string defaultVenue,
        EventWindow window)
    {
        var events = new List<EventItem>();

        foreach (Match match in JsonLdScriptRegex().Matches(html))
        {
            var json = WebUtility.HtmlDecode(match.Groups["json"].Value);
            try
            {
                using var document = JsonDocument.Parse(json);
                ExtractElement(source, document.RootElement, defaultVenue, window, events);
            }
            catch (JsonException)
            {
                // Some sites include non-standard JSON-LD. Ignore it and continue to the page fallback.
            }
        }

        return events;
    }

    private static void ExtractElement(
        string source,
        JsonElement element,
        string defaultVenue,
        EventWindow window,
        List<EventItem> events)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                ExtractElement(source, item, defaultVenue, window, events);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (element.TryGetProperty("@graph", out var graph))
        {
            ExtractElement(source, graph, defaultVenue, window, events);
        }

        var type = element.String("@type");
        if (!string.Equals(type, "Event", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var start = DublinDateTime.ParseIsoOrLocal(element.String("startDate"));
        if (start is null || !DublinDateTime.IsInside(window, start.Value))
        {
            return;
        }

        var location = element.Property("location");
        var offers = element.Property("offers");
        var image = element.Property("image");

        events.Add(new EventItem
        {
            Source = source,
            SourceEventId = element.String("@id") ?? element.String("url") ?? $"{source}-{events.Count}",
            Title = element.String("name") ?? $"Untitled {source} event",
            Venue = location?.String("name") ?? defaultVenue,
            StartsAt = start.Value,
            EndsAt = DublinDateTime.ParseIsoOrLocal(element.String("endDate")),
            Url = element.String("url"),
            ImageUrl = image?.ValueKind == JsonValueKind.String ? image.Value.GetString() : null,
            Category = element.String("eventAttendanceMode"),
            PriceMin = offers?.Decimal("lowPrice") ?? offers?.Decimal("price"),
            PriceMax = offers?.Decimal("highPrice"),
            Currency = offers?.String("priceCurrency"),
            Status = element.String("eventStatus")
        });
    }

    [GeneratedRegex("<script[^>]+type=[\"']application/ld\\+json[\"'][^>]*>(?<json>.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex JsonLdScriptRegex();
}
