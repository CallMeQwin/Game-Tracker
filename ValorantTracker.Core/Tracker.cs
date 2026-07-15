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

        // The pd server needs a little time after a match ends before its details and
        // RR update are queryable; syncing immediately often finds nothing.
        private static readonly TimeSpan PostMatchSyncDelay = TimeSpan.FromSeconds(20);

        private readonly Database _database;
        private readonly MatchHistoryService? _matchHistory;
        private ValorantClient? _client;
        private string _lastState = "CLOSED";
        private string? _lastMode;
        private bool _startupSyncDone;

        public Tracker(Database database, MatchHistoryService? matchHistory = null)
        {
            _database = database;
            _matchHistory = matchHistory;
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

                    // A match just ended — pull its stats once the server has them.
                    if (_lastState == "INGAME" && currentState != "INGAME" && _matchHistory != null)
                    {
                        var service = _matchHistory;
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(PostMatchSyncDelay, cancellationToken);
                            await service.SyncAsync();
                        }, cancellationToken);
                    }

                    _lastState = currentState;
                    _lastMode = currentMode;
                }

                // Catch up on matches played while the tracker wasn't running. Once per
                // process lifetime, the first time the Riot Client is seen alive.
                if (!_startupSyncDone && currentState != "CLOSED" && _matchHistory != null)
                {
                    _startupSyncDone = true;
                    _ = Task.Run(() => _matchHistory.SyncAsync(), cancellationToken);
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
