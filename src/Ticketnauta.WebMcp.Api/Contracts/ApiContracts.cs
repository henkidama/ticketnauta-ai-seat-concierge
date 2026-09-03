namespace Ticketnauta.WebMcp.Contracts;

public sealed record EventSummary(
    string Id,
    string Name,
    string Venue,
    string City,
    DateTimeOffset StartsAt,
    string Tagline,
    decimal FromPrice,
    int AvailableSeats,
    string AccentColor);

public sealed record ZoneView(
    string Code,
    string Name,
    decimal Price,
    string Color,
    int SortOrder,
    int AvailableSeats,
    int TotalSeats);

public sealed record SeatView(
    string Id,
    string Label,
    string ZoneCode,
    string ZoneName,
    string Row,
    int Number,
    decimal Price,
    int X,
    int Y,
    bool Accessible,
    string Status);

public sealed record EventDetails(
    EventSummary Event,
    string Description,
    IReadOnlyList<ZoneView> Zones,
    IReadOnlyList<SeatView> Seats);

public sealed record FindSeatOptionsRequest(
    int Quantity,
    decimal? MaxTotalBudget,
    string? ZonePreference,
    string? Preference,
    bool PreferAisle = false,
    bool RequireAccessiblePair = false,
    bool AllowSplitPairs = false,
    bool AvoidOrphanSeats = true);

public sealed record SeatCandidate(
    string Id,
    string ZoneCode,
    string ZoneName,
    string Row,
    int Number,
    decimal Price,
    int ZoneSortOrder,
    bool Accessible,
    int SeatsPerRow);

public sealed record SeatOption(
    string OptionId,
    string EventId,
    string ZoneCode,
    string ZoneName,
    string Row,
    IReadOnlyList<string> SeatIds,
    IReadOnlyList<string> SeatLabels,
    decimal PricePerSeat,
    decimal TotalPrice,
    string Reason,
    double Score,
    string Layout,
    int MatchScore,
    SeatScoreBreakdown ScoreBreakdown,
    IReadOnlyList<string> Tradeoffs);

public sealed record SeatScoreBreakdown(
    double CenterOffset,
    bool PreferredZoneMatched,
    bool IncludesAisle,
    bool IncludesAccessibleCompanion,
    bool LeavesOrphanSeat,
    decimal? BudgetRemaining);

public sealed record SelectSeatOptionRequest(
    string SessionId,
    string EventId,
    IReadOnlyList<string> SeatIds);

public sealed record HoldSeatsRequest(
    string SessionId,
    string EventId,
    IReadOnlyList<string> SeatIds);

public sealed record HoldView(
    Guid Id,
    DateTimeOffset ExpiresAt,
    string Status,
    int RemainingSeconds);

public sealed record CartSeat(
    string Id,
    string Label,
    string ZoneName,
    decimal Price);

public sealed record CartSummary(
    string SessionId,
    string? EventId,
    string? EventName,
    IReadOnlyList<CartSeat> Seats,
    decimal Total,
    HoldView? Hold);

public sealed record CheckoutRequest(string SessionId);

public sealed record CheckoutResult(
    string Reference,
    string EventId,
    string EventName,
    IReadOnlyList<CartSeat> Seats,
    decimal Total,
    DateTimeOffset CompletedAt,
    string Message);

public sealed record DemoResetRequest(string Confirmation);

public sealed record DemoSessionResetRequest(string SessionId);

public sealed record SimulateCompetingBuyerRequest(
    string SessionId,
    string EventId,
    IReadOnlyList<string> SeatIds);

public sealed record DemoDisruptionResult(
    Guid HoldId,
    string EventId,
    IReadOnlyList<string> SeatIds,
    IReadOnlyList<string> SeatLabels,
    DateTimeOffset ExpiresAt,
    string Message);
