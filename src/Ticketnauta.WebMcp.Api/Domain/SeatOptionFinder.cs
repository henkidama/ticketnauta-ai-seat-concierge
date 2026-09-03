using System.Security.Cryptography;
using System.Text;
using Ticketnauta.WebMcp.Contracts;

namespace Ticketnauta.WebMcp.Domain;

public static class SeatOptionFinder
{
    private static readonly HashSet<string> SupportedPreferences =
        ["any", "closest_to_stage", "center", "best_value", "accessible"];

    public static IReadOnlyList<SeatOption> Find(
        string eventId,
        IReadOnlyCollection<SeatCandidate> candidates,
        FindSeatOptionsRequest request,
        int maxResults = 5)
    {
        if (request.Quantity is < 1 or > 8)
        {
            throw new DemoValidationException("Quantity must be between 1 and 8 seats.");
        }

        if (request.MaxTotalBudget is <= 0)
        {
            throw new DemoValidationException("MaxTotalBudget must be greater than zero when provided.");
        }

        var preference = Normalize(request.Preference, "any");
        if (!SupportedPreferences.Contains(preference))
        {
            throw new DemoValidationException(
                "Preference must be any, closest_to_stage, center, best_value, or accessible.");
        }

        var zonePreference = Normalize(request.ZonePreference, "any");
        var windows = new List<ScoredWindow>();

        foreach (var group in candidates
                     .GroupBy(seat => new { seat.ZoneCode, seat.Row })
                     .OrderBy(group => group.First().ZoneSortOrder)
                     .ThenBy(group => group.Key.Row))
        {
            var seats = group.OrderBy(seat => seat.Number).ToArray();
            for (var index = 0; index <= seats.Length - request.Quantity; index++)
            {
                var window = seats[index..(index + request.Quantity)];
                if (!IsContiguous(window))
                {
                    continue;
                }

                var total = window.Sum(seat => seat.Price);
                if (request.MaxTotalBudget is not null && total > request.MaxTotalBudget.Value)
                {
                    continue;
                }

                if (preference == "accessible" && window.All(seat => !seat.Accessible))
                {
                    continue;
                }

                var score = CalculateScore(window, preference, zonePreference);
                windows.Add(new ScoredWindow(window, total, score));
            }
        }

        return windows
            .OrderBy(option => option.Score)
            .ThenBy(option => option.Total)
            .Take(maxResults)
            .Select(option => ToSeatOption(eventId, option, preference, zonePreference))
            .ToArray();
    }

    private static bool IsContiguous(IReadOnlyList<SeatCandidate> seats)
    {
        for (var index = 1; index < seats.Count; index++)
        {
            if (seats[index].Number != seats[index - 1].Number + 1)
            {
                return false;
            }
        }

        return true;
    }

    private static double CalculateScore(
        IReadOnlyList<SeatCandidate> seats,
        string preference,
        string zonePreference)
    {
        var first = seats[0];
        var averageSeat = seats.Average(seat => seat.Number);
        var center = (first.SeatsPerRow + 1) / 2d;
        var centerDistance = Math.Abs(averageSeat - center);
        var zoneMatchBonus = zonePreference != "any" && first.ZoneCode == zonePreference
            ? -10_000d
            : 0d;
        var accessibleBonus = seats.Any(seat => seat.Accessible) ? -250d : 0d;

        var preferenceScore = preference switch
        {
            "closest_to_stage" => first.ZoneSortOrder * 1_000d + centerDistance * 10d,
            "center" => centerDistance * 1_000d + first.ZoneSortOrder * 25d,
            "best_value" => (double)first.Price + first.ZoneSortOrder * 5d + centerDistance,
            "accessible" => first.ZoneSortOrder * 100d + centerDistance * 10d + accessibleBonus,
            _ => first.ZoneSortOrder * 100d + centerDistance * 10d
        };

        return preferenceScore + zoneMatchBonus;
    }

    private static SeatOption ToSeatOption(
        string eventId,
        ScoredWindow option,
        string preference,
        string zonePreference)
    {
        var first = option.Seats[0];
        var last = option.Seats[^1];
        var labels = option.Seats.Select(seat => $"{seat.Row}{seat.Number}").ToArray();
        var optionId = CreateOptionId(eventId, option.Seats.Select(seat => seat.Id));
        var reason = preference switch
        {
            "closest_to_stage" => $"{first.ZoneName}, row {first.Row}: close to the stage with {option.Seats.Length} seats together.",
            "center" => $"{first.ZoneName}, row {first.Row}: centered block from seat {first.Number} to {last.Number}.",
            "best_value" => $"{first.ZoneName}, row {first.Row}: the best available balance of location and price.",
            "accessible" => $"{first.ZoneName}, row {first.Row}: contiguous option that includes an accessible seat.",
            _ when zonePreference != "any" && first.ZoneCode == zonePreference =>
                $"Matches the {first.ZoneName} zone while keeping every seat contiguous.",
            _ => $"{first.ZoneName}, row {first.Row}: {option.Seats.Length} contiguous seats available."
        };

        return new SeatOption(
            optionId,
            eventId,
            first.ZoneCode,
            first.ZoneName,
            first.Row,
            option.Seats.Select(seat => seat.Id).ToArray(),
            labels,
            first.Price,
            option.Total,
            reason,
            Math.Round(option.Score, 2));
    }

    private static string CreateOptionId(string eventId, IEnumerable<string> seatIds)
    {
        var source = $"{eventId}|{string.Join('|', seatIds)}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return $"opt_{Convert.ToHexString(hash)[..12].ToLowerInvariant()}";
    }

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');

    private sealed record ScoredWindow(SeatCandidate[] Seats, decimal Total, double Score);
}
