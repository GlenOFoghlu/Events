using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using DublinEventsCollector.Models;
using DublinEventsCollector.Services;

namespace DublinEventsCollector.Collectors;

public sealed partial class GateTheatreEventSource(IHttpClientFactory httpClientFactory) : IEventSource
{
    private const string ApiBase = "https://tickets.gatetheatre.ie/thegatedublin/api/v3";
    private const string ListingsUrl = "https://gatetheatre.ie/whats-on/";

    public string Name => "gate-theatre";
    public string Description => "Gate Theatre performances from its public Spektrix ticketing feed.";
    public bool RequiresConfiguration => false;

    public async Task<EventSourceResult> CollectAsync(EventWindow window, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DublinEventsCollector/1.0");

        var metadataTask = LoadProductionMetadataAsync(client, cancellationToken);
        var eventsUrl = $"{ApiBase}/events?instanceStart_from={window.From:yyyy-MM-dd}&instanceStart_to={window.To:yyyy-MM-dd}";

        using var response = await client.GetAsync(eventsUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return EventSourceResult.Empty(Name, $"Gate Theatre ticketing feed returned {(int)response.StatusCode}.");
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var productions = ReadData(document.RootElement).ToArray();
        var metadata = await metadataTask;
        var performanceTasks = productions.Select(production => LoadPerformancesAsync(client, production, metadata, window, cancellationToken));
        var events = (await Task.WhenAll(performanceTasks)).SelectMany(items => items).ToArray();

        return new EventSourceResult(Name, events, []);
    }

    private async Task<IReadOnlyList<EventItem>> LoadPerformancesAsync(
        HttpClient client,
        JsonElement production,
        IReadOnlyDictionary<string, ProductionMetadata> metadata,
        EventWindow window,
        CancellationToken cancellationToken)
    {
        var eventId = production.String("id");
        var title = production.String("name");
        if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(title))
        {
            return [];
        }

        var from = window.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = window.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var encodedId = Uri.EscapeDataString(eventId);
        var instancesTask = GetJsonAsync(client, $"{ApiBase}/events/{encodedId}/instances?start_from={from}&start_to={to}", cancellationToken);
        var availabilityTask = GetJsonAsync(client, $"{ApiBase}/events/{encodedId}/availability?start_from={from}&start_to={to}", cancellationToken);

        using var instancesDocument = await instancesTask;
        using var availabilityDocument = await availabilityTask;
        if (instancesDocument is null)
        {
            return [];
        }

        var availability = availabilityDocument is null
            ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            : ReadAvailability(availabilityDocument.RootElement);
        metadata.TryGetValue(NormalizeTitle(title), out var productionMetadata);
        var imageUrl = productionMetadata?.ImageUrl ?? production.String("imageUrl") ?? production.String("thumbnailUrl");
        var url = productionMetadata?.Url ?? BuildBookingUrl(eventId);
        var isOnSale = production.Boolean("isOnSale") == true;
        var events = new List<EventItem>();

        foreach (var instance in ReadData(instancesDocument.RootElement))
        {
            var instanceId = instance.String("id");
            var startsAt = ParseGateDateTime(instance.String("start"));
            if (string.IsNullOrWhiteSpace(instanceId) || startsAt is null || !DublinDateTime.IsInside(window, startsAt.Value))
            {
                continue;
            }

            var cancelled = instance.Boolean("cancelled") == true;
            var soldOut = availability.TryGetValue(instanceId, out var availableSeats) && availableSeats == 0;
            events.Add(new EventItem
            {
                Source = Name,
                SourceEventId = instanceId,
                Title = title,
                Venue = "Gate Theatre",
                StartsAt = startsAt.Value,
                Url = url,
                ImageUrl = imageUrl,
                Category = "Theatre",
                Status = cancelled ? "cancelled" : soldOut ? "sold-out" : isOnSale ? "on-sale" : null
            });
        }

        return events;
    }

    private static async Task<JsonDocument?> GetJsonAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
    }

    private static IEnumerable<JsonElement> ReadData(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray();
        }

        return root.Array("data");
    }

    private static Dictionary<string, int> ReadAvailability(JsonElement root)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in ReadData(root))
        {
            var instanceId = item.String("eventInstanceId");
            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                result[instanceId] = item.Array("availability").Sum(entry => entry.Int32("count") ?? 0);
            }
        }

        return result;
    }

    private static async Task<IReadOnlyDictionary<string, ProductionMetadata>> LoadProductionMetadataAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, ProductionMetadata>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var response = await client.GetAsync(ListingsUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return result;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            foreach (Match match in ProductionCardRegex().Matches(html))
            {
                var title = WebUtility.HtmlDecode(match.Groups["title"].Value).Trim();
                var url = WebUtility.HtmlDecode(match.Groups["url"].Value);
                var imageUrl = WebUtility.HtmlDecode(match.Groups["image"].Value);
                result.TryAdd(NormalizeTitle(title), new ProductionMetadata(url, imageUrl));
            }
        }
        catch (HttpRequestException)
        {
            // The ticketing feed still provides complete listings if the marketing page is unavailable.
        }

        return result;
    }

    private static DateTimeOffset? ParseGateDateTime(string? value)
    {
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var local))
        {
            return null;
        }

        return DublinDateTime.FromLocal(DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local));
    }

    private static string BuildBookingUrl(string eventId)
    {
        var numericId = NumericIdRegex().Match(eventId).Value;
        return string.IsNullOrWhiteSpace(numericId)
            ? ListingsUrl
            : $"https://tickets.gatetheatre.ie/thegatedublin/website/EventDetails.aspx?EventId={numericId}";
    }

    private static string NormalizeTitle(string value) => TitleCharactersRegex().Replace(WebUtility.HtmlDecode(value).ToLowerInvariant(), string.Empty);

    private sealed record ProductionMetadata(string Url, string ImageUrl);

    [GeneratedRegex("<div class=\"item\"[^>]*>.*?<a href=\"(?<url>https://gatetheatre\\.ie/production/[^\"]+)\"[^>]*title=\"(?<title>[^\"]+)\".*?<img src=\"(?<image>[^\"]+)\"[^>]*class=\"item-background-img\"", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ProductionCardRegex();

    [GeneratedRegex("^\\d+")]
    private static partial Regex NumericIdRegex();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex TitleCharactersRegex();
}
