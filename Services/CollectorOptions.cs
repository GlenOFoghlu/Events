namespace DublinEventsCollector.Services;

public sealed class CollectorOptions
{
    public string? TicketmasterApiKey { get; set; }
    public string? EventbriteToken { get; set; }
    public string[] EventbriteOrganizationIds { get; set; } = [];
    public string[] EventbriteVenueIds { get; set; } = [];
    public bool EnableEvensos { get; set; } = true;
    public string EvensosDublinJsonUrl { get; set; } = "https://www.evensos.com/events/dublin.json";
    public bool EnableTickts { get; set; }
    public string TicktsDublinJsonUrl { get; set; } = "https://tickts.ie/feed/whats-on/dublin.json";
}
