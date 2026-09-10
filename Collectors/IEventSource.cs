using DublinEventsCollector.Models;

namespace DublinEventsCollector.Collectors;

public interface IEventSource
{
    string Name { get; }
    string Description { get; }
    bool RequiresConfiguration { get; }

    Task<EventSourceResult> CollectAsync(EventWindow window, CancellationToken cancellationToken);
}

public sealed record EventSourceResult(
    string Source,
    IReadOnlyList<EventItem> Events,
    IReadOnlyList<string> Warnings)
{
    public static EventSourceResult Empty(string source, params string[] warnings) => new(source, [], warnings);
}
