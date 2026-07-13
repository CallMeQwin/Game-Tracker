using System;
using System.Collections.Generic;

namespace ValorantTracker.Core
{
    public record Match(DateTime Start, DateTime? End, string? Mode);

    public static class MatchCalculator
    {
        // Every INGAME event is one match: it starts when the state becomes INGAME
        // and ends when the next event fires (back to MENUS, or CLOSED). If it's the
        // last event overall, the match is still ongoing (End = null).
        public static List<Match> Calculate(List<(DateTime Timestamp, string State, string? Mode)> events)
        {
            var matches = new List<Match>();

            for (var i = 0; i < events.Count; i++)
            {
                var (timestamp, state, mode) = events[i];
                if (state != "INGAME")
                    continue;

                var end = i + 1 < events.Count ? events[i + 1].Timestamp : (DateTime?)null;
                matches.Add(new Match(timestamp, end, mode));
            }

            return matches;
        }
    }
}
