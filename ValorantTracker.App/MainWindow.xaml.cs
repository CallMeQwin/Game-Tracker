using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using ValorantTracker.Core;

namespace ValorantTracker.App;

public partial class MainWindow : Window
{
    private const string Game = "Valorant";
    private readonly Database _database;
    private readonly DispatcherTimer _refreshTimer;

    public MainWindow(Database database)
    {
        InitializeComponent();
        _database = database;

        RefreshStats();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _refreshTimer.Tick += (_, _) => RefreshStats();
        _refreshTimer.Start();
    }

    private void RefreshStats()
    {
        var today = DateTime.Now.Date;
        var events = _database.GetEventsSince(Game, today);

        var dailyStats = StatsCalculator.Calculate(events);
        ActiveText.Text = Format(dailyStats.Active);
        IdleText.Text = Format(dailyStats.Idle);

        var sessions = SessionCalculator.Calculate(events);
        SessionsList.ItemsSource = sessions
            .OrderByDescending(s => s.Start)
            .Select(s =>
                $"{s.Start:h:mm tt} - {(s.End.HasValue ? s.End.Value.ToString("h:mm tt") : "ongoing")}  " +
                $"|  Active: {Format(s.Active)}, Idle: {Format(s.Idle)}")
            .ToList();
    }

    private static string Format(TimeSpan span) => $"{(int)span.TotalHours}h {span.Minutes}m";
}
