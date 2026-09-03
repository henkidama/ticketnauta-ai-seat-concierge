import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const root = new URL('../', import.meta.url);
const webMcp = await readFile(new URL('src/Ticketnauta.WebMcp.Api/wwwroot/webmcp.js', root), 'utf8');
const app = await readFile(new URL('src/Ticketnauta.WebMcp.Api/wwwroot/app.js', root), 'utf8');
const page = await readFile(new URL('src/Ticketnauta.WebMcp.Api/wwwroot/index.html', root), 'utf8');

const expectedTools = [
  'search_events',
  'get_event_details',
  'find_seat_options',
  'highlight_seats',
  'select_seat_option',
  'hold_seats',
  'release_seats',
  'get_cart_summary',
  'proceed_to_checkout',
];

const registeredNames = [...webMcp.matchAll(/\bname:\s*'([^']+)'/g)].map((match) => match[1]);
assert.deepEqual(registeredNames, expectedTools, 'The WebMCP tool surface must remain the expected nine narrow tools.');
assert.match(webMcp, /document\.modelContext\.registerTool\(tool,/, 'Tools must register from the main document.');
assert.equal((webMcp.match(/\binputSchema:/g) ?? []).length, expectedTools.length, 'Every tool needs an input schema.');
assert.equal((webMcp.match(/\bannotations:/g) ?? []).length, expectedTools.length, 'Every tool needs behavior annotations.');
assert.match(webMcp, /enum:\s*\['SIMULATE_CHECKOUT'\]/, 'Checkout must require explicit simulated confirmation.');
assert.match(webMcp, /enum:\s*\['HOLD_SELECTED_SEATS'\]/, 'A hold must require explicit user confirmation.');
for (const field of ['prefer_aisle', 'require_accessible_pair', 'allow_split_pairs', 'avoid_orphan_seats']) {
  assert.match(webMcp, new RegExp(`\\b${field}:`), `find_seat_options must expose ${field}.`);
}

assert.match(page, /<html lang="en">/, 'The evaluation-facing page must declare English.');
assert.match(page, /id="concierge-form"/, 'The conversational concierge must be present.');
assert.match(page, /id="conversation-feed"/, 'Visible tool activity must be present.');
assert.match(page, /id="load-judge-scenario"/, 'Judge Mode must expose a repeatable scenario launcher.');
assert.match(page, /id="simulate-competitor"/, 'Live Seat Rescue must expose the competing-buyer control.');
assert.match(page, /id="accessible-pair"/, 'The UI must expose accessible companion seating.');
assert.match(
  page,
  /id="release-button" class="release-button hidden"[^>]*>Release hold now<\/button>/,
  'An active demo hold must expose a prominent manual release action.',
);
assert.equal(
  (page.match(/data-concierge-prompt="[^"]+" disabled/g) ?? []).length,
  3,
  'Suggested prompts must stay disabled until initial event state is ready.',
);
assert.doesNotMatch(page, /<iframe\b/i, 'WebMCP must run in the main page, not an iframe.');

assert.match(app, /ticketnauta:tool-activity/, 'The UI must listen for WebMCP tool activity.');
assert.match(app, /source:\s*'WEBMCP'/, 'Browser-agent actions must be labeled separately.');
assert.match(app, /source:\s*'GUIDED'/, 'Guided chat actions must be labeled separately.');
assert.match(app, /class DemoApiError/, 'Structured API errors must remain available to WebMCP tools.');
assert.match(app, /error\?\.code !== 'seat_conflict'/, 'The guided experience must recover from seat conflicts.');
assert.match(app, /async function releaseHoldFromUi\(\)/, 'The manual release action must be visible in the guided activity trace.');
assert.match(app, /\/api\/demo\/competing-hold/, 'Judge Mode must exercise a real backend availability change.');
assert.match(app, /state\.conciergeReady = true/, 'The concierge must enable only after initial data is ready.');
assert.match(app, /crypto\?\.getRandomValues/, 'The main page needs an HTTP LAN-safe UUID fallback.');
assert.match(webMcp, /crypto\?\.getRandomValues/, 'WebMCP activity IDs need an HTTP LAN-safe UUID fallback.');

console.log('WebMCP contract check passed: 9 narrow tools, English Judge Mode, structured recovery, visible traces, prominent hold release, and explicit hold/checkout confirmation.');
