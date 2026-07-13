using System;
using System.Collections.Generic;

namespace ValorantTracker.Core
{
    public record Session(DateTime Start, DateTime? End, TimeSpan Active, TimeSpan Idle);

    public static class SessionCalculator
    {
        // Groups events into sessions: a session runs from the first non-CLOSED event
        // after a CLOSED (or the start of the list) until the next CLOSED event.
        // If the game is still open, the last session has End = null ("ongoing").
        public static List<Session> Calculate(List<(DateTime Timestamp, string State)> events)
        {
            var sessions = new List<Session>();

            DateTime? sessionStart = null;
            var active = TimeSpan.Zero;
            var idle = TimeSpan.Zero;

            for (var i = 0; i < events.Count; i++)
            {
                var (timestamp, state) = events[i];
                var end = i + 1 < events.Count ? events[i + 1].Timestamp : DateTime.Now;
                var duration = end - timestamp;

                if (state == "CLOSED")
                {
                    if (sessionStart != null)
                    {
                        sessions.Add(new Session(sessionStart.Value, timestamp, active, idle));
                        sessionStart = null;
                        active = TimeSpan.Zero;
                        idle = TimeSpan.Zero;
                    }
                    continue;
                }

                sessionStart ??= timestamp;

                if (state == "INGAME")
                    active += duration;
                else if (state == "MENUS" || state == "PREGAME")
                    idle += duration;
            }

            if (sessionStart != null)
                sessions.Add(new Session(sessionStart.Value, null, active, idle));

            return sessions;
        }
    }
}
