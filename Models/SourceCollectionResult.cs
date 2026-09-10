namespace DublinEventsCollector.Models;

public sealed record SourceCollectionResult(
    string Source,
    int Count,
    IReadOnlyList<string> Warnings);
