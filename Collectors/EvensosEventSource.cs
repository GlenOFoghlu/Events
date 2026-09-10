using System.Text.Json;
using DublinEventsCollector.Models;
using DublinEventsCollector.Services;
using Microsoft.Extensions.Options;

namespace DublinEventsCollector.Collectors;

public sealed class EvensosEventSource(
    IHttpClientFactory httpClientFactory,
    IOptions<CollectorOptions> options) : IEventSource
{
    public string Name => "evensos";
    public string Description => "Evensos Dublin machine-readable JSON-LD event feed.";
    public bool RequiresConfiguration => !options.Value.EnableEvensos;

    public async Task<EventSourceResult> CollectAsync(EventWindow window, CancellationToken cancellationToken)
    {
        if (!options.Value.EnableEvensos)
        {
            return EventSourceResult.Empty(Name, "Evensos is available but disabled. Set Collector:EnableEvensos to true to enable it.");
        }

        var client = httpClientFactory.CreateClient();
        using var response = await client.GetAsync(options.Value.EvensosDublinJsonUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return EventSourceResult.Empty(Name, $"Evensos returned {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var events = document.RootElement
            .Array("dataFeedElement")
            .Select(element => ParseEvent(element.Property("item"), window))
            .OfType<EventItem>()
            .ToArray();

        return new EventSourceResult(Name, events, []);
    }

    private EventItem? ParseEvent(JsonElement? item, EventWindow window)
    {
        if (item is null)
        {
            return null;
        }

        var start = DublinDateTime.ParseIsoOrLocal(item.Value.String("startDate"));
        if (start is null || !DublinDateTime.IsInside(window, start.Value))
        {
            return null;
        }

        var location = item.Value.Property("location");
        var offers = item.Value.Property("offers");
        var image = item.Value.Property("image");

        return new EventItem
        {
            Source = Name,
            SourceEventId = item.Value.String("@id") ?? item.Value.String("url") ?? Guid.NewGuid().ToString("n"),
            Title = item.Value.String("name") ?? "Untitled Evensos event",
            Venue = location?.String("name"),
            StartsAt = start.Value,
            EndsAt = DublinDateTime.ParseIsoOrLocal(item.Value.String("endDate")),
            Url = item.Value.String("url"),
            ImageUrl = image?.ValueKind == JsonValueKind.Array
                ? image.Value.EnumerateArray().FirstOrDefault().GetString()
                : image?.GetString(),
            Category = item.Value.String("@type"),
            Currency = offers?.String("priceCurrency"),
            Status = ShortSchemaValue(item.Value.String("eventStatus"))
        };
    }

    private static string? ShortSchemaValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Replace("https://schema.org/", string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
