using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ValorantTracker.Core
{
    public class ValorantClient
    {
        private readonly HttpClient _httpClient;
        private readonly int _port;
        private readonly string _puuid;

        public ValorantClient()
        {
            var lockfilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Riot Games", "Riot Client", "Config", "lockfile");

            if (!File.Exists(lockfilePath))
                throw new InvalidOperationException("Riot Client lockfile not found. Is Riot Client running?");

            // Lockfile format: name:PID:port:password:protocol
            // Riot Client holds this file open exclusively, so we must explicitly allow shared access to read it.
            string lockfileContent;
            using (var stream = new FileStream(lockfilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
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
            _httpClient = new HttpClient(handler);

            var authBytes = Encoding.UTF8.GetBytes($"riot:{password}");
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

            _puuid = GetOwnPuuid();
        }

        private string GetOwnPuuid()
        {
            var response = _httpClient.GetStringAsync($"https://127.0.0.1:{_port}/chat/v1/session").Result;
            using var doc = JsonDocument.Parse(response);
            return doc.RootElement.GetProperty("puuid").GetString()!;
        }

        // Returns "MENUS", "PREGAME", "INGAME", or null if Valorant isn't running/no data yet.
        public async Task<string?> GetGameStateAsync()
        {
            var response = await _httpClient.GetStringAsync($"https://127.0.0.1:{_port}/chat/v4/presences");
            using var doc = JsonDocument.Parse(response);

            foreach (var presence in doc.RootElement.GetProperty("presences").EnumerateArray())
            {
                if (presence.GetProperty("puuid").GetString() != _puuid)
                    continue;

                if (presence.GetProperty("product").GetString() != "valorant")
                    continue;

                if (!presence.TryGetProperty("private", out var privateProp))
                    return null;

                var privateB64 = privateProp.GetString();
                if (string.IsNullOrEmpty(privateB64))
                    return null;

                var privateJson = Encoding.UTF8.GetString(Convert.FromBase64String(privateB64));
                using var privateDoc = JsonDocument.Parse(privateJson);

                if (privateDoc.RootElement.TryGetProperty("matchPresenceData", out var matchData) &&
                    matchData.TryGetProperty("sessionLoopState", out var state))
                {
                    return state.GetString();
                }
            }

            return null;
        }
    }
}
