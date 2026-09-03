# Ticketnauta AI Seat Concierge — Submission Draft

Final links:

- Live project: `https://webmcp.ticketnauta.com/`
- Public repository: `https://github.com/henkidama/ticketnauta-ai-seat-concierge`
- Public YouTube demo: `[YOUTUBE_URL]`

## Tagline

An explainable seat concierge where people and browser agents recover from changing ticket availability together through WebMCP.

## Project description

Buying seats is a deceptively complex, time-sensitive decision. A person may need four seats together, a total budget rather than a per-seat limit, aisle access, an accessible position with a companion, or the best compromise between distance and price. Traditional ticketing interfaces make the user repeatedly adjust filters and visually scan a large map. Even after finding a suitable block, another buyer can take one seat before the group confirms it.

Ticketnauta AI Seat Concierge turns that workflow into a collaboration between the human, a browser agent, and the visible web page. The agent can search fictional events, inspect zones and live availability, rank explainable seat options, highlight alternatives, select an option, create or release a temporary hold, read the cart, and complete an explicitly confirmed simulated checkout. If availability changes, the backend returns the unavailable seat IDs and a recovery directive. The agent can refresh, explain the next-best option, and ask for renewed approval instead of silently substituting seats.

### Why this is a strong fit for WebMCP

Seat selection combines structured constraints, volatile inventory, visual comparison, and consequential actions. WebMCP lets the website expose narrow capabilities with typed inputs instead of asking an agent to infer the interface from pixels or manipulate generic UI controls. The tool boundary is especially valuable here: discovery and ranking stay read-only, map highlighting remains visual, and selection, holds, release, and checkout remain separate and intentional. The same boundary gives the agent a machine-readable recovery path when a recommendation becomes stale.

### How it creates a better user experience

The user can express a goal such as “Find four Gold seats under MXN 8,000, centered and aisle-friendly.” The system ranks valid alternatives with a 0–100 fit score and evidence for center distance, zone match, aisle access, accessibility, budget headroom, and orphan-seat impact. Users can still inspect every seat and use the manual controls. A built-in Live Seat Rescue scenario then lets a second buyer invalidate the recommendation so judges can see conflict detection, recovery, and renewed consent end to end.

### What people and agents can do together

The agent handles the combinatorial work: searching hundreds of seats, enforcing contiguity or a deliberate 2 + 2 fallback, applying a total budget, protecting accessible companion seating, avoiding inventory fragmentation, comparing alternatives, and recovering from concurrent changes. The person contributes intent and judgment: which tradeoff feels right and whether a replacement may be selected or held. The shared map and tool trace let both operate on the same state without surrendering human oversight.

### How WebMCP was implemented

The main page registers nine tools with `document.modelContext.registerTool()`. Every tool has a narrow JSON Schema and rejects undeclared object properties. Read-only tools cover event search, details, explainable recommendations, and cart state. Separate page actions handle highlighting, selection, temporary holds, release, and checkout. Holds and checkout require explicit confirmation values. Tool calls invoke the same application functions as the visible interface, so the page always stays synchronized. Each WebMCP lifecycle event appears in the timeline with its source, safety classification, input/result summary, and duration; built-in guided requests remain labeled `GUIDED` so they cannot be mistaken for an external agent.

The backend is a .NET 10 ASP.NET Core Minimal API with PostgreSQL. An idempotent bootstrap creates only the isolated `webmcp_demo` schema and seeds fictional events and seats. Event-level transactional locks protect holds from races. A session-scoped endpoint simulates a 90-second competing hold, while stale actions return a typed `seat_conflict` response. Checkout is a simulation with explicit confirmation and no payment integration. Docker Compose provides the application, optional local database, health checks, and optional Cloudflare Tunnel profile. GitHub Actions verifies the build, tests, nine-tool contract, container stack, and complete Live Seat Rescue flow.

## Suggested testing instructions

1. Open `https://webmcp.ticketnauta.com/` in the ChatGPT in-app browser, which supports WebMCP, or use Chrome 149+ with `chrome://flags/#enable-webmcp-testing` enabled.
2. Confirm the header reports nine active WebMCP tools.
3. In Judge Mode, click **Load scenario**, then copy the provided browser-agent prompt.
4. Ask the agent to find and highlight the best option, but not to select or hold it without approval.
5. Click **Simulate competing buyer**, then approve the stale option. Observe the typed `seat_conflict` and the invalidated recommendation on the map.
6. The agent should refresh alternatives, explain the replacement, and ask for renewed approval. Approve it, then create the temporary hold with `HOLD_SELECTED_SEATS`.
7. Observe the `WEBMCP` traces, replacement highlight, updated cart, and countdown. Ask for the cart summary and release the hold.
8. To test checkout, create a new hold and explicitly use `SIMULATE_CHECKOUT`. No real charge or ticket is produced.

All events, venues, seats, prices, availability, holds, references, and checkout results are fictional.

## Three-minute demonstration plan

Keep the final video under 2:50 to leave margin. Use spoken English and no unlicensed music.

| Time | Visual | Narration focus |
|---|---|---|
| 0:00–0:18 | Event list, map, and isolated-demo badge | The real problem: a valid group option can disappear while the user decides |
| 0:18–0:36 | WebMCP status and tool trace | Nine typed tools; read-only, visual, and consequential boundaries |
| 0:36–1:05 | Load Judge Mode and send its agent prompt | Multi-constraint ranking, fit evidence, tradeoffs, and map highlight |
| 1:05–1:30 | Click **Simulate competing buyer**, then approve the stale option | A real PostgreSQL-backed availability change produces structured `seat_conflict` |
| 1:30–2:02 | Agent refreshes, explains a replacement, and asks again | Human-agent recovery and renewed consent instead of a hidden substitution |
| 2:02–2:25 | Approve, hold, inspect countdown, and release | Safe consequential action, reversible state, no real inventory |
| 2:25–2:48 | Repository/architecture and automated test result | .NET 10, PostgreSQL isolation, Docker, CI, open source, and measurable execution |

## Suggested closing line

“Ticketnauta AI Seat Concierge shows the future of the open web as a resilient shared workspace: the agent handles live complexity, the person keeps context and consent, and the website exposes safe recovery through WebMCP.”

## Short project summary

Ticketnauta AI Seat Concierge is an isolated WebMCP demo for resilient group-seat discovery. A browser agent ranks fictional seats with explainable real-world constraints, shares alternatives on a live map, detects when another buyer invalidates a recommendation, safely recovers with renewed approval, manages temporary holds, and completes an explicitly confirmed simulated checkout. Built with .NET 10, PostgreSQL, dependency-free web UI, Docker Compose, Cloudflare Tunnel, and an end-to-end verified nine-tool WebMCP surface.
