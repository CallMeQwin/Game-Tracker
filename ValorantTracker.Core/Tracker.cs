using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ValorantTracker.Core
{
    public class Tracker
    {
        private const string Game = "Valorant";
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

        private readonly Database _database;
        private ValorantClient? _client;
        private string _lastState = "CLOSED";

        public Tracker(Database database)
        {
            _database = database;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var currentState = await PollStateAsync();

                if (currentState != _lastState)
                {
                    _database.LogEvent(Game, currentState);
                    _lastState = currentState;
                }

                await Task.Delay(PollInterval, cancellationToken);
            }
        }

        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ValorantTracker", "debug.log");

        private async Task<string> PollStateAsync()
        {
            try
            {
                _client ??= new ValorantClient();
                var state = await _client.GetGameStateAsync();
                Log($"Polled state: {state}");
                return state ?? "CLOSED";
            }
            catch (Exception ex)
            {
                // Riot Client not running, lockfile missing, or Valorant not open.
                Log($"Poll failed: {ex}");
                _client = null;
                return "CLOSED";
            }
        }

        private static void Log(string message)
        {
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
        }
    }
}
