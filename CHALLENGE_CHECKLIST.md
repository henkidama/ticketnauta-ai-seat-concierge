# WebMCP Challenge Compliance Checklist

Last reviewed against the official rules and deadline-extension notice on September 3, 2026.

Official sources:

- [OpenAI WebMCP Challenge](https://openai.com/webmcp-challenge/)
- [Devpost challenge page](https://webmcp.devpost.com/)
- [Official rules](https://webmcp.devpost.com/rules)
- [Official 12-hour deadline extension](https://webmcp.devpost.com/updates/46227-deadline-extension-12-more-hours)

## Critical deadline

Devpost extended the submission deadline by 12 hours because of a service outage. The updated deadline displayed by the challenge and its official notice is **September 4, 2026 at 1:00 AM Pacific Time**. Treat that as a hard freeze: the submitted repository, video, description, and live experience must match. The working project must remain available to judges, free of charge and without restrictions, through the end of judging.

## Current status

| Requirement | Status | Evidence or required action |
|---|---|---|
| WebMCP-powered web app | Ready | Nine tools are registered from the main page with `document.modelContext.registerTool()` |
| Working, coherent product experience | Verified publicly | Judge Mode, explainable recommendations, live conflict recovery, interactive map, cart, expiring holds, release, and simulated checkout work as one flow at the public HTTPS URL |
| Main document, no iframe | Ready | `index.html` loads the WebMCP module directly and contains no iframe |
| Narrow JSON Schemas | Ready | All tools declare input schemas; object schemas reject undeclared properties |
| Read-only and state-changing actions separated | Ready | Search/details/ranking/cart are read-only; highlight is visual; select/hold/release/checkout are distinct consequential actions |
| Explicit confirmation for consequential actions | Ready | `hold_seats` accepts only `HOLD_SELECTED_SEATS`; `proceed_to_checkout` accepts only `SIMULATE_CHECKOUT` |
| Live availability recovery | Verified publicly | The public smoke test confirms a 90-second competing hold, typed `seat_conflict`, refreshed alternatives, a replacement hold, and release without automatic substitution |
| Explainable real-world constraints | Verified publicly | The public Judge Mode displays fit evidence for center offset, zone, aisle, accessible companion, budget, split layout, and orphan-seat impact |
| English submission experience | Ready | Page, messages, tool descriptions, README, checklist, and submission draft are in English |
| Fictional and isolated data | Ready | Only the `webmcp_demo` PostgreSQL schema is used; no real inventory, customer, payment, or ticket APIs |
| Source, assets, and run instructions | Ready | Public repository includes complete source, Docker setup, Windows/WSL/Linux steps, reset scripts, CI, and health checks |
| Open-source license | Ready | Public GitHub repository includes the MIT `LICENSE`; verify the About section detects it |
| New project or dated meaningful extension | Ready | Public dated commit history documents the WebMCP implementation and final resilience extension |
| Public source repository URL | Ready | `https://github.com/henkidama/ticketnauta-ai-seat-concierge` |
| Working public live URL | Verified publicly | `https://webmcp.ticketnauta.com/` serves the final commit, passes readiness checks, registers nine WebMCP tools, and passes `smoke:rescue` |
| Public YouTube demo under three minutes | **Replacement required** | Re-record the new Live Seat Rescue flow with English audio, publish it publicly, and replace the prior URL in Devpost |
| Devpost text description | Draft ready | Copy and adapt `SUBMISSION.md`; it directly answers all four required description points |
| Devpost registration and final submission | **User action required** | Join the challenge, complete every required field, and submit before the deadline |
| Availability through September 21 | **User action required** | Keep the server, database, internet connection, and Cloudflare Tunnel healthy through 5:00 PM PT |
| Participant/team eligibility | Owner confirmation required | Confirm age, supported country/territory, authorized representative if a team, and absence of disqualifying conflicts |
| Ownership and brand/media rights | Owner confirmation required | Confirm authorization to use the Ticketnauta name/visual identity and use only owned or licensed audio and visuals in the video |

## Judge-path verification

Complete this after the public hostname is live:

1. Open the HTTPS URL in the ChatGPT in-app browser.
2. Verify the header reports `9 WebMCP tools active`.
3. Click **Load scenario** in Judge Mode and copy the English agent prompt.
4. Let the agent rank and highlight an option, but ask before selecting or holding.
5. Click **Simulate competing buyer**, approve the stale option, and verify `seat_conflict` appears visibly.
6. Confirm the agent refreshes, explains a valid replacement, and requests renewed approval.
7. Approve the replacement and verify `HOLD_SELECTED_SEATS` creates the countdown.
8. Read the cart summary, release the hold, and verify availability returns.
9. Confirm trace rows show `WEBMCP`, safety classification, input/result summary, and duration.
10. Create another hold and complete only the simulated checkout with `SIMULATE_CHECKOUT`.
11. Repeat the essential flow in Chrome 149+ with `chrome://flags/#enable-webmcp-testing` enabled.
12. Run `npm run smoke:rescue -- https://webmcp.ticketnauta.com` and test from an external clean session.

## Submission package check

Before pressing Submit, ensure Devpost contains:

- the public HTTPS application URL;
- the public repository URL with the MIT license visible;
- the public YouTube URL for a video under three minutes with audio;
- the English description from `SUBMISSION.md`;
- testing instructions that explain the compatible browser and a suggested prompt;
- screenshots that contain no passwords, `.env` values, tunnel tokens, database credentials, or private network details.

## Judging criteria alignment

The official criteria are equally weighted:

- **WebMCP Leverage:** nine focused tools cover discovery, explainable optimization, visible page updates, safe actions, typed conflict feedback, and agent-led recovery.
- **Execution:** the demo is a complete PostgreSQL-backed journey with a repeatable failure path, session reset, Docker, health checks, unit tests, and end-to-end CI.
- **Potential Impact:** the agent handles real group, budget, aisle, accessibility, inventory-fragmentation, and concurrency constraints while the human retains consent.
- **Creativity and Ambition:** Live Seat Rescue turns a static recommendation into resilient human-agent collaboration on the same changing seat map.
