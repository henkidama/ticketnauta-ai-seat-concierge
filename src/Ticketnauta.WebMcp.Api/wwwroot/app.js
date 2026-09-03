import { registerWebMcpTools } from './webmcp.js?v=4';

const svgNamespace = 'http://www.w3.org/2000/svg';
const money = new Intl.NumberFormat('en-US', {
  style: 'currency',
  currency: 'MXN',
  currencyDisplay: 'narrowSymbol',
  maximumFractionDigits: 0,
});
const dateTime = new Intl.DateTimeFormat('en-US', {
  weekday: 'short',
  day: 'numeric',
  month: 'short',
  hour: 'numeric',
  minute: '2-digit',
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

const elements = Object.fromEntries([
  'webmcp-status', 'event-search-form', 'event-search', 'event-list',
  'hero-city', 'hero-title', 'hero-tagline', 'hero-meta',
  'seat-preferences-form', 'seat-quantity', 'seat-budget', 'zone-preference',
  'seat-priority', 'find-seats-button', 'map-title', 'zone-chips',
  'seat-map-wrapper', 'map-loading', 'seat-map', 'map-availability',
  'options-count', 'options-empty', 'seat-options', 'cart-empty',
  'cart-content', 'cart-event', 'cart-seats', 'cart-total', 'hold-badge',
  'hold-button', 'checkout-button', 'release-button', 'toast-region',
  'receipt-dialog', 'receipt-message', 'receipt-reference', 'receipt-total',
  'conversation-feed', 'concierge-form', 'concierge-input', 'concierge-send',
  'activity-count', 'prefer-aisle', 'accessible-pair', 'allow-split-pairs',
  'avoid-orphan-seats', 'judge-agent-prompt', 'copy-judge-prompt',
  'load-judge-scenario', 'simulate-competitor', 'reset-judge-scenario',
  'judge-status',
].map((id) => [id, document.getElementById(id)]));

const readOnlyTools = new Set([
  'search_events',
  'get_event_details',
  'find_seat_options',
  'get_cart_summary',
]);

const visualTools = new Set(['highlight_seats']);
const consequentialTools = new Set([
  'select_seat_option',
  'hold_seats',
  'release_seats',
  'proceed_to_checkout',
  'simulate_competing_buyer',
]);

const state = {
  sessionId: getOrCreateSessionId(),
  events: [],
  details: null,
  cart: null,
  options: new Map(),
  highlighted: new Set(),
  selected: new Set(),
  focusedOptionId: null,
  expiryRefreshPending: false,
  activityCount: 0,
  toolActivityRows: new Map(),
  conciergeReady: false,
  conciergeBusy: false,
  lastSearch: null,
  pendingRecoveryOptionId: null,
  judgeScenario: 'ready',
};

function traceValue(value, maxLength = 135) {
  if (value == null) return 'no payload';
  const serialized = typeof value === 'string' ? value : JSON.stringify(value);
  return serialized.length > maxLength ? `${serialized.slice(0, maxLength - 1)}…` : serialized;
}

function toolKind(toolName) {
  if (readOnlyTools.has(toolName)) return { className: 'read-only', label: 'READ ONLY' };
  if (visualTools.has(toolName)) return { className: 'visual', label: 'VISUAL' };
  if (consequentialTools.has(toolName)) return { className: 'consequential', label: 'CONSEQUENTIAL' };
  return { className: 'action', label: 'DEMO ACTION' };
}

function updateConciergeControls() {
  const disabled = !state.conciergeReady || state.conciergeBusy;
  elements['concierge-input'].disabled = disabled;
  elements['concierge-send'].disabled = disabled;
  document.querySelectorAll('[data-concierge-prompt]').forEach((button) => {
    button.disabled = disabled;
  });
  elements['concierge-input'].placeholder = state.conciergeReady
    ? 'Ask for seats, a hold, or your cart…'
    : 'Preparing the concierge…';
}

function appendConversationMessage(role, message) {
  const item = document.createElement('article');
  item.className = `conversation-message ${role}`;

  const avatar = document.createElement('span');
  avatar.className = 'message-avatar';
  avatar.setAttribute('aria-hidden', 'true');
  avatar.textContent = role === 'user' ? 'YOU' : '✦';

  const content = document.createElement('div');
  const author = document.createElement('strong');
  author.textContent = role === 'user' ? 'You' : 'AI Seat Concierge';
  const body = document.createElement('p');
  body.textContent = message;
  content.append(author, body);
  item.append(avatar, content);
  elements['conversation-feed'].append(item);

  while (elements['conversation-feed'].children.length > 45) {
    elements['conversation-feed'].firstElementChild.remove();
  }
  elements['conversation-feed'].scrollTop = elements['conversation-feed'].scrollHeight;
}

function renderToolActivity({ callId, toolName, source, phase, input, result, error, code, durationMs }) {
  if (!callId || !toolName) return;
  let row = state.toolActivityRows.get(callId);

  if (!row) {
    row = document.createElement('article');
    row.className = `tool-activity ${source.toLowerCase()} running`;

    const icon = document.createElement('span');
    icon.className = 'tool-activity-icon';
    icon.setAttribute('aria-hidden', 'true');
    icon.textContent = '↗';

    const copy = document.createElement('div');
    copy.className = 'tool-activity-copy';
    const sourceBadge = document.createElement('span');
    sourceBadge.className = 'tool-source';
    sourceBadge.textContent = source;
    const name = document.createElement('code');
    name.textContent = toolName;
    const classification = toolKind(toolName);
    const kind = document.createElement('span');
    kind.className = `tool-kind ${classification.className}`;
    kind.textContent = classification.label;
    const detail = document.createElement('span');
    detail.className = 'tool-detail';
    detail.dataset.toolDetail = '';
    detail.textContent = `Input · ${traceValue(input)}`;
    copy.append(sourceBadge, name, kind, detail);

    const status = document.createElement('span');
    status.className = 'tool-status';
    status.dataset.toolStatus = '';
    status.textContent = 'RUNNING';
    row.append(icon, copy, status);
    elements['conversation-feed'].append(row);
    state.toolActivityRows.set(callId, row);
  }

  if (phase === 'complete' || phase === 'error') {
    row.classList.remove('running');
    row.classList.add(phase);
    const status = row.querySelector('[data-tool-status]');
    status.textContent = phase === 'complete' ? 'DONE' : 'ERROR';
    const detail = row.querySelector('[data-tool-detail]');
    const elapsed = Number.isFinite(durationMs) ? `${durationMs} ms · ` : '';
    detail.textContent = phase === 'complete'
      ? `${elapsed}Result · ${traceValue(result)}`
      : `${elapsed}${code ? `${code} · ` : ''}${error || 'Tool call failed'}`;
    row.title = phase === 'complete' ? traceValue(result, 900) : (error || 'Tool call failed');
    state.activityCount += 1;
    elements['activity-count'].textContent = String(state.activityCount);
    state.toolActivityRows.delete(callId);
  }

  elements['conversation-feed'].scrollTop = elements['conversation-feed'].scrollHeight;
}

function optionSummary(option) {
  if (!option) return 'No matching seat block is available with those preferences.';
  const layout = option.layout === 'split_2_plus_2' ? ' Two-pair fallback.' : '';
  return `${option.zoneName}, row ${option.row}: ${option.seatLabels.join(', ')} for ${money.format(option.totalPrice)} total, ${option.matchScore}% fit. ${option.reason}${layout}`;
}

function cartSummary(cart) {
  if (!cart?.seats?.length) return 'Your demo cart is empty.';
  const labels = cart.seats.map((seat) => seat.label).join(', ');
  const hold = cart.hold?.status === 'active'
    ? ` The hold has about ${Math.ceil(cart.hold.remainingSeconds / 60)} minutes remaining.`
    : ' The seats are selected but not held yet.';
  return `${cart.seats.length} seats (${labels}) total ${money.format(cart.total)}.${hold}`;
}

function summarizeToolResult(toolName, result) {
  switch (toolName) {
    case 'search_events':
      return `The browser agent found ${result?.events?.length ?? 0} fictional event${result?.events?.length === 1 ? '' : 's'}.`;
    case 'get_event_details':
      return `${result?.event?.name ?? 'The event'} is open on the map with ${result?.seatMap?.availableSeats ?? 0} seats currently available.`;
    case 'find_seat_options': {
      const options = result?.options ?? [];
      return options.length
        ? `The browser agent ranked ${options.length} explainable option${options.length === 1 ? '' : 's'}. Best match: ${optionSummary(options[0])}`
        : (result?.message || 'No seat option matched those constraints.');
    }
    case 'highlight_seats':
      return result?.message || 'The requested seats are highlighted on the visible map.';
    case 'select_seat_option':
      return `The browser agent updated the selection. ${cartSummary(result)}`;
    case 'hold_seats':
      return `The browser agent created a temporary demo hold. ${cartSummary(result)}`;
    case 'release_seats':
      return 'The browser agent released the active demo hold and returned the seats to availability.';
    case 'get_cart_summary':
      return cartSummary(result);
    case 'proceed_to_checkout':
      return `Simulated checkout complete. Reference ${result?.reference ?? 'created'}; no payment or real ticket was issued.`;
    default:
      return 'The browser agent completed a visible demo action.';
  }
}

function handleWebMcpActivity(event) {
  const detail = event.detail;
  if (!detail || !['start', 'complete', 'error'].includes(detail.phase)) return;
  renderToolActivity({ ...detail, source: 'WEBMCP' });
  if (detail.phase === 'complete') {
    appendConversationMessage('assistant', summarizeToolResult(detail.toolName, detail.result));
    if (detail.toolName === 'find_seat_options' && state.judgeScenario === 'armed') {
      setJudgeStatus('recovered', 'The browser agent refreshed inventory and highlighted a valid replacement.');
    }
  } else if (detail.phase === 'error') {
    if (detail.code === 'seat_conflict') {
      setJudgeStatus('armed', 'Conflict detected. The agent should refresh, explain a replacement, and ask again.');
      appendConversationMessage(
        'assistant',
        'Live conflict detected: another buyer took part of the stale recommendation. The agent received structured recovery instructions and should now refresh alternatives before requesting renewed approval.',
      );
    } else {
      appendConversationMessage('assistant', `The browser-agent request could not finish: ${detail.error}`);
    }
  }
}

async function runGuidedTool(toolName, input, action) {
  const callId = createUuid();
  const startedAt = performance.now();
  renderToolActivity({ callId, toolName, source: 'GUIDED', phase: 'start', input });
  try {
    const result = await action();
    renderToolActivity({
      callId,
      toolName,
      source: 'GUIDED',
      phase: 'complete',
      input,
      result,
      durationMs: Math.round(performance.now() - startedAt),
    });
    return result;
  } catch (error) {
    renderToolActivity({
      callId,
      toolName,
      source: 'GUIDED',
      phase: 'error',
      error: error instanceof Error ? error.message : String(error),
      code: error?.code,
      durationMs: Math.round(performance.now() - startedAt),
    });
    throw error;
  }
}

function parseConciergeRequest(request) {
  const normalized = request.toLowerCase();
  const quantityMatch = normalized.match(/\b([1-8])\s*(?:seat|seats|guest|guests|ticket|tickets)\b/);
  const numberWords = {
    one: 1, two: 2, three: 3, four: 4, five: 5, six: 6, seven: 7, eight: 8,
  };
  const wordMatch = normalized.match(/\b(one|two|three|four|five|six|seven|eight)\s+(?:seat|seats|guest|guests|ticket|tickets)\b/);
  const quantity = quantityMatch
    ? Number(quantityMatch[1])
    : (wordMatch ? numberWords[wordMatch[1]] : Number(elements['seat-quantity'].value));

  const budgetMatch = normalized.match(/\b(?:under|below|up to|maximum|max|budget(?:\s+of)?)\s*(?:mxn|mx\$|\$)?\s*([\d,.]+)/i)
    || normalized.match(/(?:mx\$|\$)\s*([\d,.]+)/i);
  const parsedBudget = budgetMatch ? Number(budgetMatch[1].replaceAll(',', '')) : null;
  const currentBudget = Number(elements['seat-budget'].value);

  let zonePreference = 'any';
  if (normalized.includes('diamond')) zonePreference = 'diamond';
  else if (normalized.includes('gold')) zonePreference = 'gold';
  else if (normalized.includes('preferred')) zonePreference = 'preferred';
  else if (normalized.includes('general')) zonePreference = 'general';

  let preference = 'center';
  if (/accessible|accessibility|wheelchair/.test(normalized)) preference = 'accessible';
  else if (/closest|near(?:est)?\s+(?:the\s+)?stage|front/.test(normalized)) preference = 'closest_to_stage';
  else if (/best\s+value|value|cheapest|lowest\s+price/.test(normalized)) preference = 'best_value';
  else if (/any\s+(?:seat|priority)|no\s+preference/.test(normalized)) preference = 'any';

  const preferAisle = /aisle|row edge/.test(normalized) || elements['prefer-aisle'].checked;
  const requireAccessiblePair = preference === 'accessible'
    || /accessible.*companion|wheelchair.*companion/.test(normalized)
    || elements['accessible-pair'].checked;
  const allowSplitPairs = /(?:2\s*\+\s*2)|split\s+(?:pair|group)|two pairs/.test(normalized)
    || elements['allow-split-pairs'].checked;
  const avoidOrphanSeats = !/allow\s+orphan/.test(normalized)
    && elements['avoid-orphan-seats'].checked;

  return {
    normalized,
    quantity,
    maxTotalBudget: Number.isFinite(parsedBudget) && parsedBudget > 0
      ? parsedBudget
      : (Number.isFinite(currentBudget) && currentBudget > 0 ? currentBudget : null),
    zonePreference,
    preference,
    preferAisle,
    requireAccessiblePair,
    allowSplitPairs,
    avoidOrphanSeats,
  };
}

function syncPreferenceControls(preferences) {
  elements['seat-quantity'].value = String(preferences.quantity);
  if (preferences.maxTotalBudget) elements['seat-budget'].value = String(preferences.maxTotalBudget);
  elements['zone-preference'].value = preferences.zonePreference;
  elements['seat-priority'].value = preferences.preference;
  elements['prefer-aisle'].checked = Boolean(preferences.preferAisle);
  elements['accessible-pair'].checked = Boolean(preferences.requireAccessiblePair);
  elements['allow-split-pairs'].checked = Boolean(preferences.allowSplitPairs);
  elements['avoid-orphan-seats'].checked = preferences.avoidOrphanSeats !== false;
}

function optionAt(index) {
  return [...state.options.values()][index - 1];
}

function setJudgeStatus(status, message) {
  state.judgeScenario = status;
  elements['judge-status'].className = `judge-status ${status}`;
  elements['judge-status'].querySelector('span').textContent = message;
}

function searchInputFromControls() {
  const budgetValue = elements['seat-budget'].value.trim();
  return {
    eventId: state.details?.event.id,
    quantity: Number(elements['seat-quantity'].value),
    maxTotalBudget: budgetValue ? Number(budgetValue) : null,
    zonePreference: elements['zone-preference'].value,
    preference: elements['seat-priority'].value,
    preferAisle: elements['prefer-aisle'].checked,
    requireAccessiblePair: elements['accessible-pair'].checked,
    allowSplitPairs: elements['allow-split-pairs'].checked,
    avoidOrphanSeats: elements['avoid-orphan-seats'].checked,
  };
}

async function recoverFromSeatConflict(error) {
  const eventId = state.cart?.eventId || state.details?.event.id;
  const search = state.lastSearch || searchInputFromControls();
  await getEventDetails(eventId, undefined, { keepOptions: false });
  const result = await runGuidedTool(
    'find_seat_options',
    {
      event_id: eventId,
      quantity: search.quantity,
      max_total_budget: search.maxTotalBudget,
      zone_preference: search.zonePreference,
      preference: search.preference,
      prefer_aisle: search.preferAisle,
      require_accessible_pair: search.requireAccessiblePair,
      allow_split_pairs: search.allowSplitPairs,
      avoid_orphan_seats: search.avoidOrphanSeats,
    },
    () => findSeatOptions({ ...search, eventId }),
  );
  const replacement = result.options[0];
  if (!replacement) {
    appendConversationMessage(
      'assistant',
      `Availability changed${error?.problem?.unavailableSeatIds?.length ? ` for ${error.problem.unavailableSeatIds.length} seat` : ''}, and no safe replacement matches the original constraints. I did not change the cart or create a hold.`,
    );
    return;
  }

  state.pendingRecoveryOptionId = replacement.optionId;
  setJudgeStatus('recovered', 'A fresh replacement is highlighted; renewed human approval is required.');
  appendConversationMessage(
    'assistant',
    `Another buyer changed availability, so I refreshed the live map and ranked a replacement. ${optionSummary(replacement)} I have not selected or held it. Type “Confirm replacement hold” to approve the new option.`,
  );
}

async function handleConciergeRequest(request) {
  if (!state.conciergeReady || state.conciergeBusy) return;
  const text = request.trim();
  if (!text) return;
  appendConversationMessage('user', text);
  elements['concierge-input'].value = '';
  state.conciergeBusy = true;
  updateConciergeControls();

  try {
    const preferences = parseConciergeRequest(text);
    const normalized = preferences.normalized;

    if (/\b(?:release|cancel)\b.*\bhold\b|\brelease\s+(?:my\s+)?seats\b/.test(normalized)) {
      const cart = await runGuidedTool('release_seats', {}, () => releaseSeats());
      appendConversationMessage('assistant', `Your hold is released. ${cartSummary(cart)}`);
      return;
    }

    if (/\bconfirm replacement hold\b/.test(normalized)) {
      const option = state.options.get(state.pendingRecoveryOptionId);
      if (!option) throw new Error('There is no pending replacement. Run a fresh seat search first.');
      await runGuidedTool(
        'select_seat_option',
        { option_id: option.optionId },
        () => selectSeatOption(option.optionId),
      );
      const cart = await runGuidedTool(
        'hold_seats',
        { confirmation: 'HOLD_SELECTED_SEATS' },
        () => holdSelectedSeats('HOLD_SELECTED_SEATS'),
      );
      state.pendingRecoveryOptionId = null;
      setJudgeStatus('recovered', 'Replacement approved and safely held after recovery.');
      appendConversationMessage('assistant', `Replacement approved and held. ${optionSummary(option)} ${cartSummary(cart)}`);
      return;
    }

    if (/\bhold\b.*\b(?:selected|current|these)\b|\bhold (?:them|it)\b/.test(normalized)) {
      try {
        const cart = await runGuidedTool(
          'hold_seats',
          { confirmation: 'HOLD_SELECTED_SEATS' },
          () => holdSelectedSeats('HOLD_SELECTED_SEATS'),
        );
        appendConversationMessage('assistant', `The selected seats are now held. ${cartSummary(cart)}`);
      } catch (error) {
        if (error?.code !== 'seat_conflict') throw error;
        await recoverFromSeatConflict(error);
      }
      return;
    }

    if (/\bconfirm simulated checkout\b/.test(normalized)) {
      const receipt = await runGuidedTool(
        'proceed_to_checkout',
        { confirmation: 'SIMULATE_CHECKOUT' },
        () => proceedToCheckout('SIMULATE_CHECKOUT'),
      );
      appendConversationMessage('assistant', `Simulated checkout complete: ${receipt.reference}. No money was charged and no real ticket was issued.`);
      return;
    }

    if (/\bcheckout\b|\bbuy\b|\bpurchase\b|\bcomplete\s+(?:the\s+)?order\b/.test(normalized)) {
      appendConversationMessage(
        'assistant',
        'Checkout is a separate consequential demo action. If you want to continue, type exactly: Confirm simulated checkout',
      );
      return;
    }

    if (/\bcart\b|\bsummary\b|\bwhat (?:did|have) i (?:select|hold)\b/.test(normalized)) {
      const cart = await runGuidedTool('get_cart_summary', {}, () => getCartSummary());
      appendConversationMessage('assistant', cartSummary(cart));
      return;
    }

    const optionMatch = normalized.match(/\b(?:option|alternative)\s*(?:#\s*)?([1-5])\b/);
    if (optionMatch && /\b(?:highlight|show|view)\b/.test(normalized)) {
      const option = optionAt(Number(optionMatch[1]));
      if (!option) throw new Error('Run a seat search first, then ask me to highlight one of its options.');
      await runGuidedTool(
        'highlight_seats',
        { event_id: option.eventId, seat_ids: option.seatIds },
        () => highlightSeats({ eventId: option.eventId, seatIds: option.seatIds }),
      );
      appendConversationMessage('assistant', `Option ${optionMatch[1]} is highlighted. ${optionSummary(option)}`);
      return;
    }

    if (optionMatch && /\b(?:select|choose|add)\b/.test(normalized)) {
      const option = optionAt(Number(optionMatch[1]));
      if (!option) throw new Error('Run a seat search first, then ask me to select one of its options.');
      try {
        const cart = await runGuidedTool(
          'select_seat_option',
          { option_id: option.optionId },
          () => selectSeatOption(option.optionId),
        );
        appendConversationMessage('assistant', `Option ${optionMatch[1]} is selected. ${cartSummary(cart)}`);
      } catch (error) {
        if (error?.code !== 'seat_conflict') throw error;
        await recoverFromSeatConflict(error);
      }
      return;
    }

    syncPreferenceControls(preferences);
    const result = await runGuidedTool(
      'find_seat_options',
      {
        event_id: state.details?.event.id,
        quantity: preferences.quantity,
        max_total_budget: preferences.maxTotalBudget,
        zone_preference: preferences.zonePreference,
        preference: preferences.preference,
        prefer_aisle: preferences.preferAisle,
        require_accessible_pair: preferences.requireAccessiblePair,
        allow_split_pairs: preferences.allowSplitPairs,
        avoid_orphan_seats: preferences.avoidOrphanSeats,
      },
      () => findSeatOptions({
        eventId: state.details?.event.id,
        quantity: preferences.quantity,
        maxTotalBudget: preferences.maxTotalBudget,
        zonePreference: preferences.zonePreference,
        preference: preferences.preference,
        preferAisle: preferences.preferAisle,
        requireAccessiblePair: preferences.requireAccessiblePair,
        allowSplitPairs: preferences.allowSplitPairs,
        avoidOrphanSeats: preferences.avoidOrphanSeats,
      }),
    );

    const best = result.options[0];
    if (!best) {
      appendConversationMessage('assistant', result.message || 'I could not find a contiguous block with those preferences. Try a larger budget or another zone.');
      return;
    }

    const wantsHold = /\bhold\b|\breserve\b|\bbook\b/.test(normalized);
    const wantsSelection = wantsHold || /\bselect\b|\bchoose\b|\badd\b/.test(normalized);
    const wantsHighlight = /\bhighlight\b|\bshow\s+(?:them|it)\s+on\s+(?:the\s+)?map\b/.test(normalized);

    if (wantsHighlight && !wantsSelection) {
      await runGuidedTool(
        'highlight_seats',
        { event_id: best.eventId, seat_ids: best.seatIds },
        () => highlightSeats({ eventId: best.eventId, seatIds: best.seatIds }),
      );
    }

    if (wantsSelection) {
      await runGuidedTool(
        'select_seat_option',
        { option_id: best.optionId },
        () => selectSeatOption(best.optionId),
      );
    }

    if (wantsHold) {
      const cart = await runGuidedTool(
        'hold_seats',
        { confirmation: 'HOLD_SELECTED_SEATS' },
        () => holdSelectedSeats('HOLD_SELECTED_SEATS'),
      );
      appendConversationMessage('assistant', `I found and held the best match. ${optionSummary(best)} ${cartSummary(cart)}`);
    } else if (wantsSelection) {
      appendConversationMessage('assistant', `I selected the best match. ${optionSummary(best)} You can hold it when you are ready.`);
    } else {
      appendConversationMessage(
        'assistant',
        `I ranked ${result.options.length} contiguous option${result.options.length === 1 ? '' : 's'}. Best match: ${optionSummary(best)} Say “select option 1” or “hold the best option” to continue.`,
      );
    }
  } catch (error) {
    appendConversationMessage('assistant', `I could not complete that request: ${error.message}`);
  } finally {
    state.conciergeBusy = false;
    updateConciergeControls();
    elements['concierge-input'].focus();
  }
}

function getOrCreateSessionId() {
  const key = 'ticketnauta-webmcp-demo-session';
  const existing = localStorage.getItem(key);
  if (existing && /^[0-9a-f-]{36}$/i.test(existing)) return existing;
  const value = createUuid();
  localStorage.setItem(key, value);
  return value;
}

class DemoApiError extends Error {
  constructor(message, status, problem = {}) {
    super(message);
    this.name = 'DemoApiError';
    this.status = status;
    this.code = problem.code || 'http_error';
    this.problem = problem;
  }
}

async function apiFetch(path, { method = 'GET', body, signal } = {}) {
  const response = await fetch(path, {
    method,
    signal,
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  });

  if (!response.ok) {
    let message = `The demo returned error ${response.status}.`;
    let problem = {};
    try {
      problem = await response.json();
      message = problem.detail || problem.title || message;
    } catch {
      // Preserve the HTTP fallback when the response has no JSON body.
    }
    throw new DemoApiError(message, response.status, problem);
  }

  return response.status === 204 ? null : response.json();
}

async function searchEvents(query = '', signal) {
  const result = await apiFetch(`/api/events?query=${encodeURIComponent(query || '')}`, { signal });
  state.events = result.events;
  renderEvents();

  const currentStillVisible = state.details &&
    state.events.some((event) => event.id === state.details.event.id);
  if (!currentStillVisible && state.events.length > 0) {
    await getEventDetails(state.events[0].id, signal);
  }

  return result;
}

async function getEventDetails(eventId, signal, { keepOptions = false } = {}) {
  setMapLoading(true);
  try {
    const details = await apiFetch(
      `/api/events/${encodeURIComponent(eventId)}?sessionId=${encodeURIComponent(state.sessionId)}`,
      { signal },
    );
    state.details = details;
    if (!keepOptions) {
      state.options.clear();
      state.highlighted.clear();
      state.focusedOptionId = null;
    }
    syncSelectedFromCart();
    renderAll();
    return details;
  } finally {
    setMapLoading(false);
  }
}

async function findSeatOptions(input, signal) {
  const eventId = input.eventId || state.details?.event.id;
  if (!eventId) throw new Error('Choose an event before searching for seats.');
  if (state.details?.event.id !== eventId) await getEventDetails(eventId, signal);

  const result = await apiFetch(`/api/events/${encodeURIComponent(eventId)}/seat-options`, {
    method: 'POST',
    signal,
    body: {
      quantity: Number(input.quantity),
      maxTotalBudget: input.maxTotalBudget == null ? null : Number(input.maxTotalBudget),
      zonePreference: input.zonePreference || 'any',
      preference: input.preference || 'center',
      preferAisle: Boolean(input.preferAisle),
      requireAccessiblePair: Boolean(input.requireAccessiblePair),
      allowSplitPairs: Boolean(input.allowSplitPairs),
      avoidOrphanSeats: input.avoidOrphanSeats !== false,
    },
  });

  state.lastSearch = {
    eventId,
    quantity: Number(input.quantity),
    maxTotalBudget: input.maxTotalBudget == null ? null : Number(input.maxTotalBudget),
    zonePreference: input.zonePreference || 'any',
    preference: input.preference || 'center',
    preferAisle: Boolean(input.preferAisle),
    requireAccessiblePair: Boolean(input.requireAccessiblePair),
    allowSplitPairs: Boolean(input.allowSplitPairs),
    avoidOrphanSeats: input.avoidOrphanSeats !== false,
  };
  state.options = new Map(result.options.map((option) => [option.optionId, option]));
  state.focusedOptionId = result.options[0]?.optionId ?? null;
  state.highlighted = new Set(result.options[0]?.seatIds ?? []);
  renderOptions();
  renderMap();
  updateJudgeControls();
  if (result.options.length === 0) showToast(result.message);
  return result;
}

async function highlightSeats({ eventId, seatIds }, signal) {
  if (!Array.isArray(seatIds) || seatIds.length === 0 || seatIds.length > 8) {
    throw new Error('Provide between 1 and 8 seat_ids to highlight.');
  }
  if (state.details?.event.id !== eventId) await getEventDetails(eventId, signal);

  const knownSeats = new Set(state.details.seats.map((seat) => seat.id));
  const unknown = seatIds.filter((id) => !knownSeats.has(id));
  if (unknown.length) throw new Error(`These seats do not exist on this map: ${unknown.join(', ')}`);

  state.highlighted = new Set(seatIds);
  const matchingOption = [...state.options.values()]
    .find((option) => option.seatIds.length === seatIds.length &&
      option.seatIds.every((id) => state.highlighted.has(id)));
  state.focusedOptionId = matchingOption?.optionId ?? null;
  renderOptions();
  renderMap();
  elements['seat-map-wrapper'].scrollIntoView({ behavior: 'smooth', block: 'center' });
  return {
    eventId,
    highlightedSeatIds: seatIds,
    message: `${seatIds.length} seats highlighted on the visible map.`,
  };
}

async function selectSeatOption(optionId, signal) {
  const option = state.options.get(optionId);
  if (!option) {
    throw new Error('That option is no longer available on the page. Run find_seat_options again.');
  }

  state.cart = await apiFetch('/api/cart/select', {
    method: 'POST',
    signal,
    body: {
      sessionId: state.sessionId,
      eventId: option.eventId,
      seatIds: option.seatIds,
    },
  });
  state.selected = new Set(option.seatIds);
  state.highlighted = new Set(option.seatIds);
  state.focusedOptionId = optionId;
  await getEventDetails(option.eventId, signal, { keepOptions: true });
  renderCart();
  showToast(`${option.seatLabels.join(', ')} added to your demo selection.`);
  return state.cart;
}

async function holdSelectedSeats(confirmation, signal) {
  if (confirmation !== 'HOLD_SELECTED_SEATS') {
    throw new Error('Explicit confirmation="HOLD_SELECTED_SEATS" is required before creating a demo hold.');
  }
  const cart = await getCartSummary(signal);
  if (!cart.eventId || cart.seats.length === 0) {
    throw new Error('Select an option before holding seats.');
  }

  state.cart = await apiFetch('/api/holds', {
    method: 'POST',
    signal,
    body: {
      sessionId: state.sessionId,
      eventId: cart.eventId,
      seatIds: cart.seats.map((seat) => seat.id),
    },
  });
  state.expiryRefreshPending = false;
  syncSelectedFromCart();
  await getEventDetails(cart.eventId, signal, { keepOptions: true });
  renderCart();
  showToast('Seats are temporarily held. The countdown is now running.');
  return state.cart;
}

async function releaseSeats(signal) {
  const cart = await getCartSummary(signal);
  if (!cart.hold || cart.hold.status !== 'active') {
    throw new Error('There is no active hold to release.');
  }

  state.cart = await apiFetch(
    `/api/holds/${encodeURIComponent(cart.hold.id)}?sessionId=${encodeURIComponent(state.sessionId)}`,
    { method: 'DELETE', signal },
  );
  state.expiryRefreshPending = false;
  syncSelectedFromCart();
  if (state.details) await getEventDetails(state.details.event.id, signal, { keepOptions: true });
  renderCart();
  showToast('The hold was released and the seats are available again.');
  return state.cart;
}

async function releaseHoldFromUi() {
  const cart = await runGuidedTool('release_seats', {}, () => releaseSeats());
  appendConversationMessage(
    'assistant',
    `Your demo hold was released immediately and the seats are available again. ${cartSummary(cart)}`,
  );
  return cart;
}

async function getCartSummary(signal) {
  state.cart = await apiFetch(`/api/cart/${encodeURIComponent(state.sessionId)}`, { signal });
  syncSelectedFromCart();
  renderCart();
  renderMap();
  return state.cart;
}

async function proceedToCheckout(confirmation, signal) {
  if (confirmation !== 'SIMULATE_CHECKOUT') {
    throw new Error('To confirm this action, use confirmation="SIMULATE_CHECKOUT". No real charge will be made.');
  }

  const result = await apiFetch('/api/checkout', {
    method: 'POST',
    signal,
    body: { sessionId: state.sessionId },
  });
  state.expiryRefreshPending = false;
  await getCartSummary(signal);
  if (state.details) await getEventDetails(state.details.event.id, signal, { keepOptions: true });
  showReceipt(result);
  return result;
}

function updateJudgeControls() {
  const option = state.options.get(state.focusedOptionId) || state.options.values().next().value;
  const hasActiveHold = state.cart?.hold?.status === 'active';
  elements['simulate-competitor'].disabled = !option
    || hasActiveHold
    || state.judgeScenario === 'armed';
}

async function resetJudgeScenario({ announce = true } = {}) {
  await apiFetch('/api/demo/session-reset', {
    method: 'POST',
    body: { sessionId: state.sessionId },
  });
  state.options.clear();
  state.highlighted.clear();
  state.selected.clear();
  state.focusedOptionId = null;
  state.pendingRecoveryOptionId = null;
  state.lastSearch = null;
  state.expiryRefreshPending = false;
  await getCartSummary();
  if (state.details) await getEventDetails(state.details.event.id);
  renderAll();
  setJudgeStatus('ready', 'Ready to load a clean scenario.');
  updateJudgeControls();
  if (announce) {
    appendConversationMessage(
      'assistant',
      'Your browser session was reset without touching seed data or another visitor’s session.',
    );
    showToast('Your demo session was reset.');
  }
}

async function loadJudgeScenario() {
  await resetJudgeScenario({ announce: false });
  await getEventDetails('neon-desert-2026');
  const preferences = {
    eventId: 'neon-desert-2026',
    quantity: 4,
    maxTotalBudget: 8_000,
    zonePreference: 'gold',
    preference: 'center',
    preferAisle: true,
    requireAccessiblePair: false,
    allowSplitPairs: true,
    avoidOrphanSeats: true,
  };
  syncPreferenceControls(preferences);
  const result = await runGuidedTool(
    'find_seat_options',
    {
      event_id: preferences.eventId,
      quantity: preferences.quantity,
      max_total_budget: preferences.maxTotalBudget,
      zone_preference: preferences.zonePreference,
      preference: preferences.preference,
      prefer_aisle: preferences.preferAisle,
      require_accessible_pair: preferences.requireAccessiblePair,
      allow_split_pairs: preferences.allowSplitPairs,
      avoid_orphan_seats: preferences.avoidOrphanSeats,
    },
    () => findSeatOptions(preferences),
  );
  if (!result.options.length) throw new Error('The judge scenario could not find an initial option. Reset the demo and try again.');
  setJudgeStatus('loaded', 'Constraints loaded. The current recommendation is ready for a live availability change.');
  updateJudgeControls();
  appendConversationMessage(
    'assistant',
    `Judge scenario loaded. ${optionSummary(result.options[0])} Copy the agent prompt, then simulate a competing buyer before approving the option.`,
  );
}

async function simulateCompetingBuyer() {
  const option = state.options.get(state.focusedOptionId) || state.options.values().next().value;
  if (!option) throw new Error('Load the judge scenario or find seats before simulating another buyer.');
  const result = await runGuidedTool(
    'simulate_competing_buyer',
    { event_id: option.eventId, seat_ids: option.seatIds },
    () => apiFetch('/api/demo/competing-hold', {
      method: 'POST',
      body: {
        sessionId: state.sessionId,
        eventId: option.eventId,
        seatIds: option.seatIds,
      },
    }),
  );
  await getEventDetails(option.eventId, undefined, { keepOptions: true });
  state.highlighted = new Set(option.seatIds);
  renderOptions();
  renderMap();
  setJudgeStatus('armed', `${result.seatLabels.join(', ')} was taken by another buyer. The recommendation is now stale.`);
  updateJudgeControls();
  appendConversationMessage(
    'assistant',
    `${result.message} The old option remains highlighted so the conflict is visible. Ask the browser agent to select or hold it; the structured error will direct a safe recovery.`,
  );
  showToast(`${result.seatLabels.join(', ')} is now held by a simulated competing buyer.`, 'error');
  return result;
}

async function copyJudgePrompt() {
  const prompt = elements['judge-agent-prompt'].textContent.trim();
  try {
    await navigator.clipboard.writeText(prompt);
    showToast('Browser-agent prompt copied.');
  } catch {
    elements['concierge-input'].value = prompt;
    elements['concierge-input'].focus();
    showToast('The prompt was placed in the concierge input for manual copying.');
  }
}

async function toggleSeat(seat) {
  if (state.cart?.hold?.status === 'active') {
    showToast('Release the active hold before changing your selection.', 'error');
    return;
  }
  if (seat.status !== 'available' && seat.status !== 'held_by_you') return;

  const next = new Set(state.selected);
  if (next.has(seat.id)) next.delete(seat.id);
  else if (next.size < 8) next.add(seat.id);
  else {
    showToast('The demo allows up to 8 selected seats.', 'error');
    return;
  }

  if (next.size === 0) {
    showToast('Choose at least one seat or select a recommendation.', 'error');
    return;
  }

  try {
    state.cart = await apiFetch('/api/cart/select', {
      method: 'POST',
      body: {
        sessionId: state.sessionId,
        eventId: state.details.event.id,
        seatIds: [...next],
      },
    });
    state.selected = next;
    state.highlighted = new Set(next);
    renderMap();
    renderCart();
  } catch (error) {
    showToast(error.message, 'error');
    await getEventDetails(state.details.event.id, undefined, { keepOptions: true });
  }
}

function syncSelectedFromCart() {
  if (state.cart?.eventId && state.cart.eventId === state.details?.event.id) {
    state.selected = new Set(state.cart.seats.map((seat) => seat.id));
  } else {
    state.selected = new Set();
  }
}

function renderAll() {
  renderEvents();
  renderHero();
  renderZones();
  renderMap();
  renderOptions();
  renderCart();
}

function renderEvents() {
  const list = elements['event-list'];
  list.replaceChildren();
  if (state.events.length === 0) {
    const empty = document.createElement('p');
    empty.className = 'cart-empty';
    empty.textContent = 'No demo events found.';
    list.append(empty);
    return;
  }

  for (const event of state.events) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = `event-item${state.details?.event.id === event.id ? ' active' : ''}`;
    button.style.setProperty('--event-accent', event.accentColor);
    button.setAttribute('aria-pressed', String(state.details?.event.id === event.id));

    const title = document.createElement('strong');
    title.textContent = event.name;
    const where = document.createElement('span');
    where.textContent = `${event.venue} · ${event.city}`;
    const when = document.createElement('span');
    when.textContent = dateTime.format(new Date(event.startsAt));
    const price = document.createElement('small');
    price.textContent = `From ${money.format(event.fromPrice)}`;
    button.append(title, where, when, price);
    button.addEventListener('click', () => runUiAction(
      () => getEventDetails(event.id),
      `Could not open ${event.name}.`,
    ));
    list.append(button);
  }
}

function renderHero() {
  const details = state.details;
  if (!details) return;
  elements['hero-city'].textContent = details.event.city;
  elements['hero-title'].textContent = details.event.name;
  elements['hero-tagline'].textContent = details.event.tagline;
  elements['hero-meta'].replaceChildren(
    textSpan(dateTime.format(new Date(details.event.startsAt))),
    textSpan(details.event.venue),
    textSpan(`From ${money.format(details.event.fromPrice)}`),
  );
  elements['map-title'].textContent = `Map · ${details.event.venue}`;
  document.documentElement.style.setProperty('--event-accent', details.event.accentColor);
}

function renderZones() {
  const container = elements['zone-chips'];
  container.replaceChildren();
  if (!state.details) return;
  for (const zone of state.details.zones) {
    const chip = document.createElement('span');
    chip.className = 'zone-chip';
    chip.style.setProperty('--zone-color', zone.color);
    const dot = document.createElement('i');
    const name = document.createElement('span');
    name.textContent = zone.name;
    const price = document.createElement('strong');
    price.textContent = money.format(zone.price);
    chip.append(dot, name, price);
    container.append(chip);
  }
}

function renderMap() {
  const svg = elements['seat-map'];
  svg.replaceChildren();
  if (!state.details) return;

  const zoneTopStart = 78;
  const zoneStep = 205;
  const zoneHeight = 194;
  const seatDisplayYOffset = 30;

  const defs = svgElement('defs');
  const gradient = svgElement('linearGradient', { id: 'stageGradient', x1: '0', y1: '0', x2: '1', y2: '0' });
  gradient.append(
    svgElement('stop', { offset: '0%', 'stop-color': '#261d4b', 'stop-opacity': '0.35' }),
    svgElement('stop', { offset: '50%', 'stop-color': '#8d68ed', 'stop-opacity': '0.42' }),
    svgElement('stop', { offset: '100%', 'stop-color': '#261d4b', 'stop-opacity': '0.35' }),
  );
  defs.append(gradient);
  svg.append(defs);

  svg.append(svgElement('path', {
    d: 'M150 48 Q380 5 610 48 L586 70 Q380 41 174 70 Z',
    class: 'stage-base',
  }));
  const stageText = svgElement('text', { x: '380', y: '47', class: 'stage-text', 'text-anchor': 'middle' });
  stageText.textContent = 'STAGE';
  svg.append(stageText);
  svg.append(svgElement('path', { d: 'M190 75 Q380 51 570 75', class: 'stage-line' }));

  for (const zone of state.details.zones) {
    const top = zoneTopStart + ((zone.sortOrder - 1) * zoneStep);
    svg.append(svgElement('rect', {
      x: '58', y: String(top), width: '644', height: String(zoneHeight), rx: '18', class: 'zone-backdrop',
    }));
    svg.append(svgElement('rect', {
      x: '69',
      y: String(top + 8),
      width: '238',
      height: '25',
      rx: '12.5',
      class: 'zone-heading-bg',
      fill: zone.color,
      'fill-opacity': '0.1',
      stroke: zone.color,
      'stroke-opacity': '0.38',
    }));
    svg.append(svgElement('circle', {
      cx: '81', cy: String(top + 20.5), r: '3.5', class: 'zone-heading-dot', fill: zone.color,
    }));

    const zoneText = svgElement('text', { x: '91', y: String(top + 24), class: 'zone-name' });
    const zoneName = svgElement('tspan', { class: 'zone-name-label' });
    zoneName.textContent = zone.name.toUpperCase();
    const zonePrice = svgElement('tspan', { dx: '8', class: 'zone-price' });
    zonePrice.textContent = money.format(zone.price);
    zoneText.append(zoneName, zonePrice);
    svg.append(zoneText);
  }

  const seenRows = new Set();
  for (const seat of state.details.seats) {
    const displayY = seat.y + seatDisplayYOffset;
    const rowKey = `${seat.zoneCode}:${seat.row}`;
    if (!seenRows.has(rowKey)) {
      const rowText = svgElement('text', { x: '74', y: String(displayY + 3), class: 'row-label', 'text-anchor': 'middle' });
      rowText.textContent = seat.row;
      svg.append(rowText);
      seenRows.add(rowKey);
    }

    const classes = ['seat-group', seat.status];
    if (state.selected.has(seat.id)) classes.push('selected');
    if (state.highlighted.has(seat.id)) classes.push('highlighted');
    const group = svgElement('g', {
      transform: `translate(${seat.x} ${displayY})`,
      class: classes.join(' '),
      tabindex: seat.status === 'available' || seat.status === 'held_by_you' ? '0' : '-1',
      role: 'button',
      'aria-label': `${seat.zoneName}, row ${seat.row}, seat ${seat.number}, ${seatStatusLabel(seat.status)}, ${money.format(seat.price)}`,
      'aria-pressed': String(state.selected.has(seat.id)),
    });
    if (seat.accessible) group.append(svgElement('circle', { r: '13', class: 'accessible-ring' }));
    group.append(svgElement('rect', { x: '-9', y: '-8', width: '18', height: '15', rx: '4', class: 'seat-shape' }));
    const number = svgElement('text', { y: '2', class: 'seat-number' });
    number.textContent = String(seat.number);
    group.append(number);
    const title = svgElement('title');
    title.textContent = `${seat.zoneName} · ${seat.label} · ${money.format(seat.price)} · ${seatStatusLabel(seat.status)}`;
    group.append(title);
    group.addEventListener('click', () => toggleSeat(seat));
    group.addEventListener('keydown', (event) => {
      if (event.key === 'Enter' || event.key === ' ') {
        event.preventDefault();
        toggleSeat(seat);
      }
    });
    svg.append(group);
  }

  const available = state.details.seats.filter((seat) => seat.status === 'available').length;
  elements['map-availability'].textContent = `${available} seats available now`;
}

function renderOptions() {
  const container = elements['seat-options'];
  const options = [...state.options.values()];
  container.replaceChildren();
  elements['options-count'].textContent = String(options.length);
  elements['options-empty'].classList.toggle('hidden', options.length > 0);
  if (options.length === 0) return;

  const zoneColors = new Map(state.details.zones.map((zone) => [zone.code, zone.color]));
  const seatStatuses = new Map(state.details.seats.map((seat) => [seat.id, seat.status]));
  options.forEach((option, index) => {
    const card = document.createElement('article');
    const invalidatedSeats = option.seatIds.filter((seatId) =>
      !['available', 'held_by_you'].includes(seatStatuses.get(seatId)));
    const invalidated = invalidatedSeats.length > 0;
    card.className = [
      'seat-option',
      state.focusedOptionId === option.optionId ? 'focused' : '',
      invalidated ? 'invalidated' : '',
    ].filter(Boolean).join(' ');

    const top = document.createElement('div');
    top.className = 'option-top';
    const rank = document.createElement('span');
    rank.className = 'option-rank';
    rank.style.setProperty('--zone-color', zoneColors.get(option.zoneCode) || '#9d7bff');
    const dot = document.createElement('i');
    rank.append(dot, document.createTextNode(`#${index + 1} · ${option.zoneName}, row ${option.row}`));
    const match = document.createElement('span');
    match.className = 'option-match';
    match.textContent = invalidated
      ? 'AVAILABILITY CHANGED'
      : `${option.matchScore}% ${index === 0 ? 'BEST FIT' : 'FIT'}`;
    top.append(rank, match);

    const labels = document.createElement('div');
    labels.className = 'option-labels';
    option.seatLabels.forEach((label) => {
      const seat = document.createElement('span');
      seat.textContent = label;
      labels.append(seat);
    });

    const reason = document.createElement('p');
    reason.className = 'option-reason';
    reason.textContent = invalidated
      ? `${invalidatedSeats.length} seat${invalidatedSeats.length === 1 ? '' : 's'} changed after this recommendation. Refresh alternatives before selecting.`
      : option.reason;

    const evidence = document.createElement('div');
    evidence.className = 'option-evidence';
    const addEvidence = (text, kind = '') => {
      const chip = document.createElement('span');
      chip.className = `evidence-chip ${kind}`.trim();
      chip.textContent = text;
      evidence.append(chip);
    };
    const breakdown = option.scoreBreakdown ?? {};
    addEvidence(`Center offset ${breakdown.centerOffset ?? '—'}`);
    if (breakdown.preferredZoneMatched) addEvidence('Zone matched', 'positive');
    if (breakdown.includesAisle) addEvidence('Aisle access', 'positive');
    if (breakdown.includesAccessibleCompanion) addEvidence('Accessible + companion', 'positive');
    if (!breakdown.leavesOrphanSeat) addEvidence('No orphan seat', 'positive');
    else addEvidence('Leaves orphan seat', 'warning');
    if (option.layout === 'split_2_plus_2') addEvidence('2 + 2 fallback', 'warning');
    if (breakdown.budgetRemaining != null) addEvidence(`${money.format(breakdown.budgetRemaining)} under budget`);

    let tradeoffs = null;
    if (option.tradeoffs?.length) {
      tradeoffs = document.createElement('ul');
      tradeoffs.className = 'option-tradeoffs';
      option.tradeoffs.forEach((tradeoff) => {
        const item = document.createElement('li');
        item.textContent = tradeoff;
        tradeoffs.append(item);
      });
    }

    const price = document.createElement('div');
    price.className = 'option-price';
    const each = document.createElement('small');
    each.textContent = `${money.format(option.pricePerSeat)} per seat`;
    const total = document.createElement('strong');
    total.textContent = money.format(option.totalPrice);
    price.append(each, total);

    const actions = document.createElement('div');
    actions.className = 'option-actions';
    const preview = document.createElement('button');
    preview.type = 'button';
    preview.className = 'preview-option';
    preview.textContent = 'View on map';
    preview.addEventListener('click', () => runUiAction(
      () => highlightSeats({ eventId: option.eventId, seatIds: option.seatIds }),
      'Could not highlight this option.',
    ));
    const select = document.createElement('button');
    select.type = 'button';
    select.className = 'select-option';
    select.textContent = invalidated ? 'Refresh required' : 'Select option';
    select.disabled = invalidated;
    select.addEventListener('click', () => withBusyButton(
      select,
      'Selecting…',
      () => selectSeatOption(option.optionId),
    ));
    actions.append(preview, select);
    card.append(top, labels, reason, evidence);
    if (tradeoffs) card.append(tradeoffs);
    card.append(price, actions);
    container.append(card);
  });
}

function renderCart() {
  const cart = state.cart;
  const hasSeats = Boolean(cart?.seats?.length);
  elements['cart-empty'].classList.toggle('hidden', hasSeats);
  elements['cart-content'].classList.toggle('hidden', !hasSeats);

  if (hasSeats) {
    elements['cart-event'].textContent = cart.eventName;
    elements['cart-seats'].replaceChildren();
    for (const seat of cart.seats) {
      const row = document.createElement('div');
      row.className = 'cart-seat';
      const label = document.createElement('span');
      label.textContent = `${seat.zoneName} · ${seat.label}`;
      const price = document.createElement('span');
      price.textContent = money.format(seat.price);
      row.append(label, price);
      elements['cart-seats'].append(row);
    }
    elements['cart-total'].textContent = money.format(cart.total);
  }

  const activeHold = cart?.hold?.status === 'active' && cart.hold.remainingSeconds > 0;
  elements['hold-badge'].classList.toggle('hidden', !activeHold);
  elements['release-button'].classList.toggle('hidden', !activeHold);
  elements['hold-button'].disabled = !hasSeats || activeHold;
  elements['checkout-button'].disabled = !activeHold;
  elements['hold-button'].textContent = activeHold ? 'Active hold' : 'Hold for 10 minutes';
  updateHoldClock();
  updateJudgeControls();
}

function updateHoldClock() {
  const hold = state.cart?.hold;
  if (!hold || hold.status !== 'active') return;
  const remaining = Math.max(0, Math.ceil((new Date(hold.expiresAt).getTime() - Date.now()) / 1000));
  hold.remainingSeconds = remaining;
  const minutes = Math.floor(remaining / 60).toString().padStart(2, '0');
  const seconds = (remaining % 60).toString().padStart(2, '0');
  elements['hold-badge'].querySelector('span').textContent = `${minutes}:${seconds}`;
  if (remaining === 0 && !state.expiryRefreshPending) {
    state.expiryRefreshPending = true;
    void refreshAfterExpiry();
  }
}

async function refreshAfterExpiry() {
  try {
    await getCartSummary();
    if (state.details) await getEventDetails(state.details.event.id, undefined, { keepOptions: true });
    showToast('The demo hold expired and the seats were released.');
  } catch (error) {
    showToast(error.message, 'error');
  }
}

function showReceipt(result) {
  elements['receipt-message'].textContent = result.message;
  elements['receipt-reference'].textContent = result.reference;
  elements['receipt-total'].textContent = money.format(result.total);
  elements['receipt-dialog'].showModal();
}

function showToast(message, kind = 'info') {
  const toast = document.createElement('div');
  toast.className = `toast ${kind}`;
  toast.textContent = message;
  elements['toast-region'].append(toast);
  window.setTimeout(() => toast.remove(), 4400);
}

function setMapLoading(isLoading) {
  elements['map-loading'].classList.toggle('hidden', !isLoading);
}

function setWebMcpStatus(status, message) {
  const pill = elements['webmcp-status'];
  pill.className = `mcp-pill ${status}`;
  pill.querySelector('span').textContent = message;
}

function textSpan(value) {
  const span = document.createElement('span');
  span.textContent = value;
  return span;
}

function svgElement(name, attributes = {}) {
  const element = document.createElementNS(svgNamespace, name);
  for (const [key, value] of Object.entries(attributes)) element.setAttribute(key, value);
  return element;
}

function seatStatusLabel(status) {
  return {
    available: 'available',
    held_by_you: 'held by you',
    held: 'held',
    blocked: 'unavailable',
    sold: 'demo checkout completed',
  }[status] || status;
}

async function runUiAction(action, fallback) {
  try {
    return await action();
  } catch (error) {
    showToast(error.message || fallback, 'error');
    return undefined;
  }
}

async function withBusyButton(button, busyText, action) {
  const originalText = button.textContent;
  button.disabled = true;
  button.textContent = busyText;
  try {
    await runUiAction(action, 'Could not complete the action.');
  } finally {
    button.textContent = originalText;
    button.disabled = false;
    if (state.cart) renderCart();
    else updateJudgeControls();
  }
}

function bindUi() {
  window.addEventListener('ticketnauta:tool-activity', handleWebMcpActivity);

  elements['concierge-form'].addEventListener('submit', (event) => {
    event.preventDefault();
    void handleConciergeRequest(elements['concierge-input'].value);
  });

  document.querySelectorAll('[data-concierge-prompt]').forEach((button) => {
    button.addEventListener('click', () => {
      void handleConciergeRequest(button.dataset.conciergePrompt);
    });
  });

  elements['event-search-form'].addEventListener('submit', (event) => {
    event.preventDefault();
    runUiAction(
      () => searchEvents(elements['event-search'].value),
      'Could not search demo events.',
    );
  });

  elements['seat-preferences-form'].addEventListener('submit', (event) => {
    event.preventDefault();
    withBusyButton(
      elements['find-seats-button'],
      'Searching…',
      () => findSeatOptions(searchInputFromControls()),
    );
  });

  elements['accessible-pair'].addEventListener('change', () => {
    if (elements['accessible-pair'].checked) {
      elements['seat-priority'].value = 'accessible';
      if (Number(elements['seat-quantity'].value) < 2) elements['seat-quantity'].value = '2';
    }
  });

  elements['copy-judge-prompt'].addEventListener('click', () => void copyJudgePrompt());
  elements['load-judge-scenario'].addEventListener('click', () => withBusyButton(
    elements['load-judge-scenario'],
    'Loading…',
    () => loadJudgeScenario(),
  ));
  elements['simulate-competitor'].addEventListener('click', () => withBusyButton(
    elements['simulate-competitor'],
    'Changing availability…',
    () => simulateCompetingBuyer(),
  ));
  elements['reset-judge-scenario'].addEventListener('click', () => withBusyButton(
    elements['reset-judge-scenario'],
    'Resetting…',
    () => resetJudgeScenario(),
  ));

  elements['hold-button'].addEventListener('click', () => withBusyButton(
    elements['hold-button'],
    'Holding…',
    () => holdSelectedSeats('HOLD_SELECTED_SEATS'),
  ));
  elements['release-button'].addEventListener('click', () => withBusyButton(
    elements['release-button'],
    'Releasing…',
    () => releaseHoldFromUi(),
  ));
  elements['checkout-button'].addEventListener('click', async () => {
    const confirmed = window.confirm('This checkout changes demo data only and never makes a real charge. Continue?');
    if (!confirmed) return;
    await withBusyButton(
      elements['checkout-button'],
      'Simulating…',
      () => proceedToCheckout('SIMULATE_CHECKOUT'),
    );
  });
}

async function initialize() {
  bindUi();
  try {
    await searchEvents('');
    await getCartSummary();
    state.conciergeReady = true;
    updateConciergeControls();
  } catch (error) {
    setMapLoading(false);
    showToast(`The demo could not start: ${error.message}`, 'error');
  }

  const appActions = {
    searchEvents,
    getEventDetails,
    findSeatOptions,
    highlightSeats,
    selectSeatOption,
    holdSelectedSeats,
    releaseSeats,
    getCartSummary,
    proceedToCheckout,
  };
  window.ticketnautaDemo = Object.freeze({
    actions: appActions,
    get sessionId() { return state.sessionId; },
  });

  await registerWebMcpTools(appActions, setWebMcpStatus);
  window.setInterval(updateHoldClock, 1000);
}

initialize();
