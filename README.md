# Valorant Tracker

A Windows tray app that tracks your Valorant playtime and match history — how long you've played today, your trend over the week/month, and a per-match breakdown with results, K/D/A, and RR gained or lost.

## Features

- **Runs quietly in the system tray** — no window until you open it from the tray icon.
- **Overview** — today's total playtime and match count, plus a vertical timeline of the day's matches. Navigate to any past day.
- **Trends** — playtime by day for the current week or month, as a bar chart you can click to jump to a specific day.
- **Match History** — your recent matches grouped by day, each showing mode, map, agent, win/loss with round score, K/D/A, ACS, and RR change (green for gains, red for losses). Each day has a win/loss and net-RR summary.

## How it works

Valorant's Riot Client exposes a local, undocumented HTTPS API (the same one community trackers are built on). This app uses two parts of it:

1. **Presence polling.** It reads the Riot Client `lockfile` (`%LOCALAPPDATA%\Riot Games\Riot Client\Config\lockfile`) for the local API's port and password, then polls your presence every 5 seconds to detect your game state (`MENUS`, `PREGAME`, `INGAME`, `CLOSED`). Each state *change* is logged as an event in a local SQLite database, and playtime is calculated from that event log.

2. **Match sync.** When a match ends, the app requests an access + entitlement token from the local client and queries Riot's player-data (`pd`) servers for that match's details — map, agent, score, K/D/A, and (for competitive) the RR change. Results are stored in a local `Matches` table. A sync also runs once at startup to backfill matches you played while the tracker wasn't running.

## Data & privacy

- Everything is stored locally in SQLite (`%LOCALAPPDATA%\ValorantTracker\tracker.db`). No accounts, no telemetry, no third-party servers.
- The only network requests go to two places: your **own machine** (the Riot Client's local API, over loopback) and **Riot's official servers** (`pd.{region}.a.pvp.net`, over verified HTTPS) to fetch match details.
- The local API password and API tokens are only ever used to talk to those endpoints — they're held in memory for a sync and never written to disk.

## Notes

- This relies on Riot's **unofficial** local/pd API. It's read-only and widely used by community tools, but it isn't officially supported — Riot could change it, and endpoints may need updating if they do.
- Match history only backfills your most recent matches (Riot only serves recent history), so matches played before you first ran the tracker may not appear.

## Tech stack

- C# / .NET 10 (WPF for the UI, WinForms `NotifyIcon` for the system tray)
- SQLite (`Microsoft.Data.Sqlite`) for local storage
- Riot Client's local presence API and player-data (`pd`) match API

## Project structure

```
ValorantTracker.App/     WPF app: tray icon, HUD dashboard window
ValorantTracker.Core/    Game-state detection, Riot API clients, database, and stats logic
```

## Running it

Requires **Windows**, the **.NET 10 SDK**, and Valorant/Riot Client installed (the app reads its local lockfile at runtime).

```powershell
dotnet build
dotnet run --project ValorantTracker.App
```

The app starts minimized to the tray. Double-click the tray icon (or right-click → **Show Stats**) to open the dashboard; right-click → **Exit** to quit.
