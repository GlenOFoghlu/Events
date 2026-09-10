using System.Text.RegularExpressions;
using DublinEventsCollector.Models;
using DublinEventsCollector.Services;

namespace DublinEventsCollector.Collectors;

public sealed partial class GaietyTheatreCandidateSource(IHttpClientFactory httpClientFactory) : IEventSource
{
    private const string EventsUrl = "https://www.gaietytheatre.ie/events/";

    public string Name => "gaiety-theatre";
    public string Description => "Gaiety Theatre Dublin events listings.";
    public bool RequiresConfiguration => false;

    public async Task<EventSourceResult> CollectAsync(EventWindow window, CancellationToken cancellationToken)
    {
        var events = new List<EventItem>();
        var warnings = new List<string>();
        var client = httpClientFactory.CreateClient();

        for (var page = 1; page <= 3; page++)
        {
            var url = page == 1 ? EventsUrl : $"{EventsUrl}page/{page}/";
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                warnings.Add($"Gaiety page {page} returned {(int)response.StatusCode}.");
                break;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var pageEvents = ParseCards(html, window).ToArray();
            events.AddRange(pageEvents);

            if (page > 1 && pageEvents.Length == 0)
            {
                break;
            }
        }

        return new EventSourceResult(Name, events, warnings);
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
                SourceEventId = $"gaiety-{startsAt:yyyyMMdd}-{title}",
                Title = title,
                Venue = "Gaiety Theatre",
                StartsAt = startsAt.Value,
                Url = VenueHtmlParsing.AbsoluteUrl(EventsUrl, VenueHtmlParsing.MatchRawValue(LinkRegex(), card)),
                ImageUrl = VenueHtmlParsing.AbsoluteUrl(EventsUrl, VenueHtmlParsing.MatchRawValue(ImageRegex(), card))
            };
        }
    }

    [GeneratedRegex("<div class=\"col-6 col-md-3 event-container.*?</div><!-- \\.event -->", RegexOptions.Singleline)]
    private static partial Regex CardRegex();

    [GeneratedRegex("<h3 class=\"event-title\">(?<value>.*?)</h3>", RegexOptions.Singleline)]
    private static partial Regex TitleRegex();

    [GeneratedRegex("<p class=\"event-date\"><small>(?<value>.*?)</small></p>", RegexOptions.Singleline)]
    private static partial Regex DateRegex();

    [GeneratedRegex("<a href=\"(?<value>[^\"]+)\"[^>]*><img", RegexOptions.Singleline)]
    private static partial Regex LinkRegex();

    [GeneratedRegex("<img src=\"(?<value>[^\"]+)\"", RegexOptions.Singleline)]
    private static partial Regex ImageRegex();
}
