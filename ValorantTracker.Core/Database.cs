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
                    State TEXT NOT NULL
                );";
            command.ExecuteNonQuery();
        }

        public void LogEvent(string game, string state)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO GameEvents (Timestamp, Game, State)
                VALUES ($timestamp, $game, $state);";
            command.Parameters.AddWithValue("$timestamp", DateTime.Now.ToString("o"));
            command.Parameters.AddWithValue("$game", game);
            command.Parameters.AddWithValue("$state", state);
            command.ExecuteNonQuery();
        }

        // Returns events from `since` onward, plus one synthetic "carry-in" row at exactly
        // `since` representing whatever state was active right before the window started.
        // Without this, we wouldn't know what was happening in the first few seconds/minutes
        // of the window (e.g. a match that started yesterday and is still going).
        public List<(DateTime Timestamp, string State)> GetEventsSince(string game, DateTime since)
        {
            var results = new List<(DateTime, string)>();

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var carryInCommand = connection.CreateCommand();
            carryInCommand.CommandText = @"
                SELECT State FROM GameEvents
                WHERE Game = $game AND Timestamp < $since
                ORDER BY Timestamp DESC
                LIMIT 1;";
            carryInCommand.Parameters.AddWithValue("$game", game);
            carryInCommand.Parameters.AddWithValue("$since", since.ToString("o"));

            if (carryInCommand.ExecuteScalar() is string carryInState)
                results.Add((since, carryInState));

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Timestamp, State FROM GameEvents
                WHERE Game = $game AND Timestamp >= $since
                ORDER BY Timestamp ASC;";
            command.Parameters.AddWithValue("$game", game);
            command.Parameters.AddWithValue("$since", since.ToString("o"));

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var timestamp = DateTime.Parse(reader.GetString(0));
                var state = reader.GetString(1);
                results.Add((timestamp, state));
            }

            return results;
        }
    }
}
