using System.Globalization;
using System.Text.RegularExpressions;
using DublinEventsCollector.Models;
using DublinEventsCollector.Services;

namespace DublinEventsCollector.Collectors;

public sealed partial class AbbeyTheatreCandidateSource(IHttpClientFactory httpClientFactory) : IEventSource
{
    private const string EventsUrl = "https://www.abbeytheatre.ie/whats-on/";

    public string Name => "abbey-theatre";
    public string Description => "Abbey Theatre What's On listings.";
    public bool RequiresConfiguration => false;

    public async Task<EventSourceResult> CollectAsync(EventWindow window, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        var events = new Dictionary<string, EventItem>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        foreach (var url in FilterUrls(window))
        {
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                warnings.Add($"Abbey Theatre returned {(int)response.StatusCode} for {url}.");
                continue;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            foreach (var eventItem in ParseCards(html, window))
            {
                events[eventItem.SourceEventId] = eventItem;
            }
        }

        if (events.Count == 0 && warnings.Count > 0)
        {
            return EventSourceResult.Empty(Name, warnings.ToArray());
        }

        return new EventSourceResult(Name, events.Values.OrderBy(item => item.StartsAt).ToArray(), warnings);
    }

    private static IEnumerable<string> FilterUrls(EventWindow window)
    {
        var month = new DateOnly(window.From.Year, window.From.Month, 1);
        var lastMonth = new DateOnly(window.To.Year, window.To.Month, 1);

        while (month <= lastMonth)
        {
            var monthName = month.ToString("MMM", CultureInfo.InvariantCulture).ToLowerInvariant();
            yield return $"{EventsUrl}?filter={monthName}-{month.Year}";
            month = month.AddMonths(1);
        }
    }

    private IEnumerable<EventItem> ParseCards(string html, EventWindow window)
    {
        foreach (Match match in EventCardRegex().Matches(html))
        {
            var title = VenueHtmlParsing.Clean(match.Groups["title"].Value);
            var dateText = VenueHtmlParsing.Clean(match.Groups["date"].Value);
            var startsAt = ParseAbbeyDate(dateText);

            if (string.IsNullOrWhiteSpace(title) || startsAt is null || !DublinDateTime.IsInside(window, startsAt.Value))
            {
                continue;
            }

            yield return new EventItem
            {
                Source = Name,
                SourceEventId = $"abbey-{startsAt:yyyyMMdd}-{title}",
                Title = title,
                Venue = "Abbey Theatre",
                StartsAt = startsAt.Value,
                Url = VenueHtmlParsing.AbsoluteUrl(EventsUrl, match.Groups["url"].Value),
                ImageUrl = VenueHtmlParsing.AbsoluteUrl(EventsUrl, match.Groups["image"].Value)
            };
        }
    }

    private static DateTimeOffset? ParseAbbeyDate(string dateText)
    {
        foreach (var regex in new[] { SameMonthRangeRegex(), CrossMonthRangeRegex(), SingleDateRegex() })
        {
            var match = regex.Match(dateText);
            if (!match.Success)
            {
                continue;
            }

            var text = $"{match.Groups["day"].Value} {match.Groups["month"].Value} {match.Groups["year"].Value}";
            if (DateTime.TryParseExact(
                text,
                ["d MMM yyyy", "dd MMM yyyy", "d MMMM yyyy", "dd MMMM yyyy"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
            {
                return DublinDateTime.FromLocal(DateOnly.FromDateTime(parsed));
            }
        }

        return null;
    }

    [GeneratedRegex("<div class=\"whats-on-event-card.*?background-image: url\\('(?<image>[^']+)'\\).*?<h1 class=\"event-card-title[^\"]*\">(?<title>.*?)</h1>\\s*<p class=\"event-card-date[^\"]*\">(?<date>.*?)</p>.*?<a href=\"(?<url>[^\"]+)\" class=\"event-card-btn", RegexOptions.Singleline)]
    private static partial Regex EventCardRegex();

    [GeneratedRegex(@"\b(?<day>\d{1,2})\s*(?:-|–|to)\s*\d{1,2}\s+(?<month>[A-Za-z]+)\s+(?<year>20\d{2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex SameMonthRangeRegex();

    [GeneratedRegex(@"\b(?<day>\d{1,2})\s+(?<month>[A-Za-z]+)\s*(?:-|–|to)\s*\d{1,2}\s+[A-Za-z]+\s+(?<year>20\d{2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex CrossMonthRangeRegex();

    [GeneratedRegex(@"\b(?<day>\d{1,2})\s+(?<month>[A-Za-z]+)\s+(?<year>20\d{2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex SingleDateRegex();
}
