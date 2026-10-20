using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ServerStatus.Models;
using ServerStatus.Platform;
using ServerStatus.ViewModels;
using ServerStatus.Views;

namespace ServerStatus.Services;

public sealed class AppController : IDisposable
{
    private static readonly TimeSpan TrayClickDebounce = TimeSpan.FromMilliseconds(180);
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly SettingsStore _settingsStore = new();
    private readonly StatusMonitor _monitor = new(new MinecraftStatusClient());
    private readonly StatusViewModel _viewModel;
    private readonly StatusPopupWindow _popup;
    private readonly SettingsWindow _settingsWindow;
    private ITrayIconService? _tray;
    private AppSettings _settings = new();
    private long _lastTrayClickTimestamp;
    private DateTimeOffset _suppressDeactivateUntil;
    private bool _monitorStarted;
    private bool _disposed;
    private bool _quitting;

    public AppController(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _lifetime = lifetime;
        _viewModel = new StatusViewModel
        {
            RefreshCommand = new AsyncRelayCommand(CheckNowAsync),
            SettingsCommand = new RelayCommand(OpenSettings),
            QuitCommand = new AsyncRelayCommand(QuitAsync)
        };
        _popup = new StatusPopupWindow { DataContext = _viewModel };
        _settingsWindow = new SettingsWindow { DataContext = _viewModel };
        _settingsWindow.SaveRequested += SaveSettingsAsync;
        _popup.EscapeRequested += (_, _) => _popup.Hide();
        _popup.Deactivated += PopupOnDeactivated;

        _monitor.CheckingChanged += (_, checking) =>
            Dispatcher.UIThread.Post(() => _viewModel.SetChecking(checking));
        _monitor.StatusChanged += (_, status) =>
            Dispatcher.UIThread.Post(() => ApplyStatus(status));
        _monitor.BecameOnline += (_, status) =>
            Dispatcher.UIThread.Post(() => NotifyOnline(status));
    }

    public async Task StartAsync()
    {
        ThrowIfDisposed();
        MacWindowInterop.ConfigureAccessoryApplication();
        _settings = await _settingsStore.LoadAsync();
        _viewModel.SetInterval(_settings.IntervalSeconds);
        _viewModel.UpdateStatus(new ServerStatusSnapshot { Address = _settings.ServerAddress });
        _settingsWindow.LoadSettings(_settings);
        _lifetime.MainWindow = _settingsWindow;

        var assetsDirectory = Path.Combine(AppContext.BaseDirectory, "Assets");
        _tray = TrayIconFactory.Create(assetsDirectory);
        _tray.Clicked += TrayOnClicked;
        _tray.SetState(ServerState.Waiting, string.IsNullOrWhiteSpace(_settings.ServerAddress)
            ? "server-status — 需要设置服务器"
            : "server-status — 等待检查");

        MacWindowInterop.PrepareTrayPanel(_popup);
        if (string.IsNullOrWhiteSpace(_settings.ServerAddress))
        {
            Dispatcher.UIThread.Post(OpenSettings);
        }
        else
        {
            _monitor.Start(_settings);
            _monitorStarted = true;
        }
    }

    private void TrayOnClicked(object? sender, EventArgs eventArgs)
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(_lastTrayClickTimestamp, now);
        if (_lastTrayClickTimestamp != 0 && elapsed < TrayClickDebounce) return;
        _lastTrayClickTimestamp = now;
        _suppressDeactivateUntil = DateTimeOffset.UtcNow.AddMilliseconds(350);

