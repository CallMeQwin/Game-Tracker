using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace ValorantTracker.Core
{
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
    }
}
