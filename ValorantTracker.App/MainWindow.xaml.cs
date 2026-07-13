using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ValorantTracker.Core;
using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;
using Point = System.Windows.Point;

namespace ValorantTracker.App;

public partial class MainWindow : Window
{
    private const string Game = "Valorant";
    private const double PixelsPerMinute = 1.4;
    private const double LabelGutterWidth = 46;
    private const double MinCardHeight = 58;

    private static readonly FontFamily MonoFont = new("Consolas");
    private static readonly FontFamily BodyFont = new("Segoe UI Semibold");

    private readonly Database _database;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _clockTimer;

    private readonly Brush _surfaceRaisedBrush;
    private readonly Brush _hairlineBrush;
    private readonly Brush _textBrush;
    private readonly Brush _textMutedBrush;
    private readonly Brush _activeBrush;
    private readonly Brush _idleBrush;
    private readonly Brush _alertBrush;
    private readonly Brush _offBrush;

    private List<Match> _lastTodayMatches = new();
    private List<DayPlaytime> _lastTrendBreakdown = new();
    private DateTime _selectedDay = DateTime.Now.Date;
    private bool _pulsing;

    public MainWindow(Database database)
    {
        InitializeComponent();
        _database = database;

        _surfaceRaisedBrush = (Brush)FindResource("SurfaceRaisedBrush");
        _hairlineBrush = (Brush)FindResource("HairlineBrush");
        _textBrush = (Brush)FindResource("TextBrush");
        _textMutedBrush = (Brush)FindResource("TextMutedBrush");
        _activeBrush = (Brush)FindResource("ActiveBrush");
        _idleBrush = (Brush)FindResource("IdleBrush");
        _alertBrush = (Brush)FindResource("AlertBrush");
        _offBrush = (Brush)FindResource("OffBrush");

        UpdateClock();

        // Re-render whenever the window is resized, so charts resize immediately
        // instead of waiting for the next 5-second refresh tick. Panels report
        // ActualWidth = 0 while Visibility=Collapsed, so this also fires the first
        // real render the moment a section is switched into view.
        TimelineScroll.SizeChanged += (_, _) =>
        {
            if (OverviewPanel.Visibility == Visibility.Visible)
                RenderTimeline(_lastTodayMatches, _selectedDay);
        };
        TrendChartBorder.SizeChanged += (_, _) =>
        {
            if (TrendsPanel.Visibility == Visibility.Visible)
                RenderTrendChart(_lastTrendBreakdown, TrendMonthRadio.IsChecked == true);
        };

        // Setting these here (after InitializeComponent, not in XAML) fires their
        // Checked handlers safely, once everything is constructed.
        NavOverview.IsChecked = true;
        TrendWeekRadio.IsChecked = true;
        RefreshAll();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _refreshTimer.Tick += (_, _) =>
        {
            RefreshStatus();
            // A past day's data is static — re-rendering it on every tick would keep
            // resetting the user's scroll position for no reason. Only today updates live.
            if (_selectedDay == DateTime.Now.Date)
                RefreshOverview();
            RefreshTrends();
        };
        _refreshTimer.Start();

        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
    }

    private void CaptionBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Section_Changed(object sender, RoutedEventArgs e)
    {
        OverviewPanel.Visibility = NavOverview.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        TrendsPanel.Visibility = NavTrends.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        HistoryPanel.Visibility = NavHistory.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TrendPeriod_Changed(object sender, RoutedEventArgs e) => RefreshTrends();

    private void PrevDay_Click(object sender, RoutedEventArgs e)
    {
        _selectedDay = _selectedDay.AddDays(-1);
        RefreshOverview();
    }

    private void NextDay_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDay >= DateTime.Now.Date)
            return;