        if (_popup.IsVisible)
        {
            _popup.Hide();
            return;
        }
        ShowPopup();
    }

    private void ShowPopup()
    {
        if (_tray is null) return;
        var pointer = _tray.GetPointerPosition();
        var screen = _popup.Screens.ScreenFromPoint(pointer) ?? _popup.Screens.Primary;
        if (screen is not null)
        {
            var scaling = screen.Scaling;
            var width = (int)Math.Ceiling(_popup.Width * scaling);
            var height = (int)Math.Ceiling(_popup.Height * scaling);
            var work = screen.WorkingArea;
            var x = Math.Clamp(pointer.X - width / 2, work.X + 8, work.Right - width - 8);
            var nearTop = pointer.Y < work.Y + (int)(100 * scaling);
            var y = nearTop ? work.Y + 8 : work.Bottom - height - 8;
            _popup.Position = new PixelPoint(x, y);
        }

        MacWindowInterop.PrepareTrayPanel(_popup);
        _popup.Show();
        MacWindowInterop.PrepareTrayPanel(_popup);
        MacWindowInterop.OrderFrontRegardless(_popup);
    }

    private void PopupOnDeactivated(object? sender, EventArgs eventArgs)
    {
        DispatcherTimer.RunOnce(() =>
        {
            if (_popup.IsVisible && DateTimeOffset.UtcNow >= _suppressDeactivateUntil && !_settingsWindow.IsVisible)
            {
                _popup.Hide();
            }
        }, TimeSpan.FromMilliseconds(140));
    }

    private async Task CheckNowAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.ServerAddress))
        {
            OpenSettings();
            return;
        }
        _ = await _monitor.CheckNowAsync();
    }

    private void ApplyStatus(ServerStatusSnapshot status)
    {
        _viewModel.UpdateStatus(status);
        var tooltip = status.State switch
        {
            ServerState.Online => $"server-status — 在线 · {status.PlayersOnline}/{status.PlayersMax} 人",
            ServerState.Offline => "server-status — 离线",
            ServerState.Unknown => "server-status — 无法确认",
            _ => "server-status — 正在检查"
        };
        _tray?.SetState(status.State, tooltip);
    }

    private void NotifyOnline(ServerStatusSnapshot status)
    {
        if (_settings.Notifications)
        {
            _tray?.ShowNotification("服务器已上线", $"{_settings.ServerAddress} · {status.PlayersOnline}/{status.PlayersMax} 人在线");
        }
        if (_settings.Sound)
        {
            AlertSoundService.Play(Path.Combine(AppContext.BaseDirectory, "Assets", "online.wav"));
        }
    }

    private void OpenSettings()
    {
        _popup.Hide();
        _settingsWindow.LoadSettings(_settings);
        MacWindowInterop.ActivateForSettings();
        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private async Task SaveSettingsAsync(AppSettings settings)
    {
        var previousAddress = _settings.ServerAddress;
        await _settingsStore.SaveAsync(settings);
        await StartupRegistrationService.ApplyAsync(settings.LaunchAtLogin);
        _settings = settings;
        _viewModel.SetInterval(settings.IntervalSeconds);
        if (!string.Equals(previousAddress, settings.ServerAddress, StringComparison.OrdinalIgnoreCase))
        {
            _viewModel.UpdateStatus(new ServerStatusSnapshot { Address = settings.ServerAddress });
        }
        if (_monitorStarted)
        {
            _monitor.UpdateSettings(settings);
        }
        else
        {
            _monitor.Start(settings);
            _monitorStarted = true;
        }
    }

    private async Task QuitAsync()
    {
        if (_quitting) return;
        _quitting = true;

        // Remove the UI immediately, then let in-flight network work cancel without
        // blocking Avalonia's UI thread. Blocking here can make macOS report the app
        // as unresponsive while it is trying to terminate.
        _popup.Hide();
        _settingsWindow.Hide();
        DisposeTray();

        try
        {
            await _monitor.DisposeAsync();
        }
        finally
        {
            _disposed = true;
            _settingsWindow.AllowClose = true;
            _popup.Close();
            _settingsWindow.Close();
            _lifetime.Shutdown();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeTray();

        // Exit can be raised while the UI thread is shutting down (for example from
        // the macOS application menu). Cancel synchronously, but never wait for
        // network tasks on that thread.
        _monitor.Stop();
    }

    private void DisposeTray()
    {
        if (_tray is not null)
        {
            _tray.Clicked -= TrayOnClicked;
            _tray.Dispose();
            _tray = null;
        }
    }
}
