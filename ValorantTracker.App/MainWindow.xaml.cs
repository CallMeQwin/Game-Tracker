using System;
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
        var stats = StatsCalculator.Calculate(events);

        ActiveText.Text = Format(stats.Active);
        IdleText.Text = Format(stats.Idle);
    }

    private static string Format(TimeSpan span) => $"{(int)span.TotalHours}h {span.Minutes}m";
}
