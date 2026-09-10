using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DublinEventsCollector.Models;
using DublinEventsCollector.Services;

namespace DublinEventsCollector.Collectors;

public sealed partial class ButtonFactoryEventSource(IHttpClientFactory httpClientFactory) : IEventSource
{
    private const string ShowsUrl = "https://buttonfactory.ie/shows";
    private const int MaxCalendarDownloads = 220;
    private static readonly TimeZoneInfo DublinTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin");

    public string Name => "button-factory";
    public string Description => "Button Factory per-event ICS calendar listings.";
    public bool RequiresConfiguration => false;

    public async Task<EventSourceResult> CollectAsync(EventWindow window, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

        using var response = await client.GetAsync(ShowsUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return EventSourceResult.Empty(Name, $"Button Factory returned {(int)response.StatusCode}.");
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var calendarUrls = CalendarLinkRegex()
            .Matches(html)
            .Select(match => WebUtility.HtmlDecode(match.Groups["url"].Value))
            .Select(url => VenueHtmlParsing.AbsoluteUrl(ShowsUrl, url))
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxCalendarDownloads)
            .ToArray();

        if (calendarUrls.Length == 0)
        {
            return EventSourceResult.Empty(Name, "Button Factory did not expose any ICS links on the shows page.");
        }

        using var throttle = new SemaphoreSlim(8);
        var results = await Task.WhenAll(calendarUrls.Select(url => FetchCalendarEventAsync(client, url, throttle, cancellationToken)));

        var events = results
            .Select(result => result.Event)
            .Where(item => item is not null && DublinDateTime.IsInside(window, item.StartsAt))
            .Select(item => item!)
            .ToArray();

        var warnings = results
            .Select(result => result.Warning)
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Select(warning => warning!)
            .Distinct()
            .ToArray();

        return new EventSourceResult(Name, events, warnings);
    }

    private async Task<(EventItem? Event, string? Warning)> FetchCalendarEventAsync(
        HttpClient client,
        string calendarUrl,
        SemaphoreSlim throttle,
        CancellationToken cancellationToken)
    {
        await throttle.WaitAsync(cancellationToken);
        try
        {
            using var calendarResponse = await client.GetAsync(calendarUrl, cancellationToken);
            if (!calendarResponse.IsSuccessStatusCode)
            {
                return (null, null);
            }

            var ics = await calendarResponse.Content.ReadAsStringAsync(cancellationToken);
            return (ParseEvent(ics, calendarUrl), null);
        }
        catch (HttpRequestException ex)
        {
            return (null, $"Button Factory calendar fetch failed for {calendarUrl}: {ex.Message}");
        }
        finally
        {
            throttle.Release();
        }
    }

    private EventItem? ParseEvent(string ics, string calendarUrl)
    {
        var properties = ReadProperties(ics);
        var title = Get(properties, "SUMMARY");
        var startsAt = ParseCalendarDate(Get(properties, "DTSTART"));

        if (string.IsNullOrWhiteSpace(title) || startsAt is null)
        {
            return null;
        }

        var endsAt = ParseCalendarDate(Get(properties, "DTEND"));
        var eventUrl = Get(properties, "URL") ?? calendarUrl.Replace("?format=ical", string.Empty, StringComparison.OrdinalIgnoreCase);
        var uid = Get(properties, "UID") ?? eventUrl ?? $"{title}-{startsAt:yyyyMMddHHmm}";

        return new EventItem
        {
            Source = Name,
            SourceEventId = $"button-factory-{StableId(uid)}",
            Title = CleanText(title) ?? title,
            Venue = CleanText(Get(properties, "LOCATION")) ?? "Button Factory",
            City = "Dublin",
            StartsAt = startsAt.Value,
            EndsAt = endsAt,
            Url = eventUrl,
            Category = "Music"
        };
    }

    private static Dictionary<string, string> ReadProperties(string ics)
    {
        var unfolded = new List<string>();

        foreach (var rawLine in ics.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (rawLine.Length > 0 && (rawLine[0] == ' ' || rawLine[0] == '\t') && unfolded.Count > 0)
            {
                unfolded[^1] += rawLine[1..];
                continue;
            }

            unfolded.Add(rawLine);
        }

        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in unfolded)
        {
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var name = line[..separatorIndex].Split(';', 2)[0];
            if (properties.ContainsKey(name))
            {
                continue;
            }

            properties[name] = UnescapeCalendarText(line[(separatorIndex + 1)..]);
        }

        return properties;
    }

    private static string? Get(IReadOnlyDictionary<string, string> properties, string name)
    {
        return properties.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    }

    private static DateTimeOffset? ParseCalendarDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var formats = value.EndsWith("Z", StringComparison.OrdinalIgnoreCase)
            ? ["yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd'T'HHmm'Z'"]
            : new[] { "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm", "yyyyMMdd" };

        if (!DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return null;
        }

        if (value.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
        {
            return new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Utc)).ToOffset(DublinTimeZone.GetUtcOffset(parsed));
        }

        var local = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, DublinTimeZone.GetUtcOffset(local));
    }

    private static string? CleanText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return WhitespaceRegex().Replace(WebUtility.HtmlDecode(value), " ").Trim();
    }

    private static string UnescapeCalendarText(string value)
    {
        return value
            .Replace("\\n", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("\\,", ",", StringComparison.Ordinal)
            .Replace("\\;", ";", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    private static string StableId(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes[..8]).ToLowerInvariant();
    }

    [GeneratedRegex("href=[\"'](?<url>[^\"']+\\?format=ical)[\"']", RegexOptions.IgnoreCase)]
    private static partial Regex CalendarLinkRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();
}
