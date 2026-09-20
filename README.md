# Dublin Events Collector

A .NET 9 web application that collects upcoming events from Dublin ticketing services and venue listings. The default event window is 30 days.

## Deploy with GitHub Actions

The included workflow deploys a Docker container to a Proxmox-hosted Linux VM or LXC through Cloudflare Access SSH.

Before deploying, confirm the destination in `.github/workflows/proxmox-deploy.yml`:

```yaml
DEPLOY_HOST: deploy.example.com
DEPLOY_USER: deploy-user
DEPLOY_DIR: /opt/dublin-events-collector
```

Add these repository secrets under **Settings > Secrets and variables > Actions**:

| Secret | Purpose |
| --- | --- |
| `SSH_PRIVATE_KEY` | Private key used by GitHub Actions to connect to the deployment host |
| `CF_CLIENT_ID` | Cloudflare Access service-token client ID |
| `CF_CLIENT_SECRET` | Cloudflare Access service-token secret |
| `PROD_ENV` | Complete production environment file |

The matching public SSH key must be present in the deployment user's `~/.ssh/authorized_keys` file.

Set `PROD_ENV` to:

```env
APP_PORT=8123

COLLECTOR__TICKETMASTERAPIKEY=your-ticketmaster-consumer-key
COLLECTOR__EVENTBRITETOKEN=
COLLECTOR__EVENTBRITEORGANIZATIONIDS__0=
COLLECTOR__EVENTBRITEVENUEIDS__0=
COLLECTOR__ENABLEEVENSOS=true
COLLECTOR__ENABLETICKTS=false

NEW_RELIC_ENABLED=1
NEW_RELIC_LICENSE_KEY=your-new-relic-license-key
NEW_RELIC_APP_NAME=Dublin Events Collector
NEW_RELIC_DISTRIBUTED_TRACING_ENABLED=true
NEW_RELIC_LOG_CONSOLE=1
```

Eventbrite and New Relic values may be left blank when those services are not required. Set `NEW_RELIC_ENABLED=0` when no New Relic license key is configured.

Push to `main` or run **Proxmox Deploy via Cloudflare** manually from the repository's Actions page. The deployed site is available on the configured host port, for example:

```text
http://your-proxmox-host:8123/
```

If a reverse proxy or Cloudflare Tunnel is used, point it to that host and port.

## Deploy with Docker Compose

On any Docker host:

```bash
cp .env.example .env
# Add the required keys to .env, then run:
docker compose up -d --build
```

Useful checks:

```bash
docker compose ps
docker compose logs -f dublin-events
```

## Application Endpoints

| Method | Endpoint | Description |
| --- | --- | --- |
| `GET` | `/` | Web interface |
| `GET` | `/events` | Collected events and per-source results |
| `GET` | `/api/events` | Alias of `/events` |
| `GET` | `/sources` | Configured source names and status information |
| `GET` | `/api/sources` | Alias of `/sources` |

The event endpoints accept:

| Parameter | Format | Description |
| --- | --- | --- |
| `from` | `YYYY-MM-DD` | First date to include; defaults to today |
| `to` | `YYYY-MM-DD` | Last date to include |
| `days` | `1` to `180` | Window length when `to` is omitted; defaults to `30` |

Example:

```text
GET /api/events?from=2026-09-20&to=2026-10-20
```

## External Endpoints Accessed

The deployed service makes outbound HTTPS requests to the following endpoints.

### Ticketing APIs and feeds

- `https://app.ticketmaster.com/discovery/v2/events.json`
- `https://www.eventbriteapi.com/v3/users/me/organizations/`
- `https://www.eventbriteapi.com/v3/organizations/{organizationId}/events/`
- `https://www.eventbriteapi.com/v3/venues/{venueId}/events/`
- `https://www.evensos.com/events/dublin.json`
- `https://tickts.ie/feed/whats-on/dublin.json` when Tickts is enabled
- `https://fringefest.ticketsolve.com/shows.xml`
- `https://smockalley.ticketsolve.com/shows.xml`
- `https://draiocht.ticketsolve.com/shows.xml`
- `https://civictheatre.ticketsolve.com/shows.xml`
- `https://milltheatre.ticketsolve.com/shows.xml`
- `https://axisballymun.ticketsolve.com/shows.xml`
- `https://tickets.gatetheatre.ie/thegatedublin/api/v3/events`
- Gate Theatre event instance and availability endpoints under the same API

### Venue listings

- `https://www.nch.ie/all-events-listing/`
- `https://orchestra.rte.ie/whats-on/` and linked event detail pages
- `https://gatetheatre.ie/whats-on/`
- `https://www.abbeytheatre.ie/whats-on/`
- `https://www.gaietytheatre.ie/events/`
- `https://www.paviliontheatre.ie/events/`
- `https://www.vicarstreet.com/all-shows-at-vicar-street.html`
- `https://buttonfactory.ie/shows` and the linked calendar feeds

Keep API keys in `.env` or GitHub Actions secrets. Do not commit production credentials.
