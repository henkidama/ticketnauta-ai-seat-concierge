using Ticketnauta.WebMcp.Contracts;
using Ticketnauta.WebMcp.Domain;
using Xunit;

namespace Ticketnauta.WebMcp.Tests;

public sealed class SeatOptionFinderTests
{
    [Fact]
    public void Find_ReturnsOnlyTrulyContiguousBlocks()
    {
        var candidates = new[]
        {
            Seat("gold", "Gold", "A", 1, 900m, 2),
            Seat("gold", "Gold", "A", 2, 900m, 2),
            Seat("gold", "Gold", "A", 4, 900m, 2),
            Seat("gold", "Gold", "A", 5, 900m, 2),
        };

        var result = SeatOptionFinder.Find(
            "demo",
            candidates,
            new FindSeatOptionsRequest(3, null, "any", "center"));

        Assert.Empty(result);
    }

    [Fact]
    public void Find_FiltersByTotalBudget()
    {
        var candidates = Enumerable.Range(1, 4)
            .Select(number => Seat("diamond", "Diamond", "A", number, 1_000m, 1))
            .Concat(Enumerable.Range(1, 4)
                .Select(number => Seat("general", "General Admission", "B", number, 300m, 4)))
            .ToArray();

        var result = SeatOptionFinder.Find(
            "demo",
            candidates,
            new FindSeatOptionsRequest(2, 700m, "any", "center"));

        Assert.NotEmpty(result);
        Assert.All(result, option =>
        {
            Assert.Equal("general", option.ZoneCode);
            Assert.True(option.TotalPrice <= 700m);
        });
    }

    [Fact]
    public void Find_PrioritizesRequestedZoneWhileKeepingAlternatives()
    {
        var candidates = Enumerable.Range(1, 5)
            .Select(number => Seat("diamond", "Diamond", "A", number, 1_500m, 1))
            .Concat(Enumerable.Range(1, 5)
                .Select(number => Seat("preferred", "Preferred", "A", number, 700m, 3)))
            .ToArray();

        var result = SeatOptionFinder.Find(
            "demo",
            candidates,
            new FindSeatOptionsRequest(2, null, "preferred", "closest_to_stage"));

        Assert.NotEmpty(result);
        Assert.Equal("preferred", result[0].ZoneCode);
        Assert.Contains(result, option => option.ZoneCode == "diamond");
    }

    [Fact]
    public void Find_AccessiblePreferenceRequiresAnAccessibleSeat()
    {
        var candidates = new[]
        {
            Seat("general", "General Admission", "E", 1, 450m, 4, accessible: true),
            Seat("general", "General Admission", "E", 2, 450m, 4, accessible: true),
            Seat("general", "General Admission", "E", 3, 450m, 4),
            Seat("general", "General Admission", "E", 4, 450m, 4),
        };

        var result = SeatOptionFinder.Find(
            "demo",
            candidates,
            new FindSeatOptionsRequest(2, null, "any", "accessible"));

        Assert.NotEmpty(result);
        Assert.All(result, option =>
            Assert.Contains(option.SeatIds, id => id.EndsWith(":01") || id.EndsWith(":02")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void Find_RejectsUnsupportedQuantities(int quantity)
    {
        var exception = Assert.Throws<DemoValidationException>(() => SeatOptionFinder.Find(
            "demo",
            [],
            new FindSeatOptionsRequest(quantity, null, "any", "center")));

        Assert.Equal("validation_error", exception.ErrorCode);
    }

    private static SeatCandidate Seat(
        string zoneCode,
        string zoneName,
        string row,
        int number,
        decimal price,
        int zoneSortOrder,
        bool accessible = false) =>
        new(
            $"demo:{zoneCode}:{row}:{number:00}",
            zoneCode,
            zoneName,
            row,
            number,
            price,
            zoneSortOrder,
            accessible,
            12);
}
