using System;
using System.Collections.Generic;

namespace ValorantTracker.Core
{
    public static class StatusFormatter
    {
        private static readonly Dictionary<string, string> ModeNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["unrated"] = "Unrated",
            ["competitive"] = "Competitive",
            ["deathmatch"] = "Deathmatch",
            ["spikerush"] = "Spike Rush",
            ["swiftplay"] = "Swift Play",
            ["premier"] = "Premier",
            ["hurm"] = "Team Deathmatch",
            ["onefa"] = "Replication",
        };

        public static string Format(string state, string? mode)
        {
            return state switch
            {
                "CLOSED" => "Not running",
                "MENUS" => "In Menu",
                "PREGAME" => "Agent Select",
                "INGAME" => string.IsNullOrEmpty(mode)
                    ? "In Match (The Range / Custom Game)"
                    : $"Playing: {FormatMode(mode)}",
                _ => state
            };
        }

        private static string FormatMode(string mode) =>
            ModeNames.TryGetValue(mode, out var friendly) ? friendly : mode;
    }
}
