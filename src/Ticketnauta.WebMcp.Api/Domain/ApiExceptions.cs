namespace Ticketnauta.WebMcp.Domain;

public abstract class ApiException(string message) : Exception(message)
{
    public abstract int StatusCode { get; }
    public abstract string ErrorCode { get; }
}

public sealed class ResourceNotFoundException(string message) : ApiException(message)
{
    public override int StatusCode => StatusCodes.Status404NotFound;
    public override string ErrorCode => "not_found";
}

public sealed class DemoConflictException(string message) : ApiException(message)
{
    public override int StatusCode => StatusCodes.Status409Conflict;
    public override string ErrorCode => "conflict";
}

public sealed class SeatConflictException(
    IReadOnlyList<string> unavailableSeatIds,
    string message = "One or more selected seats are no longer available. Refresh availability and ask the user to approve a replacement option.")
    : ApiException(message)
{
    public override int StatusCode => StatusCodes.Status409Conflict;
    public override string ErrorCode => "seat_conflict";
    public IReadOnlyList<string> UnavailableSeatIds { get; } = unavailableSeatIds;
}

public sealed class DemoValidationException(string message) : ApiException(message)
{
    public override int StatusCode => StatusCodes.Status400BadRequest;
    public override string ErrorCode => "validation_error";
}
