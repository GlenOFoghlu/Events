using System.Globalization;
using System.Text;
using DublinEventsCollector.Models;

namespace DublinEventsCollector.Services;

public sealed class EventNormalizer
{
    public IReadOnlyList<EventItem> Dedupe(IEnumerable<EventItem> events)
    {
        return events
            .GroupBy(DedupeKey)
            .Select(group => group
                .OrderByDescending(item => SourcePriority(item.Source))
                .ThenByDescending(CompletenessScore)
                .First())
            .OrderBy(item => item.StartsAt)
            .ThenBy(item => item.Title)
            .ToArray();
    }

    private static string DedupeKey(EventItem item)
    {
        return string.Join('|',
            Slug(item.Title),
            Slug(item.Venue ?? "unknown-venue"),
            item.StartsAt.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private static int CompletenessScore(EventItem item)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(item.Url)) score++;
        if (!string.IsNullOrWhiteSpace(item.ImageUrl)) score++;
        if (!string.IsNullOrWhiteSpace(item.Category)) score++;
        if (!string.IsNullOrWhiteSpace(item.Status)) score++;
        if (item.PriceMin.HasValue || item.PriceMax.HasValue) score++;
        return score;
    }

    private static int SourcePriority(string source)
    {
        return source switch
        {
            "3olympia" or
            "abbey-theatre" or
            "axis-ballymun" or
            "button-factory" or
            "civic-theatre" or
            "draiocht" or
            "fringefest" or
            "gaiety-theatre" or
            "gate-theatre" or
            "mill-theatre" or
            "national-concert-hall" or
            "pavilion-theatre" or
            "rte-orchestra" or
            "smock-alley" or
            "vicar-street" => 30,
            "mcd" => 20,
            "ticketmaster" => 10,
            _ => 0
        };
    }

    private static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (char.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        return string.Join('-', builder.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
