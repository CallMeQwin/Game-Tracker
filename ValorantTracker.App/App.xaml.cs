using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ValorantTracker.Core;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using ContextMenuStrip = System.Windows.Forms.ContextMenuStrip;

namespace ValorantTracker.App;

public partial class App : System.Windows.Application
{
    private NotifyIcon? _notifyIcon;
    private CancellationTokenSource? _cancellationTokenSource;
    private Database? _database;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _database = new Database();
        var tracker = new Tracker(_database);

        _cancellationTokenSource = new CancellationTokenSource();
        Task.Run(() => tracker.RunAsync(_cancellationTokenSource.Token));

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Show Stats", null, (_, _) => ShowMainWindow());
        contextMenu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _notifyIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "Valorant Tracker",
            ContextMenuStrip = contextMenu
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null)
        {
            _mainWindow = new MainWindow(_database!);
            _mainWindow.Closed += (_, _) => _mainWindow = null;
        }

        _mainWindow.Show();
        _mainWindow.Activate();
    }

    private void ExitApplication()
    {
        _cancellationTokenSource?.Cancel();

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        Shutdown();
    }
}

