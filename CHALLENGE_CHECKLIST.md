# WebMCP Challenge Compliance Checklist

Last reviewed against the official rules on September 2, 2026.

Official sources:

- [OpenAI WebMCP Challenge](https://openai.com/webmcp-challenge/)
- [Devpost challenge page](https://webmcp.devpost.com/)
- [Official rules](https://webmcp.devpost.com/rules)

## Critical deadline

The submission deadline is **September 3, 2026 at 1:00 PM Pacific Time**. The judging period runs from September 4 at 10:00 AM PT through September 21 at 5:00 PM PT. The working project must remain available to judges, free of charge and without restrictions, through the end of judging.

## Current status

| Requirement | Status | Evidence or required action |
|---|---|---|
| WebMCP-powered web app | Ready | Nine tools are registered from the main page with `document.modelContext.registerTool()` |
| Working, coherent product experience | Ready locally | Conversational concierge, recommendations, interactive map, cart, expiring holds, release, and simulated checkout work as one flow |
| Main document, no iframe | Ready | `index.html` loads the WebMCP module directly and contains no iframe |
| Narrow JSON Schemas | Ready | All tools declare input schemas; object schemas reject undeclared properties |
| Read-only and state-changing actions separated | Ready | Search/details/ranking/cart are read-only; highlight/select/hold/release/checkout are distinct actions |
| Explicit confirmation for consequential checkout | Ready | `proceed_to_checkout` accepts only `SIMULATE_CHECKOUT`; guided chat asks for a separate confirmation phrase |
| English submission experience | Ready | Page, messages, tool descriptions, README, checklist, and submission draft are in English |
| Fictional and isolated data | Ready | Only the `webmcp_demo` PostgreSQL schema is used; no real inventory, customer, payment, or ticket APIs |
| Source, assets, and run instructions | Ready locally | Repository includes complete source, Docker setup, Windows/WSL/Linux steps, reset scripts, and health checks |
| Open-source license | Ready locally | MIT `LICENSE` is present; verify the hosting site detects it in the repository About section |
| New project or dated meaningful extension | Needs repository history | Create the initial commit before the deadline and preserve dated commits that show the WebMCP implementation |
| Public source repository URL | **User action required** | Publish to GitHub, GitLab, or Bitbucket; confirm `.env` and tunnel credentials are absent before pushing |
| Working public live URL | **User action required** | Start the Cloudflare Tunnel profile, configure HTTPS, and test from outside the home network in a compatible client |
| Public YouTube demo under three minutes | **User action required** | Record with audio, demonstrate the working app and WebMCP, publish publicly, and add the URL to Devpost |
| Devpost text description | Draft ready | Copy and adapt `SUBMISSION.md`; it directly answers all four required description points |
| Devpost registration and final submission | **User action required** | Join the challenge, complete every required field, and submit before the deadline |
| Availability through September 21 | **User action required** | Keep the server, database, internet connection, and Cloudflare Tunnel healthy through 5:00 PM PT |
| Participant/team eligibility | Owner confirmation required | Confirm age, supported country/territory, authorized representative if a team, and absence of disqualifying conflicts |
| Ownership and brand/media rights | Owner confirmation required | Confirm authorization to use the Ticketnauta name/visual identity and use only owned or licensed audio and visuals in the video |

## Judge-path verification

Complete this after the public hostname is live:

1. Open the HTTPS URL in the ChatGPT in-app browser.
2. Verify the header reports `9 WebMCP tools active`.
3. Ask an agent to search fictional events and inspect one event.
4. Ask for four contiguous seats under a total budget and a preferred zone.
5. Confirm the map highlights the returned seats and the conversation labels the call `WEBMCP`.
6. Ask the agent to select and hold the option; verify the countdown appears.
7. Read the cart summary, release the hold, and verify availability returns.
8. Create another hold and complete only the simulated checkout with explicit confirmation.
9. Repeat the essential flow in Chrome 149+ with `chrome://flags/#enable-webmcp-testing` enabled.
10. Test the URL from an external network and a clean browser session.

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

- **WebMCP Leverage:** nine focused tools cover discovery, decision support, visible page updates, and safe actions.
- **Execution:** the demo is a complete user journey backed by PostgreSQL and Docker rather than a static mockup.
- **Potential Impact:** the agent reduces the difficult multi-constraint seat search while the human keeps visual context and control.
- **Creativity and Ambition:** the shared conversation timeline makes human-guided and browser-agent actions transparent on the same live seat map.
