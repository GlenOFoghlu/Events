using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using DublinEventsCollector.Models;
using DublinEventsCollector.Services;

namespace DublinEventsCollector.Collectors;

internal static partial class VenueHtmlParsing
{
    public static string Clean(string value)
    {
        return WebUtility.HtmlDecode(StripTagsRegex().Replace(value, " "))
            .Replace('\u00a0', ' ')
            .Trim();
    }

    public static string? MatchValue(Regex regex, string html)
    {
        var match = regex.Match(html);
        return match.Success ? Clean(match.Groups["value"].Value) : null;
    }

    public static string? MatchRawValue(Regex regex, string html)
    {
        var match = regex.Match(html);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["value"].Value).Trim() : null;
    }

    public static string? AbsoluteUrl(string baseUrl, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var baseUri = new Uri(baseUrl);

        if (value.StartsWith("//", StringComparison.Ordinal))
        {
            return $"{baseUri.Scheme}:{value}";
        }

        if (value.StartsWith("/", StringComparison.Ordinal))
        {
            return new Uri(new Uri($"{baseUri.Scheme}://{baseUri.Host}"), value).ToString();
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var absolute)
            ? absolute.ToString()
            : new Uri(baseUri, value).ToString();
    }

    public static DateTimeOffset? ParseVenueDate(string value, EventWindow window)
    {
        var cleaned = Clean(value)
            .Replace(".", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(",", string.Empty, StringComparison.OrdinalIgnoreCase);

        cleaned = OrdinalSuffixRegex().Replace(cleaned, "$1");
        cleaned = cleaned.Replace("Sept", "Sep", StringComparison.OrdinalIgnoreCase);

        var compactRange = CompactRangeRegex().Match(cleaned);
        if (compactRange.Success)
        {
            return ParseDayMonthYear(compactRange, window);
        }

        var rangeParts = Regex.Split(cleaned, @"\s+(?:-|–|&|to)\s+", RegexOptions.IgnoreCase)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        var firstPart = rangeParts.FirstOrDefault() ?? cleaned;
        var month = MonthRegex().Matches(cleaned).Select(match => match.Value).LastOrDefault();
        var year = YearRegex().Matches(cleaned).Select(match => match.Value).LastOrDefault();

        if (!MonthRegex().IsMatch(firstPart) && month is not null)
        {
            firstPart = $"{firstPart} {month}";
        }

        if (!YearRegex().IsMatch(firstPart))
        {
            firstPart = $"{firstPart} {year ?? InferYear(firstPart, window).ToString(CultureInfo.InvariantCulture)}";
        }

        firstPart = WeekdayRegex().Replace(firstPart, string.Empty).Trim();

        var formats = new[]
        {
            "d MMM yyyy",
            "dd MMM yyyy",
            "d MMMM yyyy",
            "dd MMMM yyyy",
            "d/M/yyyy",
            "dd/M/yyyy",
            "d/MM/yyyy",
            "dd/MM/yyyy",
            "d/MMM/yyyy",
            "dd/MMM/yyyy"
        };

        if (!DateTime.TryParseExact(firstPart, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            && !DateTime.TryParse(firstPart, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
        {
            var directRange = DayMonthYearRegex().Match(cleaned);
            return directRange.Success ? ParseDayMonthYear(directRange, window) : null;
        }

        return DublinDateTime.FromLocal(DateOnly.FromDateTime(parsed));
    }

    private static DateTimeOffset? ParseDayMonthYear(Match match, EventWindow window)
    {
        var text = $"{match.Groups["day"].Value} {match.Groups["month"].Value} {match.Groups["year"].Value}";
        return DateTime.TryParseExact(
            text,
            ["d MMM yyyy", "dd MMM yyyy", "d MMMM yyyy", "dd MMMM yyyy"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? DublinDateTime.FromLocal(DateOnly.FromDateTime(parsed))
            : null;
    }

    private static int InferYear(string dateText, EventWindow window)
    {
        var year = window.From.Year;
        var textWithYear = $"{WeekdayRegex().Replace(dateText, string.Empty).Trim()} {year}";

        if (DateTime.TryParse(textWithYear, CultureInfo.InvariantCulture, DateTimeStyles.None, out var candidate)
            && DateOnly.FromDateTime(candidate) < window.From
            && candidate.Month < window.From.Month)
        {
            return year + 1;
        }

        return year;
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex StripTagsRegex();

    [GeneratedRegex(@"(\d{1,2})(st|nd|rd|th)", RegexOptions.IgnoreCase)]
    private static partial Regex OrdinalSuffixRegex();

    [GeneratedRegex(@"\b(Mar|Apr|May|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec|Jan|Feb|March|April|June|July|August|September|October|November|December|January|February)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MonthRegex();

    [GeneratedRegex(@"\b20\d{2}\b")]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"\b(Mon|Tue|Wed|Thu|Fri|Sat|Sun|Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)\b", RegexOptions.IgnoreCase)]
    private static partial Regex WeekdayRegex();

    [GeneratedRegex(@"\b(?<day>\d{1,2})\s+(?<month>Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec|Jan|Feb|March|April|June|July|August|September|October|November|December|January|February)\s+(?<year>20\d{2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex DayMonthYearRegex();

    [GeneratedRegex(@"\b(?<day>\d{1,2})\s*(?:-|–|to)\s*\d{1,2}\s+(?<month>Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec|Jan|Feb|March|April|June|July|August|September|October|November|December|January|February)\s+(?<year>20\d{2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex CompactRangeRegex();
}
