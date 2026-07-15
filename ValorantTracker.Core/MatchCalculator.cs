using System;
using System.Collections.Generic;

namespace ValorantTracker.Core
{
    public record Match(DateTime Start, DateTime? End, string? Mode);

    public static class MatchCalculator
    {
        // A match runs from the first INGAME event until the next non-INGAME event.
        // Consecutive INGAME rows (e.g. the queue/mode value populating a poll cycle
        // after the state already flipped to INGAME) are merged into the same match
        // rather than starting a new one, using the latest non-empty mode seen.
        //
        // If the match is still open when the event list runs out, `windowEnd` decides
        // what "still going" means: pass null for a live, unbounded query (the match is
        // genuinely still in progress right now: End = null). Pass the query's upper
        // bound for a bounded window (e.g. a past day) so a match that merely outlasted
        // the window gets closed at the window edge instead of being reported as
        // open-ended forever.
        public static List<Match> Calculate(List<(DateTime Timestamp, string State, string? Mode)> events, DateTime? windowEnd = null)
        {
            var matches = new List<Match>();

            DateTime? matchStart = null;
            string? matchMode = null;

            foreach (var (timestamp, state, mode) in events)
            {
                if (state == "INGAME")
                {
                    matchStart ??= timestamp;
                    if (!string.IsNullOrEmpty(mode))
                        matchMode = mode;
                    continue;
                }

                if (matchStart.HasValue)
                {
                    matches.Add(new Match(matchStart.Value, timestamp, matchMode));
                    matchStart = null;
                    matchMode = null;
                }
            }

            if (matchStart.HasValue)
                matches.Add(new Match(matchStart.Value, windowEnd, matchMode));

            return matches;
        }
    }
}
