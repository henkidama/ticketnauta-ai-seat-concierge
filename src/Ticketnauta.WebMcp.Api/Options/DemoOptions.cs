namespace Ticketnauta.WebMcp.Options;

public sealed class DemoOptions
{
    public const string SectionName = "Demo";

    public int HoldMinutes { get; init; } = 10;
    public string AdminToken { get; init; } = string.Empty;
}
