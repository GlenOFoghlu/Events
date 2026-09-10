using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using DublinEventsCollector.Models;
using DublinEventsCollector.Services;

namespace DublinEventsCollector.Collectors;

public sealed partial class NationalConcertHallEventSource(IHttpClientFactory httpClientFactory) : IEventSource
{
    private const string EventsUrl = "https://www.nch.ie/all-events-listing/";

    public string Name => "national-concert-hall";
    public string Description => "National Concert Hall public events listing.";
    public bool RequiresConfiguration => false;

    public async Task<EventSourceResult> CollectAsync(EventWindow window, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        var events = new List<EventItem>();
        var warnings = new List<string>();

        for (var page = 1; page <= 12; page++)
        {
            var pageUrl = page == 1 ? EventsUrl : $"{EventsUrl}?page={page}";
            using var response = await client.GetAsync(pageUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                warnings.Add($"National Concert Hall page {page} returned {(int)response.StatusCode}.");
                break;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var structured = StructuredEventExtractor.ExtractJsonLdEvents(Name, html, "National Concert Hall", window);
            events.AddRange(structured.Count > 0 ? structured : ExtractCards(html, window, pageUrl));
        }

        if (events.Count == 0)
        {
            warnings.Add("No events found. The NCH page markup may have changed or the requested window may be outside the scanned pages.");
        }

        return new EventSourceResult(Name, events, warnings);
    }

    private static IReadOnlyList<EventItem> ExtractCards(string html, EventWindow window, string pageUrl)
    {
        var events = new List<EventItem>();

        foreach (Match match in FeatureCardRegex().Matches(html))
        {
            var cardHtml = match.Value;
            var title = WebUtility.HtmlDecode(match.Groups["title"].Value).Trim();
            var dateText = WebUtility.HtmlDecode(match.Groups["date"].Value).Trim();

            if (string.IsNullOrWhiteSpace(title) || !TryParseNchDate(dateText, out var startsAt))
            {
                continue;
            }

            if (!DublinDateTime.IsInside(window, startsAt))
            {
                continue;
            }

            events.Add(new EventItem
            {
                Source = "national-concert-hall",
                SourceEventId = $"nch-{startsAt:yyyyMMddHHmm}-{Slug(title)}",
                Title = title,
                Venue = "National Concert Hall",
                StartsAt = startsAt,
                Url = MakeAbsoluteUrl(ReadCardValue(LinkRegex(), cardHtml)) ?? pageUrl,
                ImageUrl = ReadCardValue(ImageRegex(), cardHtml),
                Category = ReadCardValue(CategoryRegex(), cardHtml),
                Status = cardHtml.Contains("Sold Out", StringComparison.OrdinalIgnoreCase) ? "sold-out" : null
            });
        }

        return events;
    }

    private static string? ReadCardValue(Regex regex, string cardHtml)
    {
        var match = regex.Match(cardHtml);
        return match.Success ? WebUtility.HtmlDecode(StripTagsRegex().Replace(match.Groups["value"].Value, string.Empty)).Trim() : null;
    }

    private static string? MakeAbsoluteUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.StartsWith("/", StringComparison.Ordinal))
        {
            return new Uri(new Uri("https://www.nch.ie"), value).ToString();
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var absolute)
            ? absolute.ToString()
            : new Uri(new Uri(EventsUrl), value).ToString();
    }

    private static bool TryParseNchDate(string? value, out DateTimeOffset startsAt)
    {
        startsAt = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = NchDateRegex().Match(value);
        if (!match.Success)
        {
            return false;
        }

        var dateText = match.Groups["date"].Value.Replace("Sept", "Sep", StringComparison.OrdinalIgnoreCase);
        var timeText = match.Groups["time"].Value.Replace(".", string.Empty, StringComparison.OrdinalIgnoreCase);
        var fullText = $"{dateText} {timeText}";

        if (!DateTime.TryParseExact(
                fullText,
                ["dddd d MMMM yyyy h:mmtt", "dddd d MMM yyyy h:mmtt"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return false;
        }

        startsAt = DublinDateTime.FromLocal(DateOnly.FromDateTime(parsed), TimeOnly.FromDateTime(parsed));
        return true;
    }

    private static string Slug(string value) => string.Join('-', value.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex StripTagsRegex();

    [GeneratedRegex("(?<date>\\w+\\s+\\d{1,2}\\s+\\w+\\s+\\d{4})\\s+(?<time>\\d{1,2}:\\d{2}\\s*(?:AM|PM))", RegexOptions.IgnoreCase)]
    private static partial Regex NchDateRegex();

    [GeneratedRegex("<div[^>]*class=\"feature-card\"[^>]*>.*?<h2[^>]*class=\"title\"[^>]*>(?<title>.*?)</h2>.*?<p[^>]*class=\"meta\"[^>]*>(?<date>\\w+\\s+\\d{1,2}\\s+\\w+\\s+\\d{4}\\s+\\d{1,2}:\\d{2}\\s*(?:AM|PM))</p>.*?</div>\\s*</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex FeatureCardRegex();

    [GeneratedRegex("<h2[^>]*class=\"title\"[^>]*>(?<value>.*?)</h2>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRegex();

    [GeneratedRegex("<p[^>]*class=\"meta\"[^>]*>(?<value>\\w+\\s+\\d{1,2}\\s+\\w+\\s+\\d{4}\\s+\\d{1,2}:\\d{2}\\s*(?:AM|PM))</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DateRegex();

    [GeneratedRegex("<p[^>]*class=\"meta\"[^>]*>(?<value>(?!\\w+\\s+\\d{1,2}\\s+\\w+\\s+\\d{4}).*?)</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CategoryRegex();

    [GeneratedRegex("<img[^>]+src=\"(?<value>[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ImageRegex();

    [GeneratedRegex("<a[^>]+href=\"(?<value>[^\"]+)\"[^>]*class=\"btn-ghost", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LinkRegex();
}
