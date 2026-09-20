using DublinEventsCollector.Collectors;
using DublinEventsCollector.Models;

namespace DublinEventsCollector.Services;

public sealed class EventCollector(
    IEnumerable<IEventSource> sources,
    EventNormalizer normalizer,
    ILogger<EventCollector> logger)
{
    public async Task<CollectedEvents> CollectAsync(EventWindow window, CancellationToken cancellationToken)
    {
        var sourceResults = await Task.WhenAll(
            sources.Select(source => CollectSourceAsync(source, window, cancellationToken)));

        var events = normalizer.Dedupe(sourceResults.SelectMany(result => result.Events));
        var summaries = sourceResults
            .Select(result => new SourceCollectionResult(result.Source, result.Events.Count, result.Warnings))
            .OrderBy(result => result.Source)
            .ToArray();

        return new CollectedEvents(events, summaries);
    }

    private async Task<EventSourceResult> CollectSourceAsync(
        IEventSource source,
        EventWindow window,
        CancellationToken cancellationToken)
    {
        try
        {
            return await source.CollectAsync(window, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Event source {Source} could not be collected.", source.Name);
            return EventSourceResult.Empty(source.Name, $"{source.Name} is temporarily unavailable.");
        }
    }
}

public sealed record CollectedEvents(
    IReadOnlyList<EventItem> Events,
    IReadOnlyList<SourceCollectionResult> SourceResults);
