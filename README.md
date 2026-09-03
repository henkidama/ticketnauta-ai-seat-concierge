# Ticketnauta AI Seat Concierge

Ticketnauta AI Seat Concierge is an isolated WebMCP challenge demo where people and browser agents collaborate to discover an event, compare contiguous seat blocks, understand tradeoffs on a visible map, place a temporary hold, and finish a simulated checkout.

**[Try the live HTTPS demo](https://webmcp.ticketnauta.com/)** · **[MIT License](LICENSE)**

> **Isolation guarantee:** the application never queries real Ticketnauta inventory, customers, payments, sessions, or APIs. It creates and uses only the `webmcp_demo` PostgreSQL schema. Every included event, venue, price, seat, hold, and checkout is fictional.

The interface works in a standard browser. To let an agent discover and call the nine browser-native tools, open the live URL in a WebMCP-compatible client such as the ChatGPT in-app browser or Chrome 149+ with WebMCP testing enabled.

## Demo experience

- A responsive interactive SVG seat map with zones, prices, availability, keyboard controls, and accessible-seat markers.
- Contiguous-seat ranking by guest count, total budget, preferred zone, and priority.
- Alternatives that remain visible while the best option is highlighted on the map.
- A conversational concierge that understands common English seat requests and visibly traces every guided tool action.
- Browser-agent WebMCP calls appear in the same conversation timeline with a separate `WEBMCP` source label.
- Selection, temporary holds with expiration, release, cart summary, and explicitly confirmed simulated checkout.
- No external language-model API is required by the page. The built-in conversation is a deterministic guided interface; a compatible browser agent uses the nine registered WebMCP tools directly.

Try this request with a compatible browser agent:

> Find four contiguous seats under MX$7,000, prioritize centered seats, show me the alternatives, and ask before holding the best option.

## Architecture

```text
Human visitor                    Browser agent
      |                               |
      | guided conversation           | WebMCP tools
      +---------------+---------------+
                      v
              Main page (no iframe)
          conversation + SVG seat map
                      |
                      | /api
                      v
               ASP.NET Core 10
          ranking + holds + checkout
                      |
                      | Npgsql
                      v
                 PostgreSQL
             webmcp_demo schema only
```

The bootstrap and every query use fully qualified `webmcp_demo.*` table names. The application contains no queries against `public`, production tables, or payment services.

## WebMCP tools

The main document registers these tools with `document.modelContext.registerTool()`:

| Tool | Behavior | Purpose |
|---|---|---|
| `search_events` | Read-only | Search fictional events |
| `get_event_details` | Read-only | Read zones, prices, and availability |
| `find_seat_options` | Read-only | Rank contiguous seat blocks |
| `highlight_seats` | Page action | Highlight seats on the visible map |
| `select_seat_option` | Page action | Replace the demo cart selection |
| `hold_seats` | Consequential demo action | Create a temporary hold |
| `release_seats` | Consequential demo action | Release the active hold |
| `get_cart_summary` | Read-only | Read the selection, total, and expiration |
| `proceed_to_checkout` | Consequential demo action | Finish the simulation after explicit confirmation |

Every tool has a narrow JSON Schema. Object schemas reject undeclared fields with `additionalProperties: false`. Read-only operations and state-changing operations remain separate, and checkout accepts only the explicit `SIMULATE_CHECKOUT` confirmation value.

## Included technology

- .NET 10 and ASP.NET Core Minimal API
- PostgreSQL with idempotent schema bootstrap and fictional seed data
- Npgsql data access with transactional event-level locking for holds
- Dependency-free HTML, CSS, JavaScript, and SVG frontend
- Multi-stage Dockerfile and Docker Compose health checks
- Optional Cloudflare Tunnel Compose profile
- Token-protected reset that deletes only runtime records in `webmcp_demo`

## Quick start with isolated Docker

Requirements: Docker Engine with Docker Compose. This path starts both the application and a dedicated PostgreSQL container; it does not need an existing database.

Clone this repository, enter its directory, and create a local `.env` from the safe template. Replace the placeholder password and reset token with local-only values before starting the containers.

### Windows / PowerShell

```powershell
Copy-Item .env.example .env
notepad .env
```

Use these local database settings in `.env`:

```dotenv
DB_HOST=db
DB_PORT=5432
DB_NAME=ticketnauta_webmcp_demo
DB_USER=webmcp_demo
DB_PASSWORD=choose_a_long_random_local_password
DB_SSL_MODE=Disable
DEMO_ADMIN_TOKEN=choose_a_different_long_random_local_token
```

Then start and verify the complete stack:

```powershell
docker compose --profile localdb up --build -d
docker compose ps
Invoke-RestMethod http://localhost:8085/health/ready
```

Open [http://localhost:8085](http://localhost:8085). The first start creates `webmcp_demo`, its tables, and the fictional seed data. Later starts are idempotent and preserve the current demo state.

### WSL / Linux

```bash
cp .env.example .env
chmod +x scripts/*.sh
$EDITOR .env
docker compose --profile localdb up --build -d
docker compose ps
curl --fail http://localhost:8085/health/ready
```

Use the same `DB_HOST=db` settings shown above. PostgreSQL is reachable only inside the Compose network unless you intentionally publish its optional local port.

## Use an existing demo PostgreSQL server

To use a separate PostgreSQL test server instead of the included `localdb` profile, set its local-only connection values in `.env`:

```dotenv
DB_HOST=your_postgres_host
DB_PORT=5432
DB_NAME=your_demo_database
DB_USER=your_demo_user
DB_PASSWORD=choose_a_long_random_password
DB_SSL_MODE=Prefer
```

Start only the application:

```powershell
docker compose up --build -d
```

The application still creates and queries only the `webmcp_demo` schema. It never reads or modifies unrelated schemas.

## Develop without an application container

Requirements: .NET 10 SDK, Node.js 22 or later for web contract checks, and a reachable PostgreSQL instance configured in `.env`.

Windows:

```powershell
.\scripts\run-local.ps1
```

WSL / Linux:

```bash
./scripts/run-local.sh
```

The API serves the page and JavaScript modules directly from `wwwroot`; there is no frontend build chain and no npm runtime dependency.

## Validation

```powershell
.\scripts\verify.ps1
```

Manual equivalent:

```powershell
dotnet restore .\Ticketnauta.WebMcp.slnx --configfile .\NuGet.Config
dotnet build .\Ticketnauta.WebMcp.slnx --no-restore -c Release
dotnet test .\Ticketnauta.WebMcp.slnx --no-restore -c Release
npm run check:web
docker compose config
docker compose build
```

`npm run check:web` verifies JavaScript syntax and the challenge-facing contract: nine tools, English main page, no iframe, visible conversation, separated sources, and explicit checkout confirmation.

Health endpoints:

- `/health/live`: the web process is responding.
- `/health/ready`: PostgreSQL is reachable and `webmcp_demo.events` exists.

## Reset the demo

The reset requires `DEMO_ADMIN_TOKEN` from `.env`, an exact confirmation string, and the private header. It preserves seed data and never touches another schema.

```powershell
.\scripts\reset-demo.ps1
```

```bash
./scripts/reset-demo.sh
```

## Cloudflare Tunnel

For a remotely managed Cloudflare Tunnel:

1. Create the tunnel in the Cloudflare Zero Trust dashboard.
2. Create a Public Hostname targeting the private service `http://app:8080`.
3. Store the tunnel token only as `CLOUDFLARE_TUNNEL_TOKEN` in `.env`.
4. Start the tunnel profile:

```powershell
docker compose --profile tunnel up --build -d
```

No NAT or router port forwarding is required. `cloudflared/config.example.yml` documents the alternative credentials-file setup; never commit the real credentials file or token.

Public WebMCP exposure requires HTTPS and a compatible client. Challenge judges may use the ChatGPT in-app browser or Chrome 149+ with WebMCP testing enabled.

## Environment variables

| Variable | Purpose |
|---|---|
| `DB_HOST`, `DB_PORT` | Demo PostgreSQL server, or `db` for the local profile |
| `DB_NAME`, `DB_USER`, `DB_PASSWORD` | Local credentials that must never be committed |
| `DB_SSL_MODE` | `Prefer`, `Require`, or `Disable`, depending on the environment |
| `APP_PORT` | Published application port; defaults to `8085` |
| `HOLD_MINUTES` | Hold duration, constrained by the API to 1–30 minutes |
| `DEMO_ADMIN_TOKEN` | Protects the demo reset endpoint |
| `CLOUDFLARE_TUNNEL_TOKEN` | Optional; used only by the `tunnel` profile |

## Security decisions

- `.env` is ignored by Git and excluded from the Docker build context.
- JavaScript, settings, Compose, seed data, and documentation contain no credentials.
- Hold operations use a PostgreSQL advisory lock and verify availability inside the transaction.
- Selection and hold creation refuse to replace an active hold; `release_seats` must be called explicitly.
- `proceed_to_checkout` never calls a payment gateway and requires explicit confirmation.
- The reset endpoint compares its token in constant time.
- The API sends a Content Security Policy, `Permissions-Policy: tools=(self)`, `frame-ancestors 'none'`, and baseline security headers.

## Submission material

- [Challenge compliance checklist](CHALLENGE_CHECKLIST.md)
- [English submission draft and video plan](SUBMISSION.md)

## License

[MIT](LICENSE)
