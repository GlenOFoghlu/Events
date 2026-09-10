using DublinEventsCollector.Collectors;
using DublinEventsCollector.Models;

namespace DublinEventsCollector.Services;

public sealed class EventCollector(IEnumerable<IEventSource> sources, EventNormalizer normalizer)
{
    public async Task<CollectedEvents> CollectAsync(EventWindow window, CancellationToken cancellationToken)
    {
        var sourceResults = await Task.WhenAll(
            sources.Select(source => source.CollectAsync(window, cancellationToken)));

        var events = normalizer.Dedupe(sourceResults.SelectMany(result => result.Events));
        var summaries = sourceResults
            .Select(result => new SourceCollectionResult(result.Source, result.Events.Count, result.Warnings))
            .OrderBy(result => result.Source)
            .ToArray();

        return new CollectedEvents(events, summaries);
    }
}

public sealed record CollectedEvents(
    IReadOnlyList<EventItem> Events,
    IReadOnlyList<SourceCollectionResult> SourceResults);
