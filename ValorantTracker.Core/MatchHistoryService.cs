using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ValorantTracker.Core
{
    // Pulls recent matches from the pd API and stores them locally. Runs opportunistically
    // (on startup and after each match ends); everything already stored is skipped, so a
    // sync is cheap and safe to repeat.
    public class MatchHistoryService
    {
        private const int HistoryDepth = 10;
        private const int RRUpdateDepth = 20;

        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ValorantTracker", "debug.log");

        private readonly Database _database;
        private int _syncRunning;

        public MatchHistoryService(Database database)
        {
            _database = database;
        }

        // Raised after a sync stored or updated at least one match, so the UI can refresh
        // immediately instead of waiting for its next timer tick.
        public event Action? MatchesUpdated;

        public async Task SyncAsync()
        {
            // A match-end transition and the startup sync can race; one sync at a time.
            if (Interlocked.Exchange(ref _syncRunning, 1) == 1)
                return;

            try
            {
                using var client = new RiotApiClient();
                await client.InitializeAsync();

                var rrUpdates = await client.GetCompetitiveUpdatesAsync(RRUpdateDepth);
                var matchIds = await client.GetRecentMatchIdsAsync(HistoryDepth);
                var missingRR = _database.GetMatchIdsMissingRR();

                var changed = false;
                foreach (var matchId in matchIds)
                {
                    if (_database.HasMatch(matchId))
                    {
                        // The RR update sometimes lands later than the match details;
                        // fill it in when it shows up.
                        if (missingRR.Contains(matchId) && rrUpdates.TryGetValue(matchId, out var lateRR))
                        {
                            _database.UpdateMatchRR(matchId, lateRR);
                            changed = true;
                        }
                        continue;
                    }

                    var details = await client.GetMatchDetailsAsync(matchId);
                    if (details == null)
                        continue;

                    int? rrChange = rrUpdates.TryGetValue(matchId, out var rr) ? rr : null;
                    _database.SaveMatch(ToStoredMatch(details, rrChange));
                    changed = true;
                }

                if (changed)
                    MatchesUpdated?.Invoke();
            }
            catch (Exception ex)
            {
                Log($"Match history sync failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _syncRunning, 0);
            }
        }

        private static StoredMatch ToStoredMatch(MatchDetails details, int? rrChange)
        {
            var result = details.Draw ? "Draw"
                : details.Won == true ? "Win"
                : details.Won == false ? "Loss"
                : "—";

            return new StoredMatch(
                details.MatchId,
                details.StartTimeUtc.ToLocalTime(),
                details.QueueId,
                MapName(details.MapId),
                AgentName(details.AgentId),
                result,
                details.RoundsWon,
                details.RoundsLost,
                details.Kills,
                details.Deaths,
                details.Assists,
                details.Score,
                details.RoundsPlayed,
                rrChange);
        }

        // The API identifies maps by asset path (e.g. "/Game/Maps/Duality/Duality") using
        // internal codenames, not the names players know. Unknown codenames (new maps)
        // fall back to the raw codename so nothing breaks when Riot ships a map.
        private static readonly Dictionary<string, string> MapNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Ascent"] = "Ascent",
            ["Duality"] = "Bind",
            ["Bonsai"] = "Split",
            ["Triad"] = "Haven",
            ["Port"] = "Icebox",
            ["Foxtrot"] = "Breeze",
            ["Canyon"] = "Fracture",
            ["Pitt"] = "Pearl",
            ["Jam"] = "Lotus",
            ["Juliett"] = "Sunset",
            ["Infinity"] = "Abyss",
            ["Rook"] = "Corrode",
            ["Range"] = "The Range",
            ["HURM_Alley"] = "District",
            ["HURM_Bowl"] = "Kasbah",
            ["HURM_Yard"] = "Piazza",
            ["HURM_HighTide"] = "Drift",
        };

        private static string MapName(string mapId)
        {
            var codename = mapId.Substring(mapId.LastIndexOf('/') + 1);
            return MapNames.TryGetValue(codename, out var name) ? name : codename;
        }

        // Agents are identified by UUID. Unknown ids (agents released after this table
        // was written) return null and the UI simply omits the agent.
        private static readonly Dictionary<string, string> AgentNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["add6443a-41bd-e414-f6ad-e58d267f4e95"] = "Jett",
            ["f94c3b30-42be-e959-889c-5aa313dba261"] = "Raze",
            ["5f8d3a7f-467b-97f3-062c-13acf203c006"] = "Breach",
            ["8e253930-4c05-31dd-1b6c-968525494517"] = "Omen",
            ["9f0d8ba9-4140-b941-57d3-a7ad57c6b417"] = "Brimstone",
            ["eb93336a-449b-9c1b-0a54-a891f7921d69"] = "Phoenix",
            ["569fdd95-4d10-43ab-ca70-79becc718b46"] = "Sage",
            ["320b2a48-4d9b-a075-30f1-1f93a9b638fa"] = "Sova",
            ["707eab51-4836-f488-046a-cda6bf494859"] = "Viper",
            ["117ed9e3-49f3-6512-3ccf-0cada7e3823b"] = "Cypher",
            ["a3bfb853-43b2-7238-a4f1-ad90e9e46bcc"] = "Reyna",
            ["1e58de9c-4950-5125-93e9-a0aee9f98746"] = "Killjoy",
            ["6f2a04ca-43e0-be17-7f36-b3908627744d"] = "Skye",
            ["7f94d92c-4234-0a36-9646-3a87eb8b5c89"] = "Yoru",
            ["41fb69c1-4189-7b37-f117-bcaf1e96f1bf"] = "Astra",
            ["601dbbe7-43ce-be57-2a40-4abd24953621"] = "KAY/O",
            ["22697a3d-45bf-8dd7-4fec-84a9e28c69d7"] = "Chamber",
            ["bb2a4828-46eb-8cd1-e765-15848195d751"] = "Neon",
            ["dade69b4-4f5a-8528-247b-219e5a1facd6"] = "Fade",
            ["95b78ed7-4637-86d9-7e41-71ba8c293152"] = "Harbor",
            ["e370fa57-4757-3604-3648-499e1f642d3f"] = "Gekko",
            ["cc8b64c8-4b25-4ff9-6e7f-37b4da43d235"] = "Deadlock",
            ["0e38b510-41a8-5780-5e8f-568b2a4f2d6c"] = "Iso",
            ["1dbf2edd-4729-0984-3115-daa5eed44993"] = "Clove",
            ["efba5359-4016-a1e5-7626-b1ae76895940"] = "Vyse",
            ["b444168c-4e35-8076-db47-ef9bf368f384"] = "Tejo",
            ["df1cb487-4902-002e-5c17-d28e83e78588"] = "Waylay",
        };

        private static string? AgentName(string? agentId) =>
            agentId != null && AgentNames.TryGetValue(agentId, out var name) ? name : null;

        private static void Log(string message)
        {
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
        }
    }
}
