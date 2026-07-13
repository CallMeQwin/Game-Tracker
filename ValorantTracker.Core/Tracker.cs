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

        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ValorantTracker", "debug.log");

        private readonly Database _database;
        private ValorantClient? _client;
        private string _lastState = "CLOSED";
        private string? _lastMode;

        public Tracker(Database database)
        {
            _database = database;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var (currentState, currentMode) = await PollStateAsync();

                if (currentState != _lastState || currentMode != _lastMode)
                {
                    _database.LogEvent(Game, currentState, currentMode);
                    Log($"State changed: {_lastState}/{_lastMode} -> {currentState}/{currentMode}");
                    _lastState = currentState;
                    _lastMode = currentMode;
                }

                await Task.Delay(PollInterval, cancellationToken);
            }
        }

        private async Task<(string State, string? Mode)> PollStateAsync()
        {
            // Common, expected case: Riot Client just isn't running. Not worth logging.
            if (!ValorantClient.IsRiotClientRunning())
            {
                _client = null;
                return ("CLOSED", null);
            }

            try
            {
                _client ??= new ValorantClient();
                var state = await _client.GetGameStateAsync();
                return state != null ? (state.SessionLoopState, state.QueueId) : ("CLOSED", null);
            }
            catch (Exception ex)
            {
                // Unexpected: Riot Client is running but something else went wrong (auth, network, parsing).
                Log($"Unexpected poll error: {ex.Message}");
                _client = null;
                return ("CLOSED", null);
            }
        }

        private static void Log(string message)
        {
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
        }
    }
}
