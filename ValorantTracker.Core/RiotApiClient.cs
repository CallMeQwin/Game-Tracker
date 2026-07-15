using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ValorantTracker.Core
{
    public record MatchDetails(
        string MatchId,
        DateTime StartTimeUtc,
        string? QueueId,
        string MapId,
        string? AgentId,
        bool? Won,
        bool Draw,
        int RoundsWon,
        int RoundsLost,
        int Kills,
        int Deaths,
        int Assists,
        int Score,
        int RoundsPlayed);

    // Talks to Riot's player-data ("pd") server for match history, match details and
    // rank-rating updates. This is a different surface than the chat/presence API that
    // ValorantClient uses: it lives on a regional server (not localhost) and wants an
    // OAuth access token + entitlement token, which the local Riot Client hands out via
    // /entitlements/v1/token. These endpoints are undocumented but are what every
    // community tracker (tracker.gg etc.) is built on.
    public class RiotApiClient : IDisposable
    {
        private static readonly string LockfilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Riot Games", "Riot Client", "Config", "lockfile");

        private static readonly string ShooterGameLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VALORANT", "Saved", "Logs", "ShooterGame.log");

        // The pd server rejects requests without a client platform blob. The contents
        // are not validated against the actual machine; this fixed value is what the
        // community tooling has always sent.
        private static readonly string ClientPlatform = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            "{\"platformType\":\"PC\",\"platformOS\":\"Windows\",\"platformOSVersion\":\"10.0.19042.1.256.64bit\",\"platformChipset\":\"Unknown\"}"));

        private readonly HttpClient _localClient;
        private readonly HttpClient _pdClient;
        private readonly int _port;

        private string? _puuid;
        private string? _shard;

        public RiotApiClient()
        {
            if (!File.Exists(LockfilePath))
                throw new InvalidOperationException("Riot Client lockfile not found. Is Riot Client running?");

            // Lockfile format: name:PID:port:password:protocol
            // Riot Client holds this file open exclusively, so we must explicitly allow shared access to read it.
            string lockfileContent;
            using (var stream = new FileStream(LockfilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                lockfileContent = reader.ReadToEnd();
            }

            var parts = lockfileContent.Split(':');
            _port = int.Parse(parts[2]);
            var password = parts[3];

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => true
            };
            _localClient = new HttpClient(handler);

            var authBytes = Encoding.UTF8.GetBytes($"riot:{password}");
            _localClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

            _pdClient = new HttpClient();
        }

        // Tokens expire after ~an hour, so a RiotApiClient is meant to be constructed,
        // initialized and used for one sync run, not kept around.
        public async Task InitializeAsync()
        {
            var response = await _localClient.GetStringAsync($"https://127.0.0.1:{_port}/entitlements/v1/token");
            using var doc = JsonDocument.Parse(response);

            var accessToken = doc.RootElement.GetProperty("accessToken").GetString()!;
            var entitlementToken = doc.RootElement.GetProperty("token").GetString()!;
            _puuid = doc.RootElement.GetProperty("subject").GetString()!;

            var (shard, clientVersion) = ParseShooterGameLog();
            _shard = shard;

            _pdClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            _pdClient.DefaultRequestHeaders.Add("X-Riot-Entitlements-JWT", entitlementToken);
            _pdClient.DefaultRequestHeaders.Add("X-Riot-ClientPlatform", ClientPlatform);
            _pdClient.DefaultRequestHeaders.Add("X-Riot-ClientVersion", clientVersion);
        }

        // The shard (which regional pd server to talk to) and the exact client version
        // (required header) both appear in Valorant's own log file. Parsing them from
        // there keeps us self-contained — no third-party version APIs. The log exists
        // as soon as Valorant has been launched once on this machine.
        private static (string Shard, string ClientVersion) ParseShooterGameLog()
        {
            if (!File.Exists(ShooterGameLogPath))
                throw new InvalidOperationException("ShooterGame.log not found. Has Valorant been run on this machine?");

            string log;
            using (var stream = new FileStream(ShooterGameLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                log = reader.ReadToEnd();
            }

            var shardMatch = Regex.Match(log, @"https://glz-[a-z-]+-1\.([a-z]+)\.a\.pvp\.net");
            if (!shardMatch.Success)
                throw new InvalidOperationException("Could not find pd shard in ShooterGame.log.");

            var versionMatch = Regex.Match(log, @"CI server version: (.+)");
            if (!versionMatch.Success)
                throw new InvalidOperationException("Could not find client version in ShooterGame.log.");

            return (shardMatch.Groups[1].Value, versionMatch.Groups[1].Value.Trim());
        }

        public async Task<List<string>> GetRecentMatchIdsAsync(int count)
        {
            var url = $"https://pd.{_shard}.a.pvp.net/match-history/v1/history/{_puuid}?startIndex=0&endIndex={count}";
            var response = await _pdClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);

            var ids = new List<string>();
            foreach (var entry in doc.RootElement.GetProperty("History").EnumerateArray())
                ids.Add(entry.GetProperty("MatchID").GetString()!);
            return ids;
        }

        // Returns rank-rating deltas for recent competitive matches, keyed by match id.
        public async Task<Dictionary<string, int>> GetCompetitiveUpdatesAsync(int count)
        {
            var url = $"https://pd.{_shard}.a.pvp.net/mmr/v1/players/{_puuid}/competitiveupdates?startIndex=0&endIndex={count}&queue=competitive";
            var response = await _pdClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);

            var updates = new Dictionary<string, int>();
            foreach (var entry in doc.RootElement.GetProperty("Matches").EnumerateArray())
            {
                var matchId = entry.GetProperty("MatchID").GetString()!;
                updates[matchId] = entry.GetProperty("RankedRatingEarned").GetInt32();
            }
            return updates;
        }

        public async Task<MatchDetails?> GetMatchDetailsAsync(string matchId)
        {
            var url = $"https://pd.{_shard}.a.pvp.net/match-details/v1/matches/{matchId}";
            var response = await _pdClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            var matchInfo = root.GetProperty("matchInfo");
            var startTimeUtc = DateTimeOffset
                .FromUnixTimeMilliseconds(matchInfo.GetProperty("gameStartMillis").GetInt64())
                .UtcDateTime;
            var queueId = matchInfo.TryGetProperty("queueID", out var queueProp) ? queueProp.GetString() : null;
            var mapId = matchInfo.GetProperty("mapId").GetString()!;

            string? agentId = null;
            string? teamId = null;
            int kills = 0, deaths = 0, assists = 0, score = 0, roundsPlayed = 0;

            foreach (var player in root.GetProperty("players").EnumerateArray())
            {
                if (player.GetProperty("subject").GetString() != _puuid)
                    continue;

                agentId = player.TryGetProperty("characterId", out var charProp) ? charProp.GetString() : null;
                teamId = player.TryGetProperty("teamId", out var teamProp) ? teamProp.GetString() : null;

                var stats = player.GetProperty("stats");
                kills = stats.GetProperty("kills").GetInt32();
                deaths = stats.GetProperty("deaths").GetInt32();
                assists = stats.GetProperty("assists").GetInt32();
                score = stats.GetProperty("score").GetInt32();
                roundsPlayed = stats.GetProperty("roundsPlayed").GetInt32();
                break;
            }

            // Player not in this match's roster (shouldn't happen for own history) — skip it.
            if (teamId == null && agentId == null && roundsPlayed == 0)
                return null;

            // Round-based modes have two teams (Blue/Red); in deathmatch every player is
            // their own "team" and win/loss isn't meaningful, so Won stays null there.
            bool? won = null;
            var draw = false;
            int roundsWon = 0, roundsLost = 0;

            if (root.TryGetProperty("teams", out var teams) && teams.ValueKind == JsonValueKind.Array &&
                teams.GetArrayLength() == 2 && teamId != null)
            {
                foreach (var team in teams.EnumerateArray())
                {
                    var isOwn = team.GetProperty("teamId").GetString() == teamId;
                    var teamRoundsWon = team.GetProperty("roundsWon").GetInt32();

                    if (isOwn)
                    {
                        won = team.GetProperty("won").GetBoolean();
                        roundsWon = teamRoundsWon;
                    }
                    else
                    {
                        roundsLost = teamRoundsWon;
                    }
                }

                draw = won == false && roundsWon == roundsLost;
                if (draw)
                    won = null;
            }

            return new MatchDetails(matchId, startTimeUtc, queueId, mapId, agentId,
                won, draw, roundsWon, roundsLost, kills, deaths, assists, score, roundsPlayed);
        }

        public void Dispose()
        {
            _localClient.Dispose();
            _pdClient.Dispose();
        }
    }
}
