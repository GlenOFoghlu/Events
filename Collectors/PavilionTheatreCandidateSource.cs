using System.Text.RegularExpressions;
using DublinEventsCollector.Models;
using DublinEventsCollector.Services;

namespace DublinEventsCollector.Collectors;

public sealed partial class PavilionTheatreCandidateSource(IHttpClientFactory httpClientFactory) : IEventSource
{
    private const string EventsUrl = "https://www.paviliontheatre.ie/events/";

    public string Name => "pavilion-theatre";
    public string Description => "Pavilion Theatre Dún Laoghaire event listings.";
    public bool RequiresConfiguration => false;

    public async Task<EventSourceResult> CollectAsync(EventWindow window, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        using var response = await client.GetAsync(EventsUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return EventSourceResult.Empty(Name, $"Pavilion Theatre returned {(int)response.StatusCode}.");
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        return new EventSourceResult(Name, ParseCards(html, window).ToArray(), []);
    }

    private IEnumerable<EventItem> ParseCards(string html, EventWindow window)
    {
        foreach (Match match in CardRegex().Matches(html))
        {
            var card = match.Value;
            var title = VenueHtmlParsing.MatchValue(TitleRegex(), card);
            var dateText = VenueHtmlParsing.MatchValue(DateRegex(), card);
            var startsAt = dateText is null ? null : VenueHtmlParsing.ParseVenueDate(dateText, window);

            if (string.IsNullOrWhiteSpace(title) || startsAt is null || !DublinDateTime.IsInside(window, startsAt.Value))
            {
                continue;
            }

            yield return new EventItem
            {
                Source = Name,
                SourceEventId = $"pavilion-{startsAt:yyyyMMdd}-{title}",
                Title = title,
                Venue = "Pavilion Theatre",
                StartsAt = startsAt.Value,
                Url = VenueHtmlParsing.AbsoluteUrl(EventsUrl, VenueHtmlParsing.MatchRawValue(LinkRegex(), card)),
                ImageUrl = VenueHtmlParsing.AbsoluteUrl(EventsUrl, VenueHtmlParsing.MatchRawValue(ImageRegex(), card)),
                Category = VenueHtmlParsing.MatchValue(CategoryRegex(), card)
            };
        }
    }

    [GeneratedRegex("<a href=\"https://www\\.paviliontheatre\\.ie/events/view/.*?(?=<a href=\"https://www\\.paviliontheatre\\.ie/events/view/|<div class=\"pagination|</main>)", RegexOptions.Singleline)]
    private static partial Regex CardRegex();

    [GeneratedRegex("<a class=\"event_title\"[^>]*><h2>(?<value>.*?)</h2></a>", RegexOptions.Singleline)]
    private static partial Regex TitleRegex();

    [GeneratedRegex("<div class=\"date\">(?<value>.*?)</div>", RegexOptions.Singleline)]
    private static partial Regex DateRegex();

    [GeneratedRegex("<a href=\"(?<value>https://www\\.paviliontheatre\\.ie/events/view/[^\"]+)\" class=\"dim\"", RegexOptions.Singleline)]
    private static partial Regex LinkRegex();

    [GeneratedRegex("<img src=\"(?<value>[^\"]+)\"", RegexOptions.Singleline)]
    private static partial Regex ImageRegex();

    [GeneratedRegex("<a style=\"text-transform: uppercase;.*?>(?<value>.*?)</a>", RegexOptions.Singleline)]
    private static partial Regex CategoryRegex();
}
