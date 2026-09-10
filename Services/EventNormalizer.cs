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
            .Select(group => group.OrderByDescending(CompletenessScore).First())
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
