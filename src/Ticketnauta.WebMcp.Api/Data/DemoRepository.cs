using System.Data;
using Npgsql;
using NpgsqlTypes;
using Ticketnauta.WebMcp.Contracts;
using Ticketnauta.WebMcp.Domain;
using Ticketnauta.WebMcp.Options;

namespace Ticketnauta.WebMcp.Data;

public sealed class DemoRepository(
    NpgsqlDataSource dataSource,
    DemoOptions options,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<EventSummary>> SearchEventsAsync(
        string? query,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                e.id,
                e.name,
                e.venue,
                e.city,
                e.starts_at,
                e.tagline,
                COALESCE(MIN(z.price), 0) AS from_price,
                COUNT(DISTINCT s.id) FILTER (
                    WHERE NOT s.is_seed_blocked
                      AND NOT s.is_sold
                      AND (s.hold_id IS NULL OR s.hold_expires_at <= now())
                )::integer AS available_seats,
                e.accent_color
            FROM webmcp_demo.events e
            LEFT JOIN webmcp_demo.zones z ON z.event_id = e.id
            LEFT JOIN webmcp_demo.seats s ON s.event_id = e.id
            WHERE @query = ''
               OR e.name ILIKE '%' || @query || '%'
               OR e.venue ILIKE '%' || @query || '%'
               OR e.city ILIKE '%' || @query || '%'
            GROUP BY e.id
            ORDER BY e.is_featured DESC, e.starts_at
            LIMIT 12;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("query", query?.Trim() ?? string.Empty);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var events = new List<EventSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(ReadEventSummary(reader));
        }

        return events;
    }

    public async Task<EventDetails> GetEventDetailsAsync(
        string eventId,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var (summary, description) = await ReadEventAsync(connection, eventId, cancellationToken);
        var zones = await ReadZonesAsync(connection, eventId, cancellationToken);
        var seats = await ReadSeatsAsync(connection, eventId, sessionId, cancellationToken);
        return new EventDetails(summary, description, zones, seats);
    }

    public async Task<IReadOnlyList<SeatOption>> FindSeatOptionsAsync(
        string eventId,
        FindSeatOptionsRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                s.id,
                s.zone_code,
                z.name,
                s.row_label,
                s.seat_number,
                z.price,
                z.sort_order,
                s.is_accessible
            FROM webmcp_demo.seats s
            JOIN webmcp_demo.zones z
              ON z.event_id = s.event_id AND z.code = s.zone_code
            WHERE s.event_id = @eventId
              AND NOT s.is_seed_blocked
              AND NOT s.is_sold
              AND (s.hold_id IS NULL OR s.hold_expires_at <= now())
            ORDER BY z.sort_order, s.row_label, s.seat_number;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("eventId", eventId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var candidates = new List<SeatCandidate>();

        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(new SeatCandidate(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetDecimal(5),
                reader.GetInt32(6),
                reader.GetBoolean(7),
                12));
        }

        if (candidates.Count == 0)
        {
            await EnsureEventExistsAsync(eventId, cancellationToken);
        }

        return SeatOptionFinder.Find(eventId, candidates, request);
    }

    public async Task<CartSummary> SelectSeatOptionAsync(
        SelectSeatOptionRequest request,
        CancellationToken cancellationToken)
    {
        ValidateSessionAndSeats(request.SessionId, request.SeatIds);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);

        await LockEventAsync(connection, transaction, request.EventId, cancellationToken);
        await LockSessionAsync(connection, transaction, request.SessionId, cancellationToken);
        await CleanupExpiredHoldsAsync(connection, transaction, cancellationToken);
        await EnsureSessionHasNoActiveHoldAsync(
            connection, transaction, request.SessionId, cancellationToken);
        await EnsureSeatsAreAvailableAsync(
            connection,
            transaction,
            request.EventId,
            request.SessionId,
            request.SeatIds,
            cancellationToken);
        await SetCartAsync(
            connection,
            transaction,
            request.SessionId,
            request.EventId,
            request.SeatIds,
            null,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return await GetCartSummaryAsync(request.SessionId, cancellationToken);
    }

    public async Task<CartSummary> HoldSeatsAsync(
        HoldSeatsRequest request,
        CancellationToken cancellationToken)
    {
        ValidateSessionAndSeats(request.SessionId, request.SeatIds);
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(Math.Clamp(options.HoldMinutes, 1, 30));
        var holdId = Guid.NewGuid();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);

        await LockEventAsync(connection, transaction, request.EventId, cancellationToken);
        await LockSessionAsync(connection, transaction, request.SessionId, cancellationToken);
        await CleanupExpiredHoldsAsync(connection, transaction, cancellationToken);
        await EnsureSessionHasNoActiveHoldAsync(
            connection, transaction, request.SessionId, cancellationToken);
        await EnsureSeatsAreAvailableAsync(
            connection,
            transaction,
            request.EventId,
            request.SessionId,
            request.SeatIds,
            cancellationToken);

        const string insertHoldSql = """
            INSERT INTO webmcp_demo.holds
                (id, session_id, event_id, status, created_at, expires_at)
            VALUES (@id, @sessionId, @eventId, 'active', @now, @expiresAt);
            """;
        await using (var command = new NpgsqlCommand(insertHoldSql, connection, transaction))
        {
            command.Parameters.AddWithValue("id", holdId);
            command.Parameters.AddWithValue("sessionId", request.SessionId);
            command.Parameters.AddWithValue("eventId", request.EventId);
            command.Parameters.AddWithValue("now", now);
            command.Parameters.AddWithValue("expiresAt", expiresAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        const string insertItemsSql = """
            INSERT INTO webmcp_demo.hold_items (hold_id, seat_id)
            SELECT @holdId, unnest(@seatIds::text[]);
            """;
        await using (var command = new NpgsqlCommand(insertItemsSql, connection, transaction))
        {
            command.Parameters.AddWithValue("holdId", holdId);
            AddSeatIds(command, request.SeatIds);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        const string updateSeatsSql = """
            UPDATE webmcp_demo.seats
            SET held_by_session = @sessionId,
                hold_id = @holdId,
                hold_expires_at = @expiresAt
            WHERE event_id = @eventId
              AND id = ANY(@seatIds::text[]);
            """;
        await using (var command = new NpgsqlCommand(updateSeatsSql, connection, transaction))
        {
            command.Parameters.AddWithValue("sessionId", request.SessionId);
            command.Parameters.AddWithValue("holdId", holdId);
            command.Parameters.AddWithValue("expiresAt", expiresAt);
            command.Parameters.AddWithValue("eventId", request.EventId);
            AddSeatIds(command, request.SeatIds);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await SetCartAsync(
            connection,
            transaction,
            request.SessionId,
            request.EventId,
            request.SeatIds,
            holdId,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return await GetCartSummaryAsync(request.SessionId, cancellationToken);
    }

    public async Task<CartSummary> ReleaseHoldAsync(
        Guid holdId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        ValidateSession(sessionId);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        const string findSql = """
            SELECT event_id
            FROM webmcp_demo.holds
            WHERE id = @holdId AND session_id = @sessionId AND status = 'active'
            FOR UPDATE;
            """;
        string? eventId;
        await using (var command = new NpgsqlCommand(findSql, connection, transaction))
        {
            command.Parameters.AddWithValue("holdId", holdId);
            command.Parameters.AddWithValue("sessionId", sessionId);
            eventId = (string?)await command.ExecuteScalarAsync(cancellationToken);
        }

        if (eventId is null)
        {
            throw new ResourceNotFoundException("No active hold belonging to this demo session was found.");
        }

        await LockEventAsync(connection, transaction, eventId, cancellationToken);

        const string releaseSql = """
            UPDATE webmcp_demo.holds
            SET status = 'released', released_at = now()
            WHERE id = @holdId AND status = 'active';

            UPDATE webmcp_demo.seats
            SET held_by_session = NULL, hold_id = NULL, hold_expires_at = NULL
            WHERE hold_id = @holdId;

            UPDATE webmcp_demo.carts
            SET hold_id = NULL, updated_at = now()
            WHERE session_id = @sessionId AND hold_id = @holdId;
            """;
        await using (var command = new NpgsqlCommand(releaseSql, connection, transaction))
        {
            command.Parameters.AddWithValue("holdId", holdId);
            command.Parameters.AddWithValue("sessionId", sessionId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetCartSummaryAsync(sessionId, cancellationToken);
    }

    public async Task<CartSummary> GetCartSummaryAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        ValidateSession(sessionId);
        const string cartSql = """
            SELECT
                c.event_id,
                e.name,
                h.id,
                h.expires_at,
                CASE
                    WHEN h.status = 'active' AND h.expires_at <= now() THEN 'expired'
                    ELSE h.status
                END AS hold_status
            FROM webmcp_demo.carts c
            JOIN webmcp_demo.events e ON e.id = c.event_id
            LEFT JOIN webmcp_demo.holds h ON h.id = c.hold_id
            WHERE c.session_id = @sessionId;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        string? eventId = null;
        string? eventName = null;
        HoldView? hold = null;

        await using (var command = new NpgsqlCommand(cartSql, connection))
        {
            command.Parameters.AddWithValue("sessionId", sessionId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                eventId = reader.GetString(0);
                eventName = reader.GetString(1);
                if (!reader.IsDBNull(2))
                {
                    var expiresAt = ReadTimestamp(reader, 3);
                    var status = reader.GetString(4);
                    var remaining = status == "active"
                        ? Math.Max(0, (int)Math.Ceiling((expiresAt - timeProvider.GetUtcNow()).TotalSeconds))
                        : 0;
                    hold = new HoldView(reader.GetGuid(2), expiresAt, status, remaining);
                }
            }
        }

        if (eventId is null)
        {
            return EmptyCart(sessionId);
        }

        const string itemsSql = """
            SELECT s.id, s.row_label || s.seat_number::text, z.name, z.price
            FROM webmcp_demo.cart_items ci
            JOIN webmcp_demo.seats s ON s.id = ci.seat_id
            JOIN webmcp_demo.zones z
              ON z.event_id = s.event_id AND z.code = s.zone_code
            WHERE ci.session_id = @sessionId
            ORDER BY z.sort_order, s.row_label, s.seat_number;
            """;
        var seats = new List<CartSeat>();
        await using (var command = new NpgsqlCommand(itemsSql, connection))
        {
            command.Parameters.AddWithValue("sessionId", sessionId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                seats.Add(new CartSeat(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetDecimal(3)));
            }
        }

        return new CartSummary(
            sessionId,
            eventId,
            eventName,
            seats,
            seats.Sum(seat => seat.Price),
            hold);
    }

    public async Task<CheckoutResult> CheckoutAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        ValidateSession(sessionId);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await CleanupExpiredHoldsAsync(connection, transaction, cancellationToken);

        const string contextSql = """
            SELECT c.event_id, e.name, h.id, h.expires_at
            FROM webmcp_demo.carts c
            JOIN webmcp_demo.events e ON e.id = c.event_id
            JOIN webmcp_demo.holds h ON h.id = c.hold_id
            WHERE c.session_id = @sessionId
              AND h.session_id = @sessionId
              AND h.status = 'active'
              AND h.expires_at > now()
            FOR UPDATE OF h;
            """;

        string? eventId = null;
        string? eventName = null;
        Guid holdId = default;
        await using (var command = new NpgsqlCommand(contextSql, connection, transaction))
        {
            command.Parameters.AddWithValue("sessionId", sessionId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                eventId = reader.GetString(0);
                eventName = reader.GetString(1);
                holdId = reader.GetGuid(2);
            }
        }

        if (eventId is null || eventName is null)
        {
            throw new DemoConflictException("An active, unexpired hold is required before simulated checkout.");
        }

        await LockEventAsync(connection, transaction, eventId, cancellationToken);
        var seats = await ReadHoldSeatsAsync(connection, transaction, holdId, cancellationToken);
        if (seats.Count == 0)
        {
            throw new DemoConflictException("The active hold does not contain any seats.");
        }

        var total = seats.Sum(seat => seat.Price);
        var checkoutId = Guid.NewGuid();
        var reference = $"DEMO-{checkoutId.ToString("N")[..8].ToUpperInvariant()}";
        var completedAt = timeProvider.GetUtcNow();

        const string checkoutSql = """
            UPDATE webmcp_demo.seats
            SET is_sold = true,
                held_by_session = NULL,
                hold_id = NULL,
                hold_expires_at = NULL
            WHERE hold_id = @holdId;

            UPDATE webmcp_demo.holds
            SET status = 'checked_out', checked_out_at = @completedAt
            WHERE id = @holdId;

            UPDATE webmcp_demo.carts
            SET hold_id = NULL, updated_at = @completedAt
            WHERE session_id = @sessionId;

            INSERT INTO webmcp_demo.checkouts
                (id, reference, session_id, event_id, total, created_at)
            VALUES (@checkoutId, @reference, @sessionId, @eventId, @total, @completedAt);
            """;
        await using (var command = new NpgsqlCommand(checkoutSql, connection, transaction))
        {
            command.Parameters.AddWithValue("holdId", holdId);
            command.Parameters.AddWithValue("completedAt", completedAt);
            command.Parameters.AddWithValue("sessionId", sessionId);
            command.Parameters.AddWithValue("checkoutId", checkoutId);
            command.Parameters.AddWithValue("reference", reference);
            command.Parameters.AddWithValue("eventId", eventId);
            command.Parameters.AddWithValue("total", total);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new CheckoutResult(
            reference,
            eventId,
            eventName,
            seats,
            total,
            completedAt,
            "Simulated checkout complete. No charge was made and no real ticket was issued.");
    }

    public async Task<DemoDisruptionResult> SimulateCompetingBuyerAsync(
        SimulateCompetingBuyerRequest request,
        CancellationToken cancellationToken)
    {
        ValidateSessionAndSeats(request.SessionId, request.SeatIds);
        await EnsureEventExistsAsync(request.EventId, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddSeconds(90);
        var holdId = Guid.NewGuid();
        var competitorSessionId = $"judge-competitor:{request.SessionId}";
        var middle = request.SeatIds.Count / 2;
        var preferredOrder = request.SeatIds
            .Select((seatId, index) => new { SeatId = seatId, Distance = Math.Abs(index - middle) })
            .OrderBy(item => item.Distance)
            .Select(item => item.SeatId)
            .ToArray();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);

        await LockEventAsync(connection, transaction, request.EventId, cancellationToken);
        await LockSessionAsync(connection, transaction, request.SessionId, cancellationToken);
        await CleanupExpiredHoldsAsync(connection, transaction, cancellationToken);

        const string releasePreviousSql = """
            UPDATE webmcp_demo.seats s
            SET held_by_session = NULL, hold_id = NULL, hold_expires_at = NULL
            WHERE s.hold_id IN (
                SELECT h.id
                FROM webmcp_demo.holds h
                WHERE h.session_id = @competitorSessionId AND h.status = 'active'
            );

            UPDATE webmcp_demo.holds
            SET status = 'released', released_at = @now
            WHERE session_id = @competitorSessionId AND status = 'active';
            """;
        await using (var command = new NpgsqlCommand(releasePreviousSql, connection, transaction))
        {
            command.Parameters.AddWithValue("competitorSessionId", competitorSessionId);
            command.Parameters.AddWithValue("now", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        const string chooseSeatSql = """
            SELECT s.id, s.row_label || s.seat_number::text
            FROM unnest(@seatIds::text[]) WITH ORDINALITY requested(id, position)
            JOIN webmcp_demo.seats s ON s.id = requested.id
            WHERE s.event_id = @eventId
              AND NOT s.is_seed_blocked
              AND NOT s.is_sold
              AND (s.hold_id IS NULL OR s.hold_expires_at <= now())
            ORDER BY requested.position
            LIMIT 1
            FOR UPDATE OF s;
            """;
        string? seatId = null;
        string? seatLabel = null;
        await using (var command = new NpgsqlCommand(chooseSeatSql, connection, transaction))
        {
            command.Parameters.AddWithValue("eventId", request.EventId);
            AddSeatIds(command, preferredOrder);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                seatId = reader.GetString(0);
                seatLabel = reader.GetString(1);
            }
        }

        if (seatId is null || seatLabel is null)
        {
            throw new SeatConflictException(
                request.SeatIds,
                "The suggested option already changed. Refresh availability before arming the live-conflict scenario.");
        }

        const string createCompetingHoldSql = """
            INSERT INTO webmcp_demo.holds
                (id, session_id, event_id, status, created_at, expires_at)
            VALUES (@holdId, @competitorSessionId, @eventId, 'active', @now, @expiresAt);

            INSERT INTO webmcp_demo.hold_items (hold_id, seat_id)
            VALUES (@holdId, @seatId);

            UPDATE webmcp_demo.seats
            SET held_by_session = @competitorSessionId,
                hold_id = @holdId,
                hold_expires_at = @expiresAt
            WHERE id = @seatId AND event_id = @eventId;
            """;
        await using (var command = new NpgsqlCommand(createCompetingHoldSql, connection, transaction))
        {
            command.Parameters.AddWithValue("holdId", holdId);
            command.Parameters.AddWithValue("competitorSessionId", competitorSessionId);
            command.Parameters.AddWithValue("eventId", request.EventId);
            command.Parameters.AddWithValue("now", now);
            command.Parameters.AddWithValue("expiresAt", expiresAt);
            command.Parameters.AddWithValue("seatId", seatId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new DemoDisruptionResult(
            holdId,
            request.EventId,
            [seatId],
            [seatLabel],
            expiresAt,
            $"A simulated competing buyer temporarily took seat {seatLabel}. The stale recommendation must now be recovered.");
    }

    public async Task ResetSessionDemoAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        ValidateSession(sessionId);
        var competitorSessionId = $"judge-competitor:{sessionId}";

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockSessionAsync(connection, transaction, sessionId, cancellationToken);
        await CleanupExpiredHoldsAsync(connection, transaction, cancellationToken);

        const string sql = """
            UPDATE webmcp_demo.seats s
            SET held_by_session = NULL,
                hold_id = NULL,
                hold_expires_at = NULL
            WHERE s.hold_id IN (
                SELECT h.id
                FROM webmcp_demo.holds h
                WHERE h.session_id = @sessionId OR h.session_id = @competitorSessionId
            );

            UPDATE webmcp_demo.seats s
            SET is_sold = false
            WHERE s.is_sold
              AND s.hold_id IS NULL
              AND s.id IN (
                SELECT hi.seat_id
                FROM webmcp_demo.hold_items hi
                JOIN webmcp_demo.holds h ON h.id = hi.hold_id
                WHERE h.session_id = @sessionId AND h.status = 'checked_out'
              );

            DELETE FROM webmcp_demo.checkouts WHERE session_id = @sessionId;
            DELETE FROM webmcp_demo.cart_items WHERE session_id = @sessionId;
            DELETE FROM webmcp_demo.carts WHERE session_id = @sessionId;

            DELETE FROM webmcp_demo.hold_items hi
            USING webmcp_demo.holds h
            WHERE hi.hold_id = h.id
              AND (h.session_id = @sessionId OR h.session_id = @competitorSessionId);

            DELETE FROM webmcp_demo.holds
            WHERE session_id = @sessionId OR session_id = @competitorSessionId;
            """;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("sessionId", sessionId);
            command.Parameters.AddWithValue("competitorSessionId", competitorSessionId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ResetDemoAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            DELETE FROM webmcp_demo.checkouts;
            DELETE FROM webmcp_demo.cart_items;
            DELETE FROM webmcp_demo.carts;
            DELETE FROM webmcp_demo.hold_items;
            DELETE FROM webmcp_demo.holds;
            UPDATE webmcp_demo.seats
            SET is_sold = false,
                held_by_session = NULL,
                hold_id = NULL,
                hold_expires_at = NULL;
            """;
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var command = dataSource.CreateCommand(
                "SELECT to_regclass('webmcp_demo.events') IS NOT NULL");
            return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<(EventSummary Summary, string Description)> ReadEventAsync(
        NpgsqlConnection connection,
        string eventId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                e.id,
                e.name,
                e.venue,
                e.city,
                e.starts_at,
                e.tagline,
                COALESCE(MIN(z.price), 0),
                COUNT(DISTINCT s.id) FILTER (
                    WHERE NOT s.is_seed_blocked
                      AND NOT s.is_sold
                      AND (s.hold_id IS NULL OR s.hold_expires_at <= now())
                )::integer,
                e.accent_color,
                e.description
            FROM webmcp_demo.events e
            LEFT JOIN webmcp_demo.zones z ON z.event_id = e.id
            LEFT JOIN webmcp_demo.seats s ON s.event_id = e.id
            WHERE e.id = @eventId
            GROUP BY e.id;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("eventId", eventId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new ResourceNotFoundException($"Demo event '{eventId}' was not found.");
        }

        return (ReadEventSummary(reader), reader.GetString(9));
    }

    private static async Task<IReadOnlyList<ZoneView>> ReadZonesAsync(
        NpgsqlConnection connection,
        string eventId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                z.code,
                z.name,
                z.price,
                z.color,
                z.sort_order,
                COUNT(s.id) FILTER (
                    WHERE NOT s.is_seed_blocked
                      AND NOT s.is_sold
                      AND (s.hold_id IS NULL OR s.hold_expires_at <= now())
                )::integer,
                COUNT(s.id)::integer
            FROM webmcp_demo.zones z
            LEFT JOIN webmcp_demo.seats s
              ON s.event_id = z.event_id AND s.zone_code = z.code
            WHERE z.event_id = @eventId
            GROUP BY z.event_id, z.code
            ORDER BY z.sort_order;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("eventId", eventId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var zones = new List<ZoneView>();
        while (await reader.ReadAsync(cancellationToken))
        {
            zones.Add(new ZoneView(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetDecimal(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6)));
        }

        return zones;
    }

    private static async Task<IReadOnlyList<SeatView>> ReadSeatsAsync(
        NpgsqlConnection connection,
        string eventId,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                s.id,
                s.row_label || s.seat_number::text,
                s.zone_code,
                z.name,
                s.row_label,
                s.seat_number,
                z.price,
                s.x,
                s.y,
                s.is_accessible,
                CASE
                    WHEN s.is_seed_blocked THEN 'blocked'
                    WHEN s.is_sold THEN 'sold'
                    WHEN s.hold_id IS NOT NULL AND s.hold_expires_at > now()
                         AND s.held_by_session = @sessionId THEN 'held_by_you'
                    WHEN s.hold_id IS NOT NULL AND s.hold_expires_at > now() THEN 'held'
                    ELSE 'available'
                END AS status
            FROM webmcp_demo.seats s
            JOIN webmcp_demo.zones z
              ON z.event_id = s.event_id AND z.code = s.zone_code
            WHERE s.event_id = @eventId
            ORDER BY z.sort_order, s.row_label, s.seat_number;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("eventId", eventId);
        command.Parameters.AddWithValue("sessionId", sessionId is null ? DBNull.Value : sessionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var seats = new List<SeatView>();
        while (await reader.ReadAsync(cancellationToken))
        {
            seats.Add(new SeatView(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetDecimal(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetBoolean(9),
                reader.GetString(10)));
        }

        return seats;
    }

    private static EventSummary ReadEventSummary(NpgsqlDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        ReadTimestamp(reader, 4),
        reader.GetString(5),
        reader.GetDecimal(6),
        reader.GetInt32(7),
        reader.GetString(8));

    private static DateTimeOffset ReadTimestamp(NpgsqlDataReader reader, int ordinal) =>
        new(reader.GetDateTime(ordinal), TimeSpan.Zero);

    private async Task EnsureEventExistsAsync(string eventId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM webmcp_demo.events WHERE id = @eventId)");
        command.Parameters.AddWithValue("eventId", eventId);
        var exists = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
        if (!exists)
        {
            throw new ResourceNotFoundException($"Demo event '{eventId}' was not found.");
        }
    }

    private static async Task EnsureSeatsAreAvailableAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string eventId,
        string sessionId,
        IReadOnlyList<string> seatIds,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT requested.id
            FROM unnest(@seatIds::text[]) WITH ORDINALITY requested(id, position)
            LEFT JOIN webmcp_demo.seats s
              ON s.id = requested.id AND s.event_id = @eventId
            WHERE s.id IS NULL
               OR s.is_seed_blocked
               OR s.is_sold
               OR (
                    s.hold_id IS NOT NULL
                    AND s.hold_expires_at > now()
                    AND s.held_by_session <> @sessionId
               )
            ORDER BY requested.position;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("eventId", eventId);
        command.Parameters.AddWithValue("sessionId", sessionId);
        AddSeatIds(command, seatIds);
        var unavailableSeatIds = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            unavailableSeatIds.Add(reader.GetString(0));
        }

        if (unavailableSeatIds.Count > 0)
        {
            throw new SeatConflictException(unavailableSeatIds);
        }
    }

    private static async Task SetCartAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sessionId,
        string eventId,
        IReadOnlyList<string> seatIds,
        Guid? holdId,
        CancellationToken cancellationToken)
    {
        const string cartSql = """
            INSERT INTO webmcp_demo.carts (session_id, event_id, hold_id, updated_at)
            VALUES (@sessionId, @eventId, @holdId, now())
            ON CONFLICT (session_id) DO UPDATE SET
                event_id = EXCLUDED.event_id,
                hold_id = EXCLUDED.hold_id,
                updated_at = EXCLUDED.updated_at;

            DELETE FROM webmcp_demo.cart_items WHERE session_id = @sessionId;

            INSERT INTO webmcp_demo.cart_items (session_id, seat_id)
            SELECT @sessionId, unnest(@seatIds::text[]);
            """;
        await using var command = new NpgsqlCommand(cartSql, connection, transaction);
        command.Parameters.AddWithValue("sessionId", sessionId);
        command.Parameters.AddWithValue("eventId", eventId);
        var holdParameter = command.Parameters.Add("holdId", NpgsqlDbType.Uuid);
        holdParameter.Value = holdId is null ? DBNull.Value : holdId.Value;
        AddSeatIds(command, seatIds);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task LockEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string eventId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext(@eventId))", connection, transaction);
        command.Parameters.AddWithValue("eventId", eventId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task LockSessionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sessionId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('session:' || @sessionId))", connection, transaction);
        command.Parameters.AddWithValue("sessionId", sessionId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureSessionHasNoActiveHoldAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sessionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id
            FROM webmcp_demo.holds
            WHERE session_id = @sessionId
              AND status = 'active'
              AND expires_at > now()
            LIMIT 1
            FOR UPDATE;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("sessionId", sessionId);
        var activeHold = await command.ExecuteScalarAsync(cancellationToken);
        if (activeHold is not null)
        {
            throw new DemoConflictException(
                "Release the active hold before changing the selection or creating another hold.");
        }
    }

    private static async Task CleanupExpiredHoldsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE webmcp_demo.holds
            SET status = 'expired', released_at = now()
            WHERE status = 'active' AND expires_at <= now();

            UPDATE webmcp_demo.seats
            SET held_by_session = NULL, hold_id = NULL, hold_expires_at = NULL
            WHERE hold_id IS NOT NULL AND hold_expires_at <= now();

            UPDATE webmcp_demo.carts c
            SET hold_id = NULL, updated_at = now()
            WHERE hold_id IS NOT NULL
              AND NOT EXISTS (
                    SELECT 1 FROM webmcp_demo.holds h
                    WHERE h.id = c.hold_id AND h.status = 'active'
              );
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<CartSeat>> ReadHoldSeatsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid holdId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT s.id, s.row_label || s.seat_number::text, z.name, z.price
            FROM webmcp_demo.hold_items hi
            JOIN webmcp_demo.seats s ON s.id = hi.seat_id
            JOIN webmcp_demo.zones z
              ON z.event_id = s.event_id AND z.code = s.zone_code
            WHERE hi.hold_id = @holdId
            ORDER BY z.sort_order, s.row_label, s.seat_number;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("holdId", holdId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var seats = new List<CartSeat>();
        while (await reader.ReadAsync(cancellationToken))
        {
            seats.Add(new CartSeat(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDecimal(3)));
        }

        return seats;
    }

    private static void ValidateSessionAndSeats(string sessionId, IReadOnlyList<string> seatIds)
    {
        ValidateSession(sessionId);
        if (seatIds.Count is < 1 or > 8)
        {
            throw new DemoValidationException("Choose between 1 and 8 seats.");
        }

        if (seatIds.Any(string.IsNullOrWhiteSpace) ||
            seatIds.Distinct(StringComparer.Ordinal).Count() != seatIds.Count)
        {
            throw new DemoValidationException("Seat IDs must be non-empty and unique.");
        }
    }

    private static void ValidateSession(string sessionId)
    {
        if (!Guid.TryParse(sessionId, out _))
        {
            throw new DemoValidationException("SessionId must be a valid UUID.");
        }
    }

    private static void AddSeatIds(NpgsqlCommand command, IReadOnlyList<string> seatIds) =>
        command.Parameters.AddWithValue(
            "seatIds",
            NpgsqlDbType.Array | NpgsqlDbType.Text,
            seatIds.ToArray());

    private static CartSummary EmptyCart(string sessionId) =>
        new(sessionId, null, null, [], 0m, null);
}
