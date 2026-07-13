using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ValorantTracker.Core;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace ValorantTracker.App;

public partial class MainWindow : Window
{
    private const string Game = "Valorant";
    private const double PixelsPerMinute = 1.4;
    private const double LabelGutterWidth = 46;
    private const double MinCardHeight = 34;

    private readonly Database _database;
    private readonly DispatcherTimer _refreshTimer;

    public MainWindow(Database database)
    {
        InitializeComponent();
        _database = database;

        // Setting IsChecked here (after InitializeComponent, not in XAML) fires
        // Period_Changed -> RefreshStats() safely, once everything is constructed.
        TodayRadio.IsChecked = true;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _refreshTimer.Tick += (_, _) => RefreshStats();
        _refreshTimer.Start();
    }

    private void Period_Changed(object sender, RoutedEventArgs e) => RefreshStats();

    private void RefreshStats()
    {
        var latest = _database.GetLatestEvent(Game);
        StatusText.Text = latest.HasValue
            ? StatusFormatter.Format(latest.Value.State, latest.Value.Mode)
            : "Not running";

        var today = DateTime.Now.Date;
        var periodStart = GetSelectedPeriodStart(today);
        var events = _database.GetEventsSince(Game, periodStart);

        var stats = StatsCalculator.Calculate(events);
        ActiveText.Text = Format(stats.Active);
        IdleText.Text = Format(stats.Idle);

        var isToday = periodStart == today;
        SessionsHeader.Visibility = isToday ? Visibility.Visible : Visibility.Collapsed;
        TimelineBorder.Visibility = isToday ? Visibility.Visible : Visibility.Collapsed;

        if (isToday)
        {
            var sessions = SessionCalculator.Calculate(events);
            RenderTimeline(sessions);
        }
    }

    private void RenderTimeline(List<Session> sessions)
    {
        TimelineCanvas.Children.Clear();

        var now = DateTime.Now;
        var rangeStart = (sessions.Count > 0 ? sessions.Min(s => s.Start) : now).Date
            .AddHours((sessions.Count > 0 ? sessions.Min(s => s.Start) : now).Hour);
        var rangeEndSource = sessions.Count > 0
            ? sessions.Max(s => s.End ?? now)
            : now;
        var rangeEnd = rangeEndSource.Date.AddHours(rangeEndSource.Hour + 1);
        if (rangeEnd < now.Date.AddHours(now.Hour + 1) && sessions.Any(s => s.End == null))
            rangeEnd = now.Date.AddHours(now.Hour + 1);

        var totalMinutes = (rangeEnd - rangeStart).TotalMinutes;
        var canvasHeight = totalMinutes * PixelsPerMinute;
        var canvasWidth = Math.Max(TimelineScroll.ActualWidth - 4, 300);

        TimelineCanvas.Width = canvasWidth;
        TimelineCanvas.Height = canvasHeight;

        for (var hour = rangeStart; hour <= rangeEnd; hour = hour.AddHours(1))
        {
            var top = (hour - rangeStart).TotalMinutes * PixelsPerMinute;

            var line = new System.Windows.Shapes.Line
            {
                X1 = LabelGutterWidth,
                X2 = canvasWidth,
                Y1 = top,
                Y2 = top,
                Stroke = Brushes.WhiteSmoke,
                StrokeThickness = 1
            };
            TimelineCanvas.Children.Add(line);

            var label = new TextBlock
            {
                Text = hour.ToString("h tt"),
                FontSize = 11,
                Foreground = Brushes.Gray
            };
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, top - 6);
            TimelineCanvas.Children.Add(label);
        }

        foreach (var session in sessions)
        {
            var top = (session.Start - rangeStart).TotalMinutes * PixelsPerMinute;
            var end = session.End ?? now;
            var height = Math.Max((end - session.Start).TotalMinutes * PixelsPerMinute, MinCardHeight);

            var mostlyActive = session.Active >= session.Idle;
            var background = mostlyActive
                ? new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9))
                : new SolidColorBrush(Color.FromRgb(0xFF, 0xF8, 0xE1));
            var border = mostlyActive
                ? new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A))
                : new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00));

            var title = session.Modes.Count > 0
                ? string.Join(", ", session.Modes)
                : (mostlyActive ? "In Match" : "In Menu");

            var timeRange = $"{session.Start:h:mm tt} - {(session.End.HasValue ? session.End.Value.ToString("h:mm tt") : "ongoing")}";
            var breakdown = $"Active {Format(session.Active)}, Idle {Format(session.Idle)}";

            var card = new Border
            {
                Background = background,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 4, 8, 4),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold },
                        new TextBlock { Text = timeRange, FontSize = 10, Foreground = Brushes.DimGray },
                        new TextBlock { Text = breakdown, FontSize = 10, Foreground = Brushes.DimGray }
                    }
                }
            };

            Canvas.SetLeft(card, LabelGutterWidth + 4);
            Canvas.SetTop(card, top);
            card.Width = canvasWidth - LabelGutterWidth - 8;
            card.Height = height;
            TimelineCanvas.Children.Add(card);
        }

        var nowTop = (now - rangeStart).TotalMinutes * PixelsPerMinute;
        if (nowTop >= 0 && nowTop <= canvasHeight)
        {
            var nowLine = new System.Windows.Shapes.Line
            {
                X1 = LabelGutterWidth,
                X2 = canvasWidth,
                Y1 = nowTop,
                Y2 = nowTop,
                Stroke = Brushes.Red,
                StrokeThickness = 1
            };
            TimelineCanvas.Children.Add(nowLine);
        }

        TimelineScroll.ScrollToVerticalOffset(Math.Max(nowTop - 100, 0));
    }

    private DateTime GetSelectedPeriodStart(DateTime today)
    {
        if (WeekRadio.IsChecked == true)
        {
            var daysSinceMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            return today.AddDays(-daysSinceMonday);
        }

        if (MonthRadio.IsChecked == true)
            return new DateTime(today.Year, today.Month, 1);

        return today;
    }

    private static string Format(TimeSpan span) => $"{(int)span.TotalHours}h {span.Minutes}m";
}
