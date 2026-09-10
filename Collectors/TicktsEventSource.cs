using System.Text.Json;
using DublinEventsCollector.Models;
using DublinEventsCollector.Services;
using Microsoft.Extensions.Options;

namespace DublinEventsCollector.Collectors;

public sealed class TicktsEventSource(
    IHttpClientFactory httpClientFactory,
    IOptions<CollectorOptions> options) : IEventSource
{
    public string Name => "tickts";
    public string Description => "Tickts Dublin JSON feed, when publishing events.";
    public bool RequiresConfiguration => !options.Value.EnableTickts;

    public async Task<EventSourceResult> CollectAsync(EventWindow window, CancellationToken cancellationToken)
    {
        if (!options.Value.EnableTickts)
        {
            return EventSourceResult.Empty(Name, "Tickts is available as a JSON/iCal feed but is disabled. Set Collector:EnableTickts to true after confirming the feed has Dublin events.");
        }

        var client = httpClientFactory.CreateClient();
        using var response = await client.GetAsync(options.Value.TicktsDublinJsonUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return EventSourceResult.Empty(Name, $"Tickts returned {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray()
            : document.RootElement.Array("events");

        var events = root
            .Select(item => ParseEvent(item, window))
            .OfType<EventItem>()
            .ToArray();

        return new EventSourceResult(Name, events, []);
    }

    private EventItem? ParseEvent(JsonElement item, EventWindow window)
    {
        var start = DublinDateTime.ParseIsoOrLocal(item.String("datetime") ?? item.String("startDate") ?? item.String("start"));
        if (start is null || !DublinDateTime.IsInside(window, start.Value))
        {
            return null;
        }

        return new EventItem
        {
            Source = Name,
            SourceEventId = item.String("id") ?? item.String("link") ?? Guid.NewGuid().ToString("n"),
            Title = item.String("title") ?? "Untitled Tickts event",
            Venue = item.String("venue"),
            StartsAt = start.Value,
            Url = item.String("link") ?? item.String("url"),
            Category = item.String("category"),
            Currency = item.String("currency")
        };
    }
}
