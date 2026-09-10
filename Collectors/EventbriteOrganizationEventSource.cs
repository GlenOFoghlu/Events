using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Web;
using DublinEventsCollector.Models;
using DublinEventsCollector.Services;
using Microsoft.Extensions.Options;

namespace DublinEventsCollector.Collectors;

public sealed class EventbriteOrganizationEventSource(
    IHttpClientFactory httpClientFactory,
    IOptions<CollectorOptions> options) : IEventSource
{
    public string Name => "eventbrite";
    public string Description => "Eventbrite organization and venue events API for known IDs.";
    public bool RequiresConfiguration => string.IsNullOrWhiteSpace(options.Value.EventbriteToken)
        || (options.Value.EventbriteOrganizationIds.Length == 0 && options.Value.EventbriteVenueIds.Length == 0);

    public async Task<EventSourceResult> CollectAsync(EventWindow window, CancellationToken cancellationToken)
    {
        var token = options.Value.EventbriteToken;
        var organizationIds = options.Value.EventbriteOrganizationIds;
        var venueIds = options.Value.EventbriteVenueIds;

        if (string.IsNullOrWhiteSpace(token) || (organizationIds.Length == 0 && venueIds.Length == 0))
        {
            return EventSourceResult.Empty(Name, "Set Collector:EventbriteToken plus EventbriteOrganizationIds and/or EventbriteVenueIds.");
        }

        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var events = new List<EventItem>();
        var warnings = new List<string>();
        var accessibleOrganizationIds = await GetAccessibleOrganizationIdsAsync(client, cancellationToken);

        if (accessibleOrganizationIds is { Count: 0 } && organizationIds.Length > 0)
        {
            warnings.Add("Eventbrite token is valid, but /users/me/organizations/ returned no organizations. Eventbrite public search is not available through this collector.");
        }

        foreach (var organizationId in organizationIds)
        {
            if (accessibleOrganizationIds is { Count: > 0 } && !accessibleOrganizationIds.Contains(organizationId))
            {
                warnings.Add($"Eventbrite organization {organizationId} is not listed for this token. Check the organization ID or use a token from an account that manages it.");
                continue;
            }

            var uri = BuildUri(organizationId);
            using var response = await client.GetAsync(uri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                warnings.Add(await BuildWarningAsync(response, organizationId, cancellationToken));
                continue;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var parsed = document.RootElement.Array("events")
                .Select(item => ParseEvent(item, window))
                .OfType<EventItem>();

            events.AddRange(parsed);
        }

        foreach (var venueId in venueIds)
        {
            var uri = BuildVenueEventsUri(venueId);
            using var response = await client.GetAsync(uri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                warnings.Add(await BuildVenueWarningAsync(response, venueId, cancellationToken));
                continue;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var parsed = document.RootElement.Array("events")
                .Select(item => ParseEvent(item, window))
                .OfType<EventItem>();

            events.AddRange(parsed);
        }

        return new EventSourceResult(Name, events, warnings);
    }

    private static async Task<HashSet<string>?> GetAccessibleOrganizationIdsAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("https://www.eventbriteapi.com/v3/users/me/organizations/", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement
            .Array("organizations")
            .Select(organization => organization.String("id"))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    private static async Task<string> BuildWarningAsync(HttpResponseMessage response, string organizationId, CancellationToken cancellationToken)
    {
        var detail = await ReadEventbriteErrorAsync(response, cancellationToken);
        return response.StatusCode switch
        {
            System.Net.HttpStatusCode.NotFound => $"Eventbrite organization {organizationId} was not found. Use an ID from /users/me/organizations/; a user ID often will not work here.",
            System.Net.HttpStatusCode.Forbidden => $"Eventbrite organization {organizationId} is not accessible with this token.",
            System.Net.HttpStatusCode.Unauthorized => "Eventbrite rejected the private token. Generate a fresh private token and set Collector:EventbriteToken.",
            _ => $"Eventbrite organization {organizationId} returned {(int)response.StatusCode}{detail}."
        };
    }

    private static async Task<string> BuildVenueWarningAsync(HttpResponseMessage response, string venueId, CancellationToken cancellationToken)
    {
        var detail = await ReadEventbriteErrorAsync(response, cancellationToken);
        return response.StatusCode switch
        {
            System.Net.HttpStatusCode.NotFound => $"Eventbrite venue {venueId} was not found. Venue IDs must come from Eventbrite event/venue records; ordinary venue names will not work.",
            System.Net.HttpStatusCode.Forbidden => $"Eventbrite venue {venueId} is not accessible with this token.",
            System.Net.HttpStatusCode.Unauthorized => "Eventbrite rejected the private token. Generate a fresh private token and set Collector:EventbriteToken.",
            _ => $"Eventbrite venue {venueId} returned {(int)response.StatusCode}{detail}."
        };
    }

    private static async Task<string> ReadEventbriteErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var description = document.RootElement.String("error_description");
            return string.IsNullOrWhiteSpace(description) ? string.Empty : $": {description}";
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static Uri BuildUri(string organizationId)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["status"] = "live";
        query["time_filter"] = "current_future";
        query["order_by"] = "start_asc";
        query["expand"] = "venue,ticket_availability";

        return new UriBuilder($"https://www.eventbriteapi.com/v3/organizations/{organizationId}/events/")
        {
            Query = query.ToString()
        }.Uri;
    }

    private static Uri BuildVenueEventsUri(string venueId)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["status"] = "live";
        query["order_by"] = "start_asc";
        query["only_public"] = "true";
        query["expand"] = "venue,ticket_availability";

        return new UriBuilder($"https://www.eventbriteapi.com/v3/venues/{venueId}/events/")
        {
            Query = query.ToString()
        }.Uri;
    }

    private EventItem? ParseEvent(JsonElement item, EventWindow window)
    {
        var start = DublinDateTime.ParseIsoOrLocal(item.Property("start")?.String("utc"));
        if (start is null || !DublinDateTime.IsInside(window, start.Value))
        {
            return null;
        }

        var ticketAvailability = item.Property("ticket_availability");

        return new EventItem
        {
            Source = Name,
            SourceEventId = item.String("id") ?? Guid.NewGuid().ToString("n"),
            Title = item.Property("name")?.String("text") ?? "Untitled Eventbrite event",
            Venue = item.Property("venue")?.String("name"),
            StartsAt = start.Value,
            EndsAt = DublinDateTime.ParseIsoOrLocal(item.Property("end")?.String("utc")),
            Url = item.String("url"),
            ImageUrl = item.Property("logo")?.String("url"),
            Category = item.String("format_id"),
            Currency = ticketAvailability?.String("currency"),
            Status = item.String("status")
        };
    }
}
