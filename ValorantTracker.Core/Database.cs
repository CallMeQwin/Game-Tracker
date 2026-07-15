using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace ValorantTracker.Core
{
    // One row per completed match, populated from the Riot pd API after a match ends.
    // RRChange is null for anything that isn't a competitive match.
    public record StoredMatch(
        string MatchId,
        DateTime StartTime,
        string? Queue,
        string Map,
        string? Agent,
        string Result,
        int RoundsWon,
        int RoundsLost,
        int Kills,
        int Deaths,
        int Assists,
        int Score,
        int RoundsPlayed,
        int? RRChange);

    public class Database
    {
        private readonly string _connectionString;

        public Database()
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ValorantTracker");
            Directory.CreateDirectory(folder);

            var dbPath = Path.Combine(folder, "tracker.db");
            _connectionString = $"Data Source={dbPath}";

            Initialize();
        }

        private void Initialize()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS GameEvents (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Timestamp TEXT NOT NULL,
                    Game TEXT NOT NULL,
                    State TEXT NOT NULL,
                    Mode TEXT
                );

                CREATE TABLE IF NOT EXISTS Matches (
                    MatchId TEXT PRIMARY KEY,
                    StartTime TEXT NOT NULL,
                    Queue TEXT,
                    Map TEXT NOT NULL,
                    Agent TEXT,
                    Result TEXT NOT NULL,
                    RoundsWon INTEGER NOT NULL,
                    RoundsLost INTEGER NOT NULL,
                    Kills INTEGER NOT NULL,
                    Deaths INTEGER NOT NULL,
                    Assists INTEGER NOT NULL,
                    Score INTEGER NOT NULL,
                    RoundsPlayed INTEGER NOT NULL,
                    RRChange INTEGER
                );";
            command.ExecuteNonQuery();

            EnsureModeColumnExists(connection);
        }

        // The Mode column was added after the table already existed on some machines
        // (including during development). CREATE TABLE IF NOT EXISTS won't alter an
        // existing table, so we check for the column and add it if missing.
        private static void EnsureModeColumnExists(SqliteConnection connection)
        {
            var checkCommand = connection.CreateCommand();
            checkCommand.CommandText = "PRAGMA table_info(GameEvents);";

            var hasModeColumn = false;
            using (var reader = checkCommand.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), "Mode", StringComparison.OrdinalIgnoreCase))
                    {
                        hasModeColumn = true;
                        break;
                    }
                }
            }

            if (!hasModeColumn)
            {
                var alterCommand = connection.CreateCommand();
                alterCommand.CommandText = "ALTER TABLE GameEvents ADD COLUMN Mode TEXT;";
                alterCommand.ExecuteNonQuery();
            }
        }

        public void LogEvent(string game, string state, string? mode)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO GameEvents (Timestamp, Game, State, Mode)
                VALUES ($timestamp, $game, $state, $mode);";
            command.Parameters.AddWithValue("$timestamp", DateTime.Now.ToString("o"));
            command.Parameters.AddWithValue("$game", game);
            command.Parameters.AddWithValue("$state", state);
            command.Parameters.AddWithValue("$mode", (object?)mode ?? DBNull.Value);
            command.ExecuteNonQuery();
        }

        public (string State, string? Mode)? GetLatestEvent(string game)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT State, Mode FROM GameEvents
                WHERE Game = $game
                ORDER BY Timestamp DESC
                LIMIT 1;";
            command.Parameters.AddWithValue("$game", game);

            using var reader = command.ExecuteReader();
            if (!reader.Read())
                return null;

            var state = reader.GetString(0);
            var mode = reader.IsDBNull(1) ? null : reader.GetString(1);
            return (state, mode);
        }

        // Returns events from `since` onward, plus one synthetic "carry-in" row at exactly
        // `since` representing whatever state was active right before the window started.
        // Without this, we wouldn't know what was happening in the first few seconds/minutes
        // of the window (e.g. a match that started yesterday and is still going).
        public List<(DateTime Timestamp, string State, string? Mode)> GetEventsSince(string game, DateTime since)
        {
            var results = new List<(DateTime, string, string?)>();

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var carryInCommand = connection.CreateCommand();
            carryInCommand.CommandText = @"
                SELECT State, Mode FROM GameEvents
                WHERE Game = $game AND Timestamp < $since
                ORDER BY Timestamp DESC
                LIMIT 1;";
            carryInCommand.Parameters.AddWithValue("$game", game);
            carryInCommand.Parameters.AddWithValue("$since", since.ToString("o"));

            using (var carryInReader = carryInCommand.ExecuteReader())
            {
                if (carryInReader.Read())
                {
                    var state = carryInReader.GetString(0);
                    var mode = carryInReader.IsDBNull(1) ? null : carryInReader.GetString(1);
                    results.Add((since, state, mode));
                }
            }

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Timestamp, State, Mode FROM GameEvents
                WHERE Game = $game AND Timestamp >= $since
                ORDER BY Timestamp ASC;";
            command.Parameters.AddWithValue("$game", game);
            command.Parameters.AddWithValue("$since", since.ToString("o"));

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var timestamp = DateTime.Parse(reader.GetString(0));
                var state = reader.GetString(1);
                var mode = reader.IsDBNull(2) ? null : reader.GetString(2);
                results.Add((timestamp, state, mode));
            }

            return results;
        }

        // Same as GetEventsSince, but also bounds the upper end in SQL instead of leaving
        // it to the caller to filter client-side (which loses the "is this truncated?"
        // information MatchCalculator needs to tell a bounded window from a truly-open one).
        public List<(DateTime Timestamp, string State, string? Mode)> GetEventsInRange(string game, DateTime since, DateTime untilExclusive)
        {
            var results = new List<(DateTime, string, string?)>();

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var carryInCommand = connection.CreateCommand();
            carryInCommand.CommandText = @"
                SELECT State, Mode FROM GameEvents
                WHERE Game = $game AND Timestamp < $since
                ORDER BY Timestamp DESC
                LIMIT 1;";
            carryInCommand.Parameters.AddWithValue("$game", game);
            carryInCommand.Parameters.AddWithValue("$since", since.ToString("o"));

            using (var carryInReader = carryInCommand.ExecuteReader())
            {
                if (carryInReader.Read())
                {
                    var state = carryInReader.GetString(0);
                    var mode = carryInReader.IsDBNull(1) ? null : carryInReader.GetString(1);
                    results.Add((since, state, mode));
                }
            }

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Timestamp, State, Mode FROM GameEvents
                WHERE Game = $game AND Timestamp >= $since AND Timestamp < $until
                ORDER BY Timestamp ASC;";
            command.Parameters.AddWithValue("$game", game);
            command.Parameters.AddWithValue("$since", since.ToString("o"));
            command.Parameters.AddWithValue("$until", untilExclusive.ToString("o"));

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var timestamp = DateTime.Parse(reader.GetString(0));
                var state = reader.GetString(1);
                var mode = reader.IsDBNull(2) ? null : reader.GetString(2);
                results.Add((timestamp, state, mode));
            }

            return results;
        }

        public bool HasMatch(string matchId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM Matches WHERE MatchId = $matchId LIMIT 1;";
            command.Parameters.AddWithValue("$matchId", matchId);

            using var reader = command.ExecuteReader();
            return reader.Read();
        }

        // Upsert rather than insert: the RR update for a match can land on a later sync
        // than the match details did, so a re-sync must be able to fill in RRChange.
        public void SaveMatch(StoredMatch match)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO Matches (MatchId, StartTime, Queue, Map, Agent, Result,
                                     RoundsWon, RoundsLost, Kills, Deaths, Assists, Score, RoundsPlayed, RRChange)
                VALUES ($matchId, $startTime, $queue, $map, $agent, $result,
                        $roundsWon, $roundsLost, $kills, $deaths, $assists, $score, $roundsPlayed, $rrChange)
                ON CONFLICT(MatchId) DO UPDATE SET
                    RRChange = COALESCE(excluded.RRChange, RRChange);";
            command.Parameters.AddWithValue("$matchId", match.MatchId);
            command.Parameters.AddWithValue("$startTime", match.StartTime.ToString("o"));
            command.Parameters.AddWithValue("$queue", (object?)match.Queue ?? DBNull.Value);
            command.Parameters.AddWithValue("$map", match.Map);
            command.Parameters.AddWithValue("$agent", (object?)match.Agent ?? DBNull.Value);
            command.Parameters.AddWithValue("$result", match.Result);
            command.Parameters.AddWithValue("$roundsWon", match.RoundsWon);
            command.Parameters.AddWithValue("$roundsLost", match.RoundsLost);
            command.Parameters.AddWithValue("$kills", match.Kills);
            command.Parameters.AddWithValue("$deaths", match.Deaths);
            command.Parameters.AddWithValue("$assists", match.Assists);
            command.Parameters.AddWithValue("$score", match.Score);
            command.Parameters.AddWithValue("$roundsPlayed", match.RoundsPlayed);
            command.Parameters.AddWithValue("$rrChange", (object?)match.RRChange ?? DBNull.Value);
            command.ExecuteNonQuery();
        }

        // Matches saved before their RR update was available on the server. Only these
        // need re-checking on later syncs; RR never changes once recorded.
        public HashSet<string> GetMatchIdsMissingRR()
        {
            var ids = new HashSet<string>();

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT MatchId FROM Matches WHERE RRChange IS NULL AND Queue = 'competitive';";

            using var reader = command.ExecuteReader();
            while (reader.Read())
                ids.Add(reader.GetString(0));

            return ids;
        }

        public void UpdateMatchRR(string matchId, int rrChange)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "UPDATE Matches SET RRChange = $rrChange WHERE MatchId = $matchId;";
            command.Parameters.AddWithValue("$rrChange", rrChange);
            command.Parameters.AddWithValue("$matchId", matchId);
            command.ExecuteNonQuery();
        }

        public List<StoredMatch> GetRecentMatches(int limit)
        {
            var results = new List<StoredMatch>();

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT MatchId, StartTime, Queue, Map, Agent, Result,
                       RoundsWon, RoundsLost, Kills, Deaths, Assists, Score, RoundsPlayed, RRChange
                FROM Matches
                ORDER BY StartTime DESC
                LIMIT $limit;";
            command.Parameters.AddWithValue("$limit", limit);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new StoredMatch(
                    reader.GetString(0),
                    DateTime.Parse(reader.GetString(1)),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetString(5),
                    reader.GetInt32(6),
                    reader.GetInt32(7),
                    reader.GetInt32(8),
                    reader.GetInt32(9),
                    reader.GetInt32(10),
                    reader.GetInt32(11),
                    reader.GetInt32(12),
                    reader.IsDBNull(13) ? null : reader.GetInt32(13)));
            }

            return results;
        }
    }
}
