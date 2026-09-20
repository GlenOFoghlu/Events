using DublinEventsCollector.Collectors;
using DublinEventsCollector.Models;
using DublinEventsCollector.Services;

LoadLocalEnvironment();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHttpClient();
builder.Services.Configure<CollectorOptions>(builder.Configuration.GetSection("Collector"));

builder.Services.AddSingleton<EventNormalizer>();
builder.Services.AddSingleton<IEventSource, TicketmasterEventSource>();
builder.Services.AddSingleton<IEventSource, ThreeOlympiaTicketmasterEventSource>();
builder.Services.AddSingleton<IEventSource, EventbriteOrganizationEventSource>();
builder.Services.AddSingleton<IEventSource, EvensosEventSource>();
builder.Services.AddSingleton<IEventSource, DublinFringeFestivalEventSource>();
builder.Services.AddSingleton<IEventSource, TicktsEventSource>();
builder.Services.AddSingleton<IEventSource, NationalConcertHallEventSource>();
builder.Services.AddSingleton<IEventSource, RteOrchestraEventSource>();
builder.Services.AddSingleton<IEventSource, SmockAlleyEventSource>();
builder.Services.AddSingleton<IEventSource, DraiochtEventSource>();
builder.Services.AddSingleton<IEventSource, CivicTheatreEventSource>();
builder.Services.AddSingleton<IEventSource, MillTheatreEventSource>();
builder.Services.AddSingleton<IEventSource, AxisBallymunEventSource>();
builder.Services.AddSingleton<IEventSource, DublinIeCandidateSource>();
builder.Services.AddSingleton<IEventSource, EntertainmentIeCandidateSource>();
builder.Services.AddSingleton<IEventSource, WhatsOnDublinCandidateSource>();
builder.Services.AddSingleton<IEventSource, McdTicketmasterEventSource>();
builder.Services.AddSingleton<IEventSource, GateTheatreEventSource>();
builder.Services.AddSingleton<IEventSource, GaietyTheatreCandidateSource>();
builder.Services.AddSingleton<IEventSource, AbbeyTheatreCandidateSource>();
builder.Services.AddSingleton<IEventSource, PavilionTheatreCandidateSource>();
builder.Services.AddSingleton<IEventSource, VicarStreetCandidateSource>();
builder.Services.AddSingleton<IEventSource, WhelansCandidateSource>();
builder.Services.AddSingleton<IEventSource, TheAcademyCandidateSource>();
builder.Services.AddSingleton<IEventSource, ButtonFactoryEventSource>();
builder.Services.AddSingleton<IEventSource, TheGrandSocialCandidateSource>();
builder.Services.AddSingleton<IEventSource, WorkmansClubCandidateSource>();
builder.Services.AddSingleton<EventCollector>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseDefaultFiles();
app.UseStaticFiles();

IOrderedEnumerable<object> GetSources(IEnumerable<IEventSource> sources)
{
    return sources
        .Select(source => new
        {
            source.Name,
            source.Description,
            source.RequiresConfiguration
        })
        .OrderBy(source => source.Name);
}

async Task<IResult> GetEvents(
    EventCollector collector,
    DateOnly? from,
    DateOnly? to,
    int? days,
    CancellationToken cancellationToken)
{
    var window = EventWindow.Create(from, to, days ?? 30);
    var result = await collector.CollectAsync(window, cancellationToken);

    return Results.Ok(new
    {
        city = "Dublin",
        from = window.From,
        to = window.To,
        count = result.Events.Count,
        result.Events,
        result.SourceResults
    });
}

app.MapGet("/sources", GetSources);
app.MapGet("/api/sources", GetSources);
app.MapGet("/events", GetEvents);
app.MapGet("/api/events", GetEvents);

app.Run();

static void LoadLocalEnvironment()
{
    var path = Path.Combine(Directory.GetCurrentDirectory(), ".env");
    if (!File.Exists(path))
    {
        return;
    }

    foreach (var line in File.ReadLines(path))
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
        {
            continue;
        }

        var separator = trimmed.IndexOf('=');
        if (separator <= 0)
        {
            continue;
        }

        var key = trimmed[..separator].Trim();
        var value = trimmed[(separator + 1)..].Trim().Trim('"', '\'');
        if (Environment.GetEnvironmentVariable(key) is null)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
