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

        var requireAccessibleCompanion = request.RequireAccessiblePair || preference == "accessible";
        if (requireAccessibleCompanion && request.Quantity < 2)
        {
            throw new DemoValidationException(
                "An accessible companion option requires at least two seats.");
        }

        var zonePreference = Normalize(request.ZonePreference, "any");
        var options = FindContiguousOptions(
            candidates,
            request,
            preference,
            zonePreference,
            requireAccessibleCompanion);

        if (options.Count == 0 && request.AllowSplitPairs && request.Quantity == 4)
        {
            options.AddRange(FindSplitPairOptions(
                candidates,
                request,
                preference,
                zonePreference,
                requireAccessibleCompanion));
        }

        return options
            .OrderBy(option => option.Score)
            .ThenBy(option => option.Total)
            .Take(maxResults)
            .Select(option => ToSeatOption(eventId, option, request, preference, zonePreference))
            .ToArray();
    }

    private static List<ScoredOption> FindContiguousOptions(
        IReadOnlyCollection<SeatCandidate> candidates,
        FindSeatOptionsRequest request,
        string preference,
        string zonePreference,
        bool requireAccessibleCompanion)
    {
        var options = new List<ScoredOption>();

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

                var includesAccessibleCompanion = IncludesAccessibleCompanion(window);
                if (requireAccessibleCompanion && !includesAccessibleCompanion)
                {
                    continue;
                }

                var includesAisle = IncludesAisle(window);
                var leavesOrphanSeat = LeavesOrphanSeat(seats, index, request.Quantity);
                var centerOffset = CenterOffset(window);
                var score = CalculateScore(
                    window,
                    preference,
                    zonePreference,
                    request,
                    includesAisle,
                    includesAccessibleCompanion,
                    leavesOrphanSeat,
                    splitLayout: false);

                options.Add(new ScoredOption(
                    window,
                    total,
                    score,
                    "contiguous",
                    includesAisle,
                    includesAccessibleCompanion,
                    leavesOrphanSeat,
                    centerOffset));
            }
        }

        return options;
    }

    private static IEnumerable<ScoredOption> FindSplitPairOptions(
        IReadOnlyCollection<SeatCandidate> candidates,
        FindSeatOptionsRequest request,
        string preference,
        string zonePreference,
        bool requireAccessibleCompanion)
    {
        var results = new List<ScoredOption>();

        foreach (var zone in candidates
                     .GroupBy(seat => seat.ZoneCode)
                     .OrderBy(group => group.First().ZoneSortOrder))
        {
            var pairs = new List<PairWindow>();
            foreach (var row in zone.GroupBy(seat => seat.Row).OrderBy(group => group.Key))
            {
                var seats = row.OrderBy(seat => seat.Number).ToArray();
                for (var index = 0; index <= seats.Length - 2; index++)
                {
                    var pair = seats[index..(index + 2)];
                    if (!IsContiguous(pair))
                    {
                        continue;
                    }

                    pairs.Add(new PairWindow(
                        pair,
                        LeavesOrphanSeat(seats, index, 2),
                        IncludesAisle(pair)));
                }
            }

            for (var leftIndex = 0; leftIndex < pairs.Count; leftIndex++)
            {
                for (var rightIndex = leftIndex + 1; rightIndex < pairs.Count; rightIndex++)
                {
                    var left = pairs[leftIndex];
                    var right = pairs[rightIndex];
                    if (left.Seats[0].Row == right.Seats[0].Row ||
                        RowDistance(left.Seats[0].Row, right.Seats[0].Row) != 1)
                    {
                        continue;
                    }

                    var combined = left.Seats
                        .Concat(right.Seats)
                        .OrderBy(seat => seat.Row)
                        .ThenBy(seat => seat.Number)
                        .ToArray();
                    var total = combined.Sum(seat => seat.Price);
                    if (request.MaxTotalBudget is not null && total > request.MaxTotalBudget.Value)
                    {
                        continue;
                    }

                    var includesAccessibleCompanion = IncludesAccessibleCompanion(combined);
                    if (requireAccessibleCompanion && !includesAccessibleCompanion)
                    {
                        continue;
                    }

                    var includesAisle = left.IncludesAisle || right.IncludesAisle;
                    var leavesOrphanSeat = left.LeavesOrphanSeat || right.LeavesOrphanSeat;
                    var centerOffset = CenterOffset(combined);
                    var score = CalculateScore(
                        combined,
                        preference,
                        zonePreference,
                        request,
                        includesAisle,
                        includesAccessibleCompanion,
                        leavesOrphanSeat,
                        splitLayout: true);

                    results.Add(new ScoredOption(
                        combined,
                        total,
                        score,
                        "split_2_plus_2",
                        includesAisle,
                        includesAccessibleCompanion,
                        leavesOrphanSeat,
                        centerOffset));
                }
            }
        }

        return results;
    }

    private static bool IsContiguous(IReadOnlyList<SeatCandidate> seats)
    {
        for (var index = 1; index < seats.Count; index++)
        {
            if (seats[index].Row != seats[index - 1].Row ||
                seats[index].Number != seats[index - 1].Number + 1)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IncludesAisle(IReadOnlyList<SeatCandidate> seats) =>
        seats.Any(seat => seat.Number == 1 || seat.Number == seat.SeatsPerRow);

    private static bool IncludesAccessibleCompanion(IReadOnlyList<SeatCandidate> seats) =>
        seats.Any(seat => seat.Accessible) && seats.Any(seat => !seat.Accessible);

    private static double CenterOffset(IReadOnlyList<SeatCandidate> seats)
    {
        var center = (seats[0].SeatsPerRow + 1) / 2d;
        return Math.Round(Math.Abs(seats.Average(seat => seat.Number) - center), 2);
    }

    private static bool LeavesOrphanSeat(
        IReadOnlyList<SeatCandidate> rowSeats,
        int windowStart,
        int quantity)
    {
        var runStart = windowStart;
        while (runStart > 0 &&
               rowSeats[runStart].Number == rowSeats[runStart - 1].Number + 1)
        {
            runStart--;
        }

        var windowEnd = windowStart + quantity - 1;
        var runEnd = windowEnd;
        while (runEnd < rowSeats.Count - 1 &&
               rowSeats[runEnd + 1].Number == rowSeats[runEnd].Number + 1)
        {
            runEnd++;
        }

        return windowStart - runStart == 1 || runEnd - windowEnd == 1;
    }

    private static int RowDistance(string left, string right)
    {
        if (left.Length == 1 && right.Length == 1)
        {
            return Math.Abs(left[0] - right[0]);
        }

        return string.Equals(left, right, StringComparison.Ordinal) ? 0 : int.MaxValue;
    }

    private static double CalculateScore(
        IReadOnlyList<SeatCandidate> seats,
        string preference,
        string zonePreference,
        FindSeatOptionsRequest request,
        bool includesAisle,
        bool includesAccessibleCompanion,
        bool leavesOrphanSeat,
        bool splitLayout)
    {
        var first = seats[0];
        var centerOffset = CenterOffset(seats);
        var zoneMatchBonus = zonePreference != "any" && first.ZoneCode == zonePreference
            ? -10_000d
            : 0d;
        var accessibleBonus = includesAccessibleCompanion ? -250d : 0d;

        var preferenceScore = preference switch
        {
            "closest_to_stage" => first.ZoneSortOrder * 1_000d + centerOffset * 10d,
            "center" => centerOffset * 1_000d + first.ZoneSortOrder * 25d,
            "best_value" => (double)first.Price + first.ZoneSortOrder * 5d + centerOffset,
            "accessible" => first.ZoneSortOrder * 100d + centerOffset * 10d + accessibleBonus,
            _ => first.ZoneSortOrder * 100d + centerOffset * 10d
        };

        var aislePenalty = request.PreferAisle && !includesAisle ? 10_000d : 0d;
        var orphanPenalty = request.AvoidOrphanSeats && leavesOrphanSeat ? 5_000d : 0d;
        var splitPenalty = splitLayout ? 2_500d : 0d;
        return preferenceScore + zoneMatchBonus + aislePenalty + orphanPenalty + splitPenalty;
    }

    private static SeatOption ToSeatOption(
        string eventId,
        ScoredOption option,
        FindSeatOptionsRequest request,
        string preference,
        string zonePreference)
    {
        var first = option.Seats[0];
        var rows = option.Seats.Select(seat => seat.Row).Distinct().ToArray();
        var rowLabel = string.Join(" + ", rows);
        var labels = option.Seats.Select(seat => $"{seat.Row}{seat.Number}").ToArray();
        var optionId = CreateOptionId(eventId, option.Seats.Select(seat => seat.Id));
        var preferredZoneMatched = zonePreference == "any" || first.ZoneCode == zonePreference;
        decimal? budgetRemaining = request.MaxTotalBudget is null
            ? null
            : request.MaxTotalBudget.Value - option.Total;
        var matchScore = CalculateMatchScore(option, request, preference, preferredZoneMatched);
        var tradeoffs = BuildTradeoffs(option, request, preferredZoneMatched, budgetRemaining);
        var reason = BuildReason(option, preference, zonePreference, preferredZoneMatched, rowLabel);

        return new SeatOption(
            optionId,
            eventId,
            first.ZoneCode,
            first.ZoneName,
            rowLabel,
            option.Seats.Select(seat => seat.Id).ToArray(),
            labels,
            first.Price,
            option.Total,
            reason,
            Math.Round(option.Score, 2),
            option.Layout,
            matchScore,
            new SeatScoreBreakdown(
                option.CenterOffset,
                preferredZoneMatched,
                option.IncludesAisle,
                option.IncludesAccessibleCompanion,
                option.LeavesOrphanSeat,
                budgetRemaining),
            tradeoffs);
    }

    private static int CalculateMatchScore(
        ScoredOption option,
        FindSeatOptionsRequest request,
        string preference,
        bool preferredZoneMatched)
    {
        var score = 100d;
        if (preference == "center") score -= Math.Min(24d, option.CenterOffset * 5d);
        if (preference == "closest_to_stage") score -= (option.Seats[0].ZoneSortOrder - 1) * 7d;
        if (!preferredZoneMatched) score -= 20d;
        if (request.PreferAisle && !option.IncludesAisle) score -= 25d;
        if (request.AvoidOrphanSeats && option.LeavesOrphanSeat) score -= 15d;
        if (option.Layout == "split_2_plus_2") score -= 12d;
        return (int)Math.Clamp(Math.Round(score), 0d, 100d);
    }

    private static IReadOnlyList<string> BuildTradeoffs(
        ScoredOption option,
        FindSeatOptionsRequest request,
        bool preferredZoneMatched,
        decimal? budgetRemaining)
    {
        var tradeoffs = new List<string>();
        if (option.Layout == "split_2_plus_2")
        {
            tradeoffs.Add("The party is split into two pairs in adjacent rows.");
        }

        if (!preferredZoneMatched)
        {
            tradeoffs.Add("This alternative is outside the preferred zone.");
        }

        if (request.PreferAisle && !option.IncludesAisle)
        {
            tradeoffs.Add("This block does not include an aisle seat.");
        }

        if (request.AvoidOrphanSeats && option.LeavesOrphanSeat)
        {
            tradeoffs.Add("Selecting this block would leave one isolated seat for the venue.");
        }

        if (budgetRemaining is >= 0 && budgetRemaining < option.Seats[0].Price)
        {
            tradeoffs.Add("This option uses most of the stated total budget.");
        }

        return tradeoffs;
    }

    private static string BuildReason(
        ScoredOption option,
        string preference,
        string zonePreference,
        bool preferredZoneMatched,
        string rowLabel)
    {
        var first = option.Seats[0];
        if (option.Layout == "split_2_plus_2")
        {
            return $"No four-seat block matched, so this keeps two pairs close together in {first.ZoneName}, rows {rowLabel}.";
        }

        if (option.IncludesAccessibleCompanion && preference == "accessible")
        {
            return $"{first.ZoneName}, row {rowLabel}: an accessible position with an adjacent companion seat.";
        }

        if (option.IncludesAisle)
        {
            return $"{first.ZoneName}, row {rowLabel}: a contiguous block with direct aisle access.";
        }

        return preference switch
        {
            "closest_to_stage" => $"{first.ZoneName}, row {rowLabel}: close to the stage with every seat together.",
            "center" => $"{first.ZoneName}, row {rowLabel}: a centered contiguous block with a {option.CenterOffset:0.##}-seat center offset.",
            "best_value" => $"{first.ZoneName}, row {rowLabel}: the best available balance of location and price.",
            _ when zonePreference != "any" && preferredZoneMatched =>
                $"Matches the {first.ZoneName} zone while keeping every seat contiguous.",
            _ => $"{first.ZoneName}, row {rowLabel}: {option.Seats.Length} contiguous seats available."
        };
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

    private sealed record PairWindow(
        SeatCandidate[] Seats,
        bool LeavesOrphanSeat,
        bool IncludesAisle);

    private sealed record ScoredOption(
        SeatCandidate[] Seats,
        decimal Total,
        double Score,
        string Layout,
        bool IncludesAisle,
        bool IncludesAccessibleCompanion,
        bool LeavesOrphanSeat,
        double CenterOffset);
}
