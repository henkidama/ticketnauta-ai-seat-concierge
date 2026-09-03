const emptyObjectSchema = Object.freeze({
  type: 'object',
  properties: {},
  additionalProperties: false,
});

function createUuid() {
  if (typeof globalThis.crypto?.randomUUID === 'function') {
    return globalThis.crypto.randomUUID();
  }

  const bytes = new Uint8Array(16);
  if (typeof globalThis.crypto?.getRandomValues === 'function') {
    globalThis.crypto.getRandomValues(bytes);
  } else {
    for (let index = 0; index < bytes.length; index += 1) {
      bytes[index] = Math.floor(Math.random() * 256);
    }
  }

  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = Array.from(bytes, (value) => value.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

function toolResult(payload) {
  return {
    content: [{ type: 'text', text: JSON.stringify(payload) }],
  };
}

function toolError(error) {
  const problem = error?.problem ?? {};
  return {
    isError: true,
    content: [{
      type: 'text',
      text: JSON.stringify({
        error: error instanceof Error ? error.message : String(error),
        code: error?.code,
        status: error?.status,
        unavailableSeatIds: problem.unavailableSeatIds,
        recovery: problem.recovery,
      }),
    }],
  };
}

function emitToolActivity(detail) {
  window.dispatchEvent(new CustomEvent('ticketnauta:tool-activity', { detail }));
}

function execute(toolName, action) {
  return async (input = {}, execution = {}) => {
    const callId = createUuid();
    const startedAt = performance.now();
    emitToolActivity({ callId, phase: 'start', toolName, input });
    try {
      const result = await action(input, execution.signal);
      emitToolActivity({
        callId,
        phase: 'complete',
        toolName,
        input,
        result,
        durationMs: Math.round(performance.now() - startedAt),
      });
      return toolResult(result);
    } catch (error) {
      emitToolActivity({
        callId,
        phase: 'error',
        toolName,
        input,
        error: error instanceof Error ? error.message : String(error),
        code: error?.code,
        problem: error?.problem,
        durationMs: Math.round(performance.now() - startedAt),
      });
      return toolError(error);
    }
  };
}

export async function registerWebMcpTools(app, onStatus) {
  if (!document.modelContext?.registerTool) {
    onStatus('unsupported', 'WebMCP unavailable');
    return null;
  }

  const tools = [
    {
      name: 'search_events',
      title: 'Search demo events',
      description: 'Search only the fictional events in this Ticketnauta demo by name, city, or venue. This tool never queries real inventory.',
      inputSchema: {
        type: 'object',
        properties: {
          query: {
            type: 'string',
            maxLength: 80,
            description: 'Optional search text, for example Tijuana, comedy, or Horizon Demo Forum.',
          },
        },
        additionalProperties: false,
      },
      annotations: { readOnlyHint: true, untrustedContentHint: false },
      execute: execute('search_events', ({ query = '' }, signal) => app.searchEvents(query, signal)),
    },
    {
      name: 'get_event_details',
      title: 'Get event and zone details',
      description: 'Get the date, venue, zones, prices, and aggregate availability for a fictional event, and open its visible map. This tool does not change holds or the cart.',
      inputSchema: {
        type: 'object',
        properties: {
          event_id: {
            type: 'string',
            minLength: 1,
            maxLength: 80,
            description: 'Exact event identifier returned by search_events.',
          },
        },
        required: ['event_id'],
        additionalProperties: false,
      },
      annotations: { readOnlyHint: true, untrustedContentHint: false },
      execute: execute('get_event_details', async ({ event_id }, signal) => {
        const details = await app.getEventDetails(event_id, signal);
        const statuses = Object.groupBy(details.seats, (seat) => seat.status);
        return {
          event: details.event,
          description: details.description,
          zones: details.zones,
          seatMap: {
            totalSeats: details.seats.length,
            availableSeats: statuses.available?.length ?? 0,
            heldSeats: (statuses.held?.length ?? 0) + (statuses.held_by_you?.length ?? 0),
            unavailableSeats: (statuses.blocked?.length ?? 0) + (statuses.sold?.length ?? 0),
          },
        };
      }),
    },
    {
      name: 'find_seat_options',
      title: 'Find explainable seat options',
      description: 'Rank up to five available seat combinations by quantity, total budget, zone, priority, accessibility, aisle access, and inventory-friendly placement. Returns score evidence and tradeoffs. A 2+2 split is used only when explicitly allowed and no contiguous block matches. This is read-only.',
      inputSchema: {
        type: 'object',
        properties: {
          event_id: {
            type: 'string',
            minLength: 1,
            maxLength: 80,
            description: 'Exact identifier of the fictional event.',
          },
          quantity: {
            type: 'integer',
            minimum: 1,
            maximum: 8,
            description: 'Required number of contiguous seats, from 1 to 8.',
          },
          max_total_budget: {
            type: 'number',
            exclusiveMinimum: 0,
            maximum: 100000,
            description: 'Maximum total budget in Mexican pesos for all seats.',
          },
          zone_preference: {
            type: 'string',
            enum: ['any', 'diamond', 'gold', 'preferred', 'general'],
            description: 'Zone to prioritize; any compares every zone.',
          },
          preference: {
            type: 'string',
            enum: ['any', 'closest_to_stage', 'center', 'best_value', 'accessible'],
            description: 'Primary criterion for ranking alternatives.',
          },
          prefer_aisle: {
            type: 'boolean',
            description: 'When true, strongly prefer a block that includes seat 1 or the last seat in a row.',
          },
          require_accessible_pair: {
            type: 'boolean',
            description: 'Require an accessible position plus an adjacent standard companion seat. Quantity must be at least 2.',
          },
          allow_split_pairs: {
            type: 'boolean',
            description: 'For a group of four only, allow two pairs in adjacent rows if no four-seat contiguous block matches.',
          },
          avoid_orphan_seats: {
            type: 'boolean',
            description: 'Prefer choices that do not leave one isolated available seat in a row segment. Defaults to true.',
          },
        },
        required: ['event_id', 'quantity'],
        additionalProperties: false,
      },
      annotations: { readOnlyHint: true, untrustedContentHint: false },
      execute: execute('find_seat_options', ({
        event_id,
        quantity,
        max_total_budget,
        zone_preference,
        preference,
        prefer_aisle,
        require_accessible_pair,
        allow_split_pairs,
        avoid_orphan_seats,
      }, signal) =>
        app.findSeatOptions({
          eventId: event_id,
          quantity,
          maxTotalBudget: max_total_budget,
          zonePreference: zone_preference ?? 'any',
          preference: preference ?? 'center',
          preferAisle: prefer_aisle ?? false,
          requireAccessiblePair: require_accessible_pair ?? false,
          allowSplitPairs: allow_split_pairs ?? false,
          avoidOrphanSeats: avoid_orphan_seats ?? true,
        }, signal)),
    },
    {
      name: 'highlight_seats',
      title: 'Highlight seats on the map',
      description: 'Update the visible map to highlight a specific list of fictional seats. This tool does not change availability, the cart, or holds.',
      inputSchema: {
        type: 'object',
        properties: {
          event_id: {
            type: 'string',
            minLength: 1,
            maxLength: 80,
            description: 'Event that contains the seats.',
          },
          seat_ids: {
            type: 'array',
            minItems: 1,
            maxItems: 8,
            uniqueItems: true,
            description: 'Exact seat IDs returned by find_seat_options.',
            items: { type: 'string', minLength: 1, maxLength: 140 },
          },
        },
        required: ['event_id', 'seat_ids'],
        additionalProperties: false,
      },
      annotations: { readOnlyHint: false, untrustedContentHint: false },
      execute: execute('highlight_seats', ({ event_id, seat_ids }, signal) =>
        app.highlightSeats({ eventId: event_id, seatIds: seat_ids }, signal)),
    },
    {
      name: 'select_seat_option',
      title: 'Select a seat option',
      description: 'Replace the current demo cart selection with an option from the most recent search. This does not create or release a hold. If availability changed, call find_seat_options again, explain the replacement, and obtain renewed user approval before selecting it.',
      inputSchema: {
        type: 'object',
        properties: {
          option_id: {
            type: 'string',
            pattern: '^opt_[a-f0-9]{12}$',
            description: 'Exact option ID returned by find_seat_options.',
          },
        },
        required: ['option_id'],
        additionalProperties: false,
      },
      annotations: { readOnlyHint: false, untrustedContentHint: false },
      execute: execute('select_seat_option', ({ option_id }, signal) => app.selectSeatOption(option_id, signal)),
    },
    {
      name: 'hold_seats',
      title: 'Temporarily hold selected seats',
      description: 'Create a temporary hold in the demo database for the currently selected seats, only after explicit user approval. This changes availability and expires automatically. If a seat_conflict is returned, refresh and rank replacements, explain what changed, and ask again before holding a replacement.',
      inputSchema: {
        type: 'object',
        properties: {
          confirmation: {
            type: 'string',
            enum: ['HOLD_SELECTED_SEATS'],
            description: 'Explicit confirmation that the user approved holding the currently selected fictional seats.',
          },
        },
        required: ['confirmation'],
        additionalProperties: false,
      },
      annotations: { readOnlyHint: false, untrustedContentHint: false },
      execute: execute('hold_seats', ({ confirmation }, signal) => app.holdSelectedSeats(confirmation, signal)),
    },
    {
      name: 'release_seats',
      title: 'Release held seats',
      description: 'Release the active hold for this demo session and return those seats to availability. This never affects real inventory.',
      inputSchema: emptyObjectSchema,
      annotations: { readOnlyHint: false, untrustedContentHint: false },
      execute: execute('release_seats', (_, signal) => app.releaseSeats(signal)),
    },
    {
      name: 'get_cart_summary',
      title: 'Get cart and hold summary',
      description: 'Get the selection, fictional total, and remaining hold time for this tab. This tool does not change state.',
      inputSchema: emptyObjectSchema,
      annotations: { readOnlyHint: true, untrustedContentHint: false },
      execute: execute('get_cart_summary', (_, signal) => app.getCartSummary(signal)),
    },
    {
      name: 'proceed_to_checkout',
      title: 'Complete simulated checkout',
      description: 'Complete the active hold as a strictly simulated checkout. This changes demo data, but never charges money, issues tickets, or calls real APIs.',
      inputSchema: {
        type: 'object',
        properties: {
          confirmation: {
            type: 'string',
            enum: ['SIMULATE_CHECKOUT'],
            description: 'Explicit confirmation that the user requested the simulation with no real charge.',
          },
        },
        required: ['confirmation'],
        additionalProperties: false,
      },
      annotations: { readOnlyHint: false, untrustedContentHint: false },
      execute: execute('proceed_to_checkout', ({ confirmation }, signal) => app.proceedToCheckout(confirmation, signal)),
    },
  ];

  const controller = new AbortController();
  try {
    for (const tool of tools) {
      await document.modelContext.registerTool(tool, { signal: controller.signal });
    }
    window.addEventListener('pagehide', () => controller.abort(), { once: true });
    onStatus('ready', `${tools.length} WebMCP tools active`);
    return controller;
  } catch (error) {
    controller.abort();
    onStatus('error', 'WebMCP registration failed');
    console.error('WebMCP registration failed', error);
    return null;
  }
}
