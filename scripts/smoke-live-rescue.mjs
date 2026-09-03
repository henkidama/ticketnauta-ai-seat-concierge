import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';

const baseUrl = (process.argv[2] || process.env.DEMO_BASE_URL || 'http://localhost:8085').replace(/\/$/, '');
const sessionId = randomUUID();
const eventId = 'neon-desert-2026';

async function request(path, { method = 'GET', body, expected = 200 } = {}) {
  const response = await fetch(`${baseUrl}${path}`, {
    method,
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  });
  let payload = null;
  const text = await response.text();
  if (text) payload = JSON.parse(text);
  assert.equal(
    response.status,
    expected,
    `${method} ${path} returned ${response.status}: ${text}`,
  );
  return payload;
}

const search = {
  quantity: 4,
  maxTotalBudget: 8_000,
  zonePreference: 'gold',
  preference: 'center',
  preferAisle: true,
  requireAccessiblePair: false,
  allowSplitPairs: true,
  avoidOrphanSeats: true,
};

try {
  await request('/health/ready');
  await request('/api/demo/session-reset', {
    method: 'POST',
    body: { sessionId },
  });

  const initial = await request(`/api/events/${eventId}/seat-options`, {
    method: 'POST',
    body: search,
  });
  assert.ok(initial.options.length > 0, 'The judge scenario needs an initial recommendation.');
  const staleOption = initial.options[0];
  assert.equal(staleOption.zoneCode, 'gold');
  assert.ok(staleOption.matchScore >= 0 && staleOption.matchScore <= 100);
  assert.equal(typeof staleOption.scoreBreakdown.includesAisle, 'boolean');
  assert.ok(Array.isArray(staleOption.tradeoffs));

  await request('/api/cart/select', {
    method: 'POST',
    body: { sessionId, eventId, seatIds: staleOption.seatIds },
  });

  const disruption = await request('/api/demo/competing-hold', {
    method: 'POST',
    expected: 201,
    body: { sessionId, eventId, seatIds: staleOption.seatIds },
  });
  assert.equal(disruption.seatIds.length, 1);

  const conflict = await request('/api/holds', {
    method: 'POST',
    expected: 409,
    body: { sessionId, eventId, seatIds: staleOption.seatIds },
  });
  assert.equal(conflict.code, 'seat_conflict');
  assert.deepEqual(conflict.unavailableSeatIds, disruption.seatIds);
  assert.equal(conflict.recovery.nextTool, 'find_seat_options');

  const recovered = await request(`/api/events/${eventId}/seat-options`, {
    method: 'POST',
    body: search,
  });
  assert.ok(recovered.options.length > 0, 'A replacement should be available after the conflict.');
  const replacement = recovered.options[0];
  assert.equal(
    replacement.seatIds.some((seatId) => disruption.seatIds.includes(seatId)),
    false,
    'The replacement must exclude the seat held by the competing buyer.',
  );

  await request('/api/cart/select', {
    method: 'POST',
    body: { sessionId, eventId, seatIds: replacement.seatIds },
  });
  const heldCart = await request('/api/holds', {
    method: 'POST',
    expected: 201,
    body: { sessionId, eventId, seatIds: replacement.seatIds },
  });
  assert.equal(heldCart.hold.status, 'active');
  assert.ok(heldCart.hold.remainingSeconds > 0);

  await request(`/api/holds/${heldCart.hold.id}?sessionId=${encodeURIComponent(sessionId)}`, {
    method: 'DELETE',
  });

  console.log('Live Seat Rescue smoke test passed: stale option conflicted, structured recovery succeeded, and the replacement hold was released.');
} finally {
  try {
    await request('/api/demo/session-reset', {
      method: 'POST',
      body: { sessionId },
    });
  } catch {
    // Keep the original test failure when the target is unavailable.
  }
}
