# Ticketnauta AI Seat Concierge — Submission Draft

Replace the placeholders before submitting:

- Live project: `[LIVE_URL]`
- Public repository: `[REPOSITORY_URL]`
- Public YouTube demo: `[YOUTUBE_URL]`

## Tagline

A transparent seat-finding concierge where people and browser agents search, compare, visualize, hold, and simulate checkout together through WebMCP.

## Project description

Buying seats is a deceptively complex decision. A person may need four seats together, a total budget rather than a per-seat limit, a preferred section, accessible seating, or the best compromise between distance and price. Traditional ticketing interfaces make the user repeat filters and visually scan a large map while availability changes.

Ticketnauta AI Seat Concierge turns that workflow into a collaboration between the human, a browser agent, and the visible web page. The agent can search fictional events, inspect zones and availability, rank contiguous seat blocks, highlight alternatives on the live map, select an option, create or release a temporary hold, read the cart, and complete an explicitly confirmed simulated checkout. The person remains in control because every recommendation and action is reflected immediately in the interface.

### Why this is a strong fit for WebMCP

Seat selection combines structured queries, live page context, visual comparison, and consequential actions. WebMCP lets the website expose narrow capabilities with typed inputs instead of asking an agent to infer the interface from pixels or manipulate generic UI controls. The tool boundary is especially valuable here: discovery and ranking stay read-only, while selection, holds, release, and checkout remain separate and intentional.

### How it creates a better user experience

The user can express a goal such as “Find four contiguous seats under MX$7,000, closest to the stage, and hold the best option.” The system ranks valid alternatives, highlights the best block on the seat map, explains the tradeoff, and keeps a visible activity trail. Users can still inspect every seat and use the manual controls. The experience is faster than repeatedly changing filters, but it never hides what the agent did.

### What people and agents can do together

The agent handles the combinatorial work: searching hundreds of seats, enforcing contiguity, applying a total budget, comparing zones, and tracking hold state. The person contributes intent and judgment: which tradeoff feels right, whether an accessible option is suitable, whether to hold, and whether to confirm the final simulation. The shared map and conversation timeline let both operate on the same state without surrendering human oversight.

### How WebMCP was implemented

The main page registers nine tools with `document.modelContext.registerTool()`. Every tool has a narrow JSON Schema and rejects undeclared object properties. Read-only tools cover event search, details, recommendations, and cart state. Separate page actions handle highlighting, selection, temporary holds, release, and checkout. Tool calls invoke the same application functions as the visible interface, so the page always stays synchronized. WebMCP calls emit a local lifecycle event that the conversation timeline labels as `WEBMCP`; built-in guided requests are deliberately labeled `GUIDED` to avoid implying that a local parser is an external agent.

The backend is a .NET 10 ASP.NET Core Minimal API with PostgreSQL. An idempotent bootstrap creates only the isolated `webmcp_demo` schema and seeds fictional events and seats. Holds use transactional availability checks, expire automatically, and can be released. Checkout is a simulation with explicit confirmation and no payment integration. Docker Compose provides application, optional local database, health checks, and an optional Cloudflare Tunnel profile.

## Suggested testing instructions

1. Open `[LIVE_URL]` in the ChatGPT in-app browser, which supports WebMCP, or use Chrome 149+ with `chrome://flags/#enable-webmcp-testing` enabled.
2. Confirm the header reports nine active WebMCP tools.
3. Ask: “Find four contiguous seats under MX$7,000 and hold the best option.”
4. Observe the ranked alternatives, highlighted seats, updated cart, hold countdown, and `WEBMCP` activity entries.
5. Ask for the cart summary, then release the seats.
6. To test checkout, create a new hold and explicitly confirm the simulated checkout. No real charge or ticket is produced.

All events, venues, seats, prices, availability, holds, references, and checkout results are fictional.

## Three-minute demonstration plan

Keep the final video under 2:50 to leave margin. Use spoken English and no unlicensed music.

| Time | Visual | Narration focus |
|---|---|---|
| 0:00–0:18 | Event list, map, and isolated-demo badge | The real problem: multi-constraint seat search is slow and hard to explain |
| 0:18–0:38 | WebMCP status and conversation panel | Nine typed tools in the main page; explain `GUIDED` versus `WEBMCP` labels |
| 0:38–1:15 | Agent asks for four seats under MX$7,000 | Search and ranking; show contiguous alternatives and visual highlight |
| 1:15–1:48 | Select and hold the best option | Separate actions, cart update, PostgreSQL-backed countdown, no real inventory |
| 1:48–2:08 | Cart summary and release | Agent reads state, then safely reverses the hold |
| 2:08–2:30 | New hold and explicit simulated checkout | Consequential step stays separate; no payment or ticket is created |
| 2:30–2:48 | Architecture or repository view | .NET 10, PostgreSQL isolation, Docker, open source, and why WebMCP improves trust |

## Suggested closing line

“Ticketnauta AI Seat Concierge shows the future of the open web as a shared workspace: the agent handles complexity, the person keeps context and control, and the website exposes safe, purpose-built capabilities through WebMCP.”

## Short project summary

Ticketnauta AI Seat Concierge is an isolated WebMCP demo for collaborative seat discovery. A browser agent can search fictional events, rank contiguous seats by budget and preference, highlight options on a live map, manage temporary holds, and complete an explicitly confirmed simulated checkout. A transparent activity timeline keeps human-guided and WebMCP actions visible and distinct. Built with .NET 10, PostgreSQL, dependency-free web UI, Docker Compose, and an optional Cloudflare Tunnel.