        _selectedDay = _selectedDay.AddDays(1);
        RefreshOverview();
    }

    private void SelectedDayText_Click(object sender, MouseButtonEventArgs e)
    {
        if (_selectedDay == DateTime.Now.Date)
            return;

        _selectedDay = DateTime.Now.Date;
        RefreshOverview();
    }

    // Jumps Overview to a specific day — used when a Trends chart bar is clicked.
    private void ShowDay(DateTime day)
    {
        _selectedDay = day;
        NavOverview.IsChecked = true;
        RefreshOverview();
    }

    private void UpdateClock() => ClockText.Text = DateTime.Now.ToString("ddd, MMM d    h:mm:ss tt");

    private void RefreshAll()
    {
        RefreshStatus();
        RefreshOverview();
        RefreshTrends();
    }

    private void RefreshStatus()
    {
        var latest = _database.GetLatestEvent(Game);
        StatusText.Text = latest.HasValue
            ? StatusFormatter.Format(latest.Value.State, latest.Value.Mode)
            : "Not running";
        UpdateStatusDot(latest?.State);
    }

    private void RefreshOverview()
    {
        var now = DateTime.Now;
        var today = now.Date;
        var isToday = _selectedDay == today;

        // GetEventsSince has no upper bound, so for a past day it would also pull in
        // everything up through today — trim it back down to just the selected day.
        var events = _database.GetEventsSince(Game, _selectedDay)
            .Where(e => e.Timestamp < _selectedDay.AddDays(1))
            .ToList();

        _lastTodayMatches = MatchCalculator.Calculate(events);
        PlaytimeText.Text = Format(StatsCalculator.CalculatePlaytime(_lastTodayMatches, now));
        MatchCountText.Text = FormatMatchCount(_lastTodayMatches.Count);

        SelectedDayText.Text = isToday ? "TODAY" : _selectedDay.ToString("ddd, MMM d");
        NextDayButton.IsEnabled = !isToday;

        RenderTimeline(_lastTodayMatches, _selectedDay);
    }

    private void RefreshTrends()
    {
        var now = DateTime.Now;
        var today = now.Date;

        DateTime rangeStart;
        DateTime rangeEndExclusive;
        string label;
        var isMonth = TrendMonthRadio.IsChecked == true;

        if (isMonth)
        {
            rangeStart = new DateTime(today.Year, today.Month, 1);
            rangeEndExclusive = rangeStart.AddMonths(1);
            label = "PLAYTIME THIS MONTH";
        }
        else
        {
            var daysSinceMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            rangeStart = today.AddDays(-daysSinceMonday);
            rangeEndExclusive = rangeStart.AddDays(7);
            label = "PLAYTIME THIS WEEK";
        }

        var events = _database.GetEventsSince(Game, rangeStart);
        var matches = MatchCalculator.Calculate(events);

        TrendPlaytimeLabel.Text = label;
        TrendPlaytimeText.Text = Format(StatsCalculator.CalculatePlaytime(matches, now));
        TrendMatchCountText.Text = FormatMatchCount(matches.Count);

        _lastTrendBreakdown = StatsCalculator.CalculateDailyBreakdown(matches, rangeStart, rangeEndExclusive, now);
        RenderTrendChart(_lastTrendBreakdown, isMonth);
    }

    private void UpdateStatusDot(string? state)
    {
        var fill = state switch
        {
            "INGAME" => _activeBrush,
            "MENUS" or "PREGAME" => _idleBrush,
            _ => _offBrush
        };

        StatusDot.Fill = fill;
        PulseRing.Fill = fill;

        var shouldPulse = state == "INGAME";
        var storyboard = (Storyboard)FindResource("PulseStoryboard");

        if (shouldPulse && !_pulsing)
        {
            storyboard.Begin(this, true);
            _pulsing = true;
        }
        else if (!shouldPulse && _pulsing)
        {
            storyboard.Stop(this);
            PulseRing.Opacity = 0;
            _pulsing = false;
        }
    }

    private void RenderTimeline(List<Match> matches, DateTime day)
    {
        TimelineCanvas.Children.Clear();

        var now = DateTime.Now;
        var rangeStart = day;
        var rangeEnd = rangeStart.AddDays(1);

        var totalMinutes = (rangeEnd - rangeStart).TotalMinutes;
        var canvasHeight = totalMinutes * PixelsPerMinute;
        // ViewportWidth lags a frame behind ActualWidth during resize (it's recalculated
        // after the SizeChanged event fires), so subtract the scrollbar width from
        // ActualWidth directly instead — deterministic, no timing dependency.
        var canvasWidth = Math.Max(TimelineScroll.ActualWidth - SystemParameters.VerticalScrollBarWidth - 4, 300);

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
                Stroke = _hairlineBrush,
                StrokeThickness = 1
            };
            TimelineCanvas.Children.Add(line);

            var label = new TextBlock
            {
                Text = hour.ToString("h tt"),
                FontFamily = MonoFont,
                FontSize = 10,
                Foreground = _textMutedBrush
            };
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, top - 6);
            TimelineCanvas.Children.Add(label);
        }

        // MinCardHeight can be taller than the actual gap between two closely-spaced
        // matches. Their vertical position must stay time-accurate (it's what the hour
        // gridlines are for), so overlap is resolved by placing cards side-by-side in
        // columns instead — same approach a calendar app uses for overlapping events.
        var placedMatches = matches.Select(match =>
        {
            var top = (match.Start - rangeStart).TotalMinutes * PixelsPerMinute;
            var end = match.End ?? now;
            var height = Math.Max((end - match.Start).TotalMinutes * PixelsPerMinute, MinCardHeight);
            return (Match: match, Top: top, Height: height);
        }).ToList();

        var columnIndexes = new int[placedMatches.Count];
        var columnCounts = new int[placedMatches.Count];

        var clusterStart = 0;
        while (clusterStart < placedMatches.Count)
        {
            var clusterEnd = clusterStart;
            var clusterBottom = placedMatches[clusterStart].Top + placedMatches[clusterStart].Height;
            while (clusterEnd + 1 < placedMatches.Count && placedMatches[clusterEnd + 1].Top < clusterBottom)
            {
                clusterEnd++;
                clusterBottom = Math.Max(clusterBottom, placedMatches[clusterEnd].Top + placedMatches[clusterEnd].Height);
            }

            var columnBottoms = new List<double>();
            for (var i = clusterStart; i <= clusterEnd; i++)
            {
                var card = placedMatches[i];
                var columnIndex = columnBottoms.FindIndex(bottom => bottom <= card.Top);
                if (columnIndex == -1)
                {
                    columnIndex = columnBottoms.Count;
                    columnBottoms.Add(card.Top + card.Height);
                }
                else
                {
                    columnBottoms[columnIndex] = card.Top + card.Height;
                }
                columnIndexes[i] = columnIndex;
            }

            for (var i = clusterStart; i <= clusterEnd; i++)
                columnCounts[i] = columnBottoms.Count;

            clusterStart = clusterEnd + 1;
        }

        for (var i = 0; i < placedMatches.Count; i++)
        {
            var (match, top, height) = placedMatches[i];
            var columnCount = columnCounts[i];
            var availableWidth = canvasWidth - LabelGutterWidth - 8;
            var columnWidth = availableWidth / columnCount;

            var end = match.End ?? now;
            var title = StatusFormatter.FormatModeName(match.Mode);
            var timeRange = $"{match.Start:h:mm tt} - {(match.End.HasValue ? match.End.Value.ToString("h:mm tt") : "ongoing")}";
            var duration = $"Duration {Format(end - match.Start)}";

            var content = new StackPanel
            {
                Margin = new Thickness(10, 6, 8, 6),
                Children =
                {
                    new TextBlock { Text = title, FontFamily = BodyFont, FontSize = 12, Foreground = _textBrush, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = timeRange, FontFamily = MonoFont, FontSize = 10, Foreground = _textMutedBrush, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = duration, FontFamily = MonoFont, FontSize = 10, Foreground = _textMutedBrush, Margin = new Thickness(0, 1, 0, 0), TextWrapping = TextWrapping.Wrap }
                }
            };

            var accentBar = new System.Windows.Shapes.Rectangle { Fill = _activeBrush, Width = 3 };

            var cardGrid = new Grid();
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(accentBar, 0);
            Grid.SetColumn(content, 1);
            cardGrid.Children.Add(accentBar);
            cardGrid.Children.Add(content);

            var card = new Border
            {
                Background = _surfaceRaisedBrush,
                BorderBrush = _hairlineBrush,
                BorderThickness = new Thickness(1),
                Child = cardGrid
            };

            var columnIndex = columnIndexes[i];
            var columnGap = columnCount > 1 ? 4 : 0;
            Canvas.SetLeft(card, LabelGutterWidth + 4 + columnIndex * columnWidth);
            Canvas.SetTop(card, top);
            card.Width = Math.Max(columnWidth - columnGap, 40);
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
                Stroke = _alertBrush,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 }
            };
            TimelineCanvas.Children.Add(nowLine);

            var lockTick = new System.Windows.Shapes.Polyline
            {
                Points = new PointCollection { new Point(0, -5), new Point(6, 0), new Point(0, 5) },
                Stroke = _alertBrush,
                StrokeThickness = 1.5,
                StrokeLineJoin = PenLineJoin.Round
            };
            Canvas.SetLeft(lockTick, LabelGutterWidth - 8);
            Canvas.SetTop(lockTick, nowTop);
            TimelineCanvas.Children.Add(lockTick);
        }

        // Scrolling to "now" only makes sense when looking at today; a past day opens at the top.
        var scrollTarget = rangeStart == DateTime.Now.Date ? Math.Max(nowTop - 100, 0) : 0;
        TimelineScroll.ScrollToVerticalOffset(scrollTarget);
    }

    private void RenderTrendChart(List<DayPlaytime> breakdown, bool isMonth)
    {
        TrendChartCanvas.Children.Clear();
        if (breakdown.Count == 0)
            return;

        var width = Math.Max(TrendChartBorder.ActualWidth - TrendChartBorder.Padding.Left - TrendChartBorder.Padding.Right, 200);
        var height = Math.Max(TrendChartBorder.ActualHeight - TrendChartBorder.Padding.Top - TrendChartBorder.Padding.Bottom, 120);
        TrendChartCanvas.Width = width;
        TrendChartCanvas.Height = height;

        const double labelAreaHeight = 18;
        var plotHeight = height - labelAreaHeight;
        var maxPlaytimeSeconds = breakdown.Max(day => day.Playtime.TotalSeconds);

        const double gap = 6;
        var barWidth = Math.Max((width - gap * (breakdown.Count - 1)) / breakdown.Count, 2);

        for (var i = 0; i < breakdown.Count; i++)
        {
            var day = breakdown[i];
            var barHeight = maxPlaytimeSeconds > 0
                ? Math.Max(plotHeight * (day.Playtime.TotalSeconds / maxPlaytimeSeconds), 2)
                : 2;
            var x = i * (barWidth + gap);

            // A full-column hit zone (rather than just the visible bar) so short/empty
            // days are still easy to hover and click — bars can be just 2px tall.
            var hitZone = new System.Windows.Shapes.Rectangle
            {
                Width = barWidth,
                Height = plotHeight,
                Fill = System.Windows.Media.Brushes.Transparent,
                ToolTip = $"{day.Day:ddd, MMM d} — {Format(day.Playtime)}, {FormatMatchCount(day.Matches)}"
            };
            Canvas.SetLeft(hitZone, x);
            Canvas.SetTop(hitZone, 0);
            if (day.Day <= DateTime.Now.Date)
            {
                hitZone.Cursor = System.Windows.Input.Cursors.Hand;
                var clickedDay = day.Day;
                hitZone.MouseLeftButtonDown += (_, _) => ShowDay(clickedDay);
            }
            TrendChartCanvas.Children.Add(hitZone);

            var bar = new System.Windows.Shapes.Rectangle
            {
                Width = barWidth,
                Height = barHeight,
                Fill = day.Playtime > TimeSpan.Zero ? _activeBrush : _hairlineBrush,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(bar, x);
            Canvas.SetTop(bar, plotHeight - barHeight);
            TrendChartCanvas.Children.Add(bar);

            var showLabel = barWidth > 4 && (!isMonth || day.Day.Day == 1 || day.Day.Day % 5 == 0 || i == breakdown.Count - 1);
            if (!showLabel)
                continue;

            var label = new TextBlock
            {
                Text = isMonth ? day.Day.Day.ToString() : day.Day.ToString("ddd").Substring(0, 1),
                FontFamily = MonoFont,
                FontSize = 9,
                Foreground = _textMutedBrush
            };
            Canvas.SetLeft(label, x + barWidth / 2 - 4);
            Canvas.SetTop(label, plotHeight + 4);
            TrendChartCanvas.Children.Add(label);
        }
    }

    private static string Format(TimeSpan span) => $"{(int)span.TotalHours}h {span.Minutes}m";

    private static string FormatMatchCount(int count) => count == 1 ? "1 match" : $"{count} matches";
}
