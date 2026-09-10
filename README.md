# Dublin Events Collector

A small .NET 9 API for collecting upcoming Dublin events from API-backed providers and selected venue pages.

The default event window is the next 30 days. You can override it with `from`, `to`, or `days` query parameters.

## Run it

```bash
cd /Users/gofoghlu/Development/dublin-events-collector
dotnet run
```

Then open:

```text
http://localhost:5281/
```

The UI calls the API for the selected date range. You can still call the JSON endpoint directly:

```text
http://localhost:5281/events?from=2026-09-09&to=2026-10-09
```

## Run with Docker

The app is container-ready for a Proxmox VM or LXC that has Docker installed.

Create a local environment file:

```bash
cp .env.example .env
```

Edit `.env` and add your real API keys:

```bash
COLLECTOR__TICKETMASTERAPIKEY=your-ticketmaster-key
COLLECTOR__EVENTBRITETOKEN=your-eventbrite-token
```

Build and start the container:

```bash
docker compose up -d --build
```

Open:

```text
http://your-proxmox-vm-ip:8080/
```

Useful Docker commands:

```bash
docker compose logs -f
docker compose restart
docker compose pull
docker compose down
```

For production, put a reverse proxy such as Nginx Proxy Manager, Caddy, Traefik, or an existing Proxmox-hosted proxy in front of port `8080` and terminate HTTPS there.

## Deploy to Proxmox with GitHub Actions

This repo includes `.github/workflows/proxmox-deploy.yml`, which deploys through Cloudflare Access SSH to:

```text
/root/dublin-events-collector
```

Configure these GitHub repository secrets:

```text
SSH_PRIVATE_KEY
CF_CLIENT_ID
CF_CLIENT_SECRET
PROD_ENV
```

`PROD_ENV` should contain the production `.env` content, for example:

```bash
APP_PORT=8080
COLLECTOR__TICKETMASTERAPIKEY=your-ticketmaster-key
COLLECTOR__EVENTBRITETOKEN=your-eventbrite-token
COLLECTOR__EVENTBRITEORGANIZATIONIDS__0=
COLLECTOR__EVENTBRITEVENUEIDS__0=
COLLECTOR__ENABLEEVENSOS=true
COLLECTOR__ENABLETICKTS=false
```

The workflow uploads a release bundle, writes `.env`, swaps the deployment directory atomically, runs:

```bash
docker compose --env-file .env up -d --build
```

and prunes old Docker images.

## Configure sources

Use environment variables for API keys:

```bash
export COLLECTOR__TICKETMASTERAPIKEY="your-ticketmaster-key"
export COLLECTOR__EVENTBRITETOKEN="your-eventbrite-token"
export COLLECTOR__EVENTBRITEORGANIZATIONIDS__0="known-organization-id"
export COLLECTOR__EVENTBRITEVENUEIDS__0="known-venue-id"
```

You can also set the same values under the `Collector` section in `appsettings.Development.json` for local testing. Avoid committing real keys.

## Current sources

- `ticketmaster`: uses the Ticketmaster Discovery API for Dublin, Ireland.
- `3olympia`: uses the Ticketmaster Discovery API filtered to 3Olympia Theatre.
- `evensos`: reads the Evensos Dublin JSON-LD feed.
- `fringefest`: reads the Dublin Fringe Festival Ticketsolve XML feed.
- `tickts`: optional JSON/iCal feed source. Disabled by default until the Dublin feed has events.
- `eventbrite`: pulls events from known Eventbrite organization IDs and/or known Eventbrite venue IDs. Broad public Eventbrite search is not available through their current official public API.
- `national-concert-hall`: reads the public National Concert Hall listing.
- `rte-orchestra`: reads the public RTÉ Orchestra What's On listing and marks events outside Dublin.
- `smock-alley`: reads the public Smock Alley Ticketsolve XML feed.
- `button-factory`: reads the per-event ICS links from the Button Factory shows page.
- `dublin-ie`, `entertainment-ie`, `whats-on-dublin`, `mcd`: candidate listing sources shown in the UI so they can be added deliberately after checking terms and page structure.
- `gaiety-theatre`, `abbey-theatre`, `pavilion-theatre`, `vicar-street`: public venue listing scrapers.
- `gate-theatre`, `whelans`, `the-academy`, `the-grand-social`, `workmans-club`: candidate venue sources for Dublin theatre and gig listings.
- Bord Gáis Energy Theatre is covered through Ticketmaster rather than a separate source.

## Add another venue

Create a new class in `Collectors/` that implements `IEventSource`, then register it in `Program.cs`:

```csharp
builder.Services.AddSingleton<IEventSource, YourVenueEventSource>();
```

Each source should return normalized `EventItem` objects. The central collector dedupes events by title, venue, and start date.

## Eventbrite venue IDs

Eventbrite supports `GET /venues/{venue_id}/events/`, so this app can pull from a maintained list of venue IDs:

```bash
export COLLECTOR__EVENTBRITEVENUEIDS__0="123456789"
export COLLECTOR__EVENTBRITEVENUEIDS__1="987654321"
```

Eventbrite does not provide a broad public Dublin venue search API. Practical ways to find venue IDs are:

- Retrieve a known Eventbrite event and inspect its expanded venue data.
- Pull events from an Eventbrite organization you manage and collect each event's `venue_id`.
- Maintain a manual seed list for Dublin venues that actually publish via Eventbrite.

## Production notes

- Cache venue pages and API responses; daily refresh is usually enough for a month-ahead Dublin listings product.
- Store raw source payloads if you later add a database, because venue parsers will occasionally need reprocessing.
- Add source-specific tests before relying on scraped venues for production listings.
- Check each source's terms before publishing aggregated listings.
# Events
