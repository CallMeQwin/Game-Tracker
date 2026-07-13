# Valorant Tracker

A Windows tray app that tracks how much time you actually spend playing Valorant versus sitting idle in menus/lobby — so you can see how much time you've really put in today or this session.

## How it works

Valorant's Riot Client exposes a local, undocumented HTTPS API (used by community tools like match trackers) that reports real-time presence data, including whether you're in the menu, agent select, or an active match. This app:

1. Reads Riot Client's `lockfile` (`%LOCALAPPDATA%\Riot Games\Riot Client\Config\lockfile`) to get the local API's port and auth credentials
2. Polls the local presence API every 5 seconds to detect your current game state (`MENUS`, `PREGAME`, `INGAME`, or `CLOSED`)
3. Logs every state *change* as an event in a local SQLite database
4. Calculates active/idle time — both for the current session and totalled across the whole day — from that event log

## Features

- Runs quietly in the system tray, no visible window until you want it
- Distinguishes **active play time** (in a match) from **idle time** (sitting in menus with the game open)
- Tracks individual **sessions** (resets when you close the game) while still keeping a running **daily total**
- Local SQLite storage — nothing leaves your machine, no external accounts or servers involved

## Tech stack

- C# / .NET (WPF for the UI, WinForms `NotifyIcon` for the system tray)
- SQLite (`Microsoft.Data.Sqlite`) for local event storage
- Riot Client's local presence API for game-state detection

## Project structure

```
ValorantTracker.App/     WPF app: tray icon, stats window
ValorantTracker.Core/    Game-state detection, database, and stats logic
```

## Running it

Requires Valorant/Riot Client to be installed (the app reads its local lockfile at runtime).

```powershell
dotnet build
dotnet run --project ValorantTracker.App
```
