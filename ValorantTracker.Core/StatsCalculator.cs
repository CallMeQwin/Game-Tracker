using System;
using System.Collections.Generic;
using System.Linq;

namespace ValorantTracker.Core
{
    public record DayPlaytime(DateTime Day, TimeSpan Playtime, int Matches);

    public static class StatsCalculator
    {
        public static TimeSpan CalculatePlaytime(List<Match> matches, DateTime now) =>
            matches.Aggregate(TimeSpan.Zero, (sum, match) => sum + ((match.End ?? now) - match.Start));

        // Buckets matches by the day they started on (a match spanning midnight counts
        // entirely toward its start day — matches are short enough that this is a
        // non-issue in practice) and fills in zero-playtime days so the range has no gaps.
        public static List<DayPlaytime> CalculateDailyBreakdown(List<Match> matches, DateTime rangeStart, DateTime rangeEndExclusive, DateTime now)
        {
            var byDay = matches
                .GroupBy(match => match.Start.Date)
                .ToDictionary(
                    group => group.Key,
                    group => new DayPlaytime(group.Key, CalculatePlaytime(group.ToList(), now), group.Count()));

            var breakdown = new List<DayPlaytime>();
            for (var day = rangeStart; day < rangeEndExclusive; day = day.AddDays(1))
                breakdown.Add(byDay.TryGetValue(day, out var dayStats) ? dayStats : new DayPlaytime(day, TimeSpan.Zero, 0));

            return breakdown;
        }
    }
}
