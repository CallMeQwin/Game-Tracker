using System;
using System.Collections.Generic;

namespace ValorantTracker.Core
{
    public record Stats(TimeSpan Active, TimeSpan Idle);

    public static class StatsCalculator
    {
        public static Stats Calculate(List<(DateTime Timestamp, string State, string? Mode)> events)
        {
            var active = TimeSpan.Zero;
            var idle = TimeSpan.Zero;

            for (var i = 0; i < events.Count; i++)
            {
                var (timestamp, state, _) = events[i];
                var end = i + 1 < events.Count ? events[i + 1].Timestamp : DateTime.Now;
                var duration = end - timestamp;

                if (state == "INGAME")
                    active += duration;
                else if (state == "MENUS" || state == "PREGAME")
                    idle += duration;
                // CLOSED contributes to neither.
            }

            return new Stats(active, idle);
        }
    }
}
