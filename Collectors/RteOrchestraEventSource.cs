using System.Globalization;
using System.Text.RegularExpressions;
using DublinEventsCollector.Models;
using DublinEventsCollector.Services;

namespace DublinEventsCollector.Collectors;

public sealed partial class RteOrchestraEventSource(IHttpClientFactory httpClientFactory) : IEventSource
{
    private const string EventsUrl = "https://orchestra.rte.ie/whats-on/";

    public string Name => "rte-orchestra";
    public string Description => "RTÉ Concert Orchestra What's On listings.";
    public bool RequiresConfiguration => false;

    public async Task<EventSourceResult> CollectAsync(EventWindow window, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        using var response = await client.GetAsync(EventsUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return EventSourceResult.Empty(Name, $"RTÉ Orchestra returned {(int)response.StatusCode}.");
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var events = new List<EventItem>();

        foreach (var listing in ParseListings(html, window))
        {
            var detail = await LoadDetailAsync(client, listing, cancellationToken);
            var city = detail.City ?? "Dublin";

            events.Add(listing with
            {
                Venue = detail.Venue ?? listing.Venue,
                City = city,
                StartsAt = detail.StartsAt ?? listing.StartsAt,
                PriceMin = detail.PriceMin,
                PriceMax = detail.PriceMax,
                Currency = detail.PriceMin is null && detail.PriceMax is null ? null : "EUR"
            });
        }

        return new EventSourceResult(Name, events, []);
    }

    private IEnumerable<EventItem> ParseListings(string html, EventWindow window)
    {
        foreach (Match match in EventCardRegex().Matches(html))
        {
            var dateText = match.Groups["date"].Value;
            if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                continue;
            }

            var startsAt = DublinDateTime.FromLocal(date);
            if (!DublinDateTime.IsInside(window, startsAt))
            {
                continue;
            }

            var title = VenueHtmlParsing.Clean(match.Groups["title"].Value);
            var url = VenueHtmlParsing.AbsoluteUrl(EventsUrl, match.Groups["url"].Value);

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            yield return new EventItem
            {
                Source = Name,
                SourceEventId = $"rte-orchestra-{date:yyyyMMdd}-{SlugRegex().Replace(title.ToLowerInvariant(), "-").Trim('-')}",
                Title = title,
                Venue = "RTÉ Concert Orchestra",
                StartsAt = startsAt,
                Url = url,
                ImageUrl = VenueHtmlParsing.AbsoluteUrl(EventsUrl, match.Groups["image"].Value),
                Category = VenueHtmlParsing.Clean(match.Groups["group"].Value)
            };
        }
    }

    private static async Task<RteEventDetail> LoadDetailAsync(HttpClient client, EventItem listing, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(listing.Url))
        {
            return new RteEventDetail(null, null, null, null, null);
        }

        using var response = await client.GetAsync(listing.Url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new RteEventDetail(null, null, null, null, null);
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var venue = VenueHtmlParsing.MatchValue(VenueRegex(), html);
        var city = VenueHtmlParsing.MatchValue(CityRegex(), html);
        var timeText = VenueHtmlParsing.MatchValue(TimeRegex(), html);
        var startsAt = ParseTime(listing.StartsAt, timeText);
        var priceText = VenueHtmlParsing.MatchValue(PriceRegex(), html);
        var (priceMin, priceMax) = ParsePriceRange(priceText);

        return new RteEventDetail(startsAt, venue, city, priceMin, priceMax);
    }

    private static DateTimeOffset? ParseTime(DateTimeOffset listingDate, string? timeText)
    {
        if (string.IsNullOrWhiteSpace(timeText))
        {
            return null;
        }

        var normalized = timeText.Replace(".", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (!DateTime.TryParseExact(
            normalized,
            ["h:mmtt", "htt", "h:mm tt", "h tt"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed))
        {
            return null;
        }

        return DublinDateTime.FromLocal(DateOnly.FromDateTime(listingDate.Date), TimeOnly.FromDateTime(parsed));
    }

    private static (decimal? Min, decimal? Max) ParsePriceRange(string? priceText)
    {
        if (string.IsNullOrWhiteSpace(priceText))
        {
            return (null, null);
        }

        var prices = PriceNumberRegex()
            .Matches(priceText)
            .Select(match => decimal.TryParse(match.Groups["price"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) ? price : (decimal?)null)
            .Where(price => price is not null)
            .Select(price => price!.Value)
            .ToArray();

        return prices.Length switch
        {
            0 => (null, null),
            1 => (prices[0], prices[0]),
            _ => (prices.Min(), prices.Max())
        };
    }

    private sealed record RteEventDetail(
        DateTimeOffset? StartsAt,
        string? Venue,
        string? City,
        decimal? PriceMin,
        decimal? PriceMax);

    [GeneratedRegex("<div class=\"medium-6 columns event-item[^>]*data-eventdate=\"(?<date>[^\"]+)\".*?<a href=\"(?<url>[^\"]+)\" title=\"(?<title>[^\"]+)\">.*?<img src=\"(?<image>[^\"]+)\".*?<div class=\"article-title\">(?<titleText>.*?)</div>\\s*<div class=\"event-pgroup\">(?<group>.*?)</div>", RegexOptions.Singleline)]
    private static partial Regex EventCardRegex();

    [GeneratedRegex("<div class=\"venue\">(?<value>.*?)<span class=\"city\">", RegexOptions.Singleline)]
    private static partial Regex VenueRegex();

    [GeneratedRegex("<span class=\"city\">(?<value>.*?)</span>", RegexOptions.Singleline)]
    private static partial Regex CityRegex();

    [GeneratedRegex("<div class=\"event_time\"><span class=\"label\">Time</span>\\s*<span class=\"time\">(?<value>.*?)</span>", RegexOptions.Singleline)]
    private static partial Regex TimeRegex();

    [GeneratedRegex("<p class=\"eventprice\">(?<value>.*?)</p>", RegexOptions.Singleline)]
    private static partial Regex PriceRegex();

    [GeneratedRegex("€(?<price>\\d+(?:\\.\\d{1,2})?)")]
    private static partial Regex PriceNumberRegex();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugRegex();
}
