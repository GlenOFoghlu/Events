using System.Text.RegularExpressions;
using System.Web;
using DublinEventsCollector.Models;
using DublinEventsCollector.Services;

namespace DublinEventsCollector.Collectors;

public sealed partial class VicarStreetCandidateSource(IHttpClientFactory httpClientFactory) : IEventSource
{
    private const string EventsUrl = "https://www.vicarstreet.com/all-shows-at-vicar-street.html";

    public string Name => "vicar-street";
    public string Description => "Vicar Street listings.";
    public bool RequiresConfiguration => false;

    public async Task<EventSourceResult> CollectAsync(EventWindow window, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        using var response = await client.GetAsync(EventsUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return EventSourceResult.Empty(Name, $"Vicar Street returned {(int)response.StatusCode}.");
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        return new EventSourceResult(Name, ParseCards(html, window).ToArray(), []);
    }

    private IEnumerable<EventItem> ParseCards(string html, EventWindow window)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in ShowLinkRegex().Matches(html))
        {
            var relativeUrl = HttpUtility.HtmlDecode(match.Groups["url"].Value);
            if (!seen.Add(relativeUrl))
            {
                continue;
            }

            var slug = match.Groups["slug"].Value;
            var title = BuildTitle(slug);
            var dateText = match.Groups["date"].Value;
            var startsAt = dateText is null ? null : VenueHtmlParsing.ParseVenueDate(dateText, window);

            if (string.IsNullOrWhiteSpace(title) || startsAt is null || !DublinDateTime.IsInside(window, startsAt.Value))
            {
                continue;
            }

            yield return new EventItem
            {
                Source = Name,
                SourceEventId = $"vicar-{startsAt:yyyyMMdd}-{title}",
                Title = title,
                Venue = "Vicar Street",
                StartsAt = startsAt.Value,
                Url = VenueHtmlParsing.AbsoluteUrl(EventsUrl, relativeUrl)
            };
        }
    }

    private static string BuildTitle(string slug)
    {
        var title = Regex.Replace(slug, "-\\d{8,}$", string.Empty);
        title = title.Replace('-', ' ');
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(title);
    }

    [GeneratedRegex("(?<url>/thelist-dashboard/show/\\d+-(?<slug>.*?)-live-in-vicar-street.*?-on-(?<date>\\d{1,2}-[A-Za-z]{3}-\\d{4})\\.html)", RegexOptions.Singleline)]
    private static partial Regex ShowLinkRegex();
}
