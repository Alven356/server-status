using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using ServerStatus.Models;

namespace ServerStatus.ViewModels;

public sealed class StatusViewModel : INotifyPropertyChanged
{
    private static readonly IBrush OnlineBrush = new SolidColorBrush(Color.Parse("#25815A"));
    private static readonly IBrush OfflineBrush = new SolidColorBrush(Color.Parse("#BD4C45"));
    private static readonly IBrush PendingBrush = new SolidColorBrush(Color.Parse("#9A7331"));
    private static readonly IBrush OnlineSoftBrush = new SolidColorBrush(Color.Parse("#E9F4EE"));
    private static readonly IBrush OfflineSoftBrush = new SolidColorBrush(Color.Parse("#F8ECEA"));
    private static readonly IBrush PendingSoftBrush = new SolidColorBrush(Color.Parse("#F7F1E5"));

    private ServerStatusSnapshot _snapshot = new();
    private bool _isChecking;
    private int _intervalSeconds = 30;

    public event PropertyChangedEventHandler? PropertyChanged;
    public required AsyncRelayCommand RefreshCommand { get; init; }
    public required RelayCommand SettingsCommand { get; init; }
    public required AsyncRelayCommand QuitCommand { get; init; }

    private bool HasConfiguredServer => !string.IsNullOrWhiteSpace(_snapshot.Address);

    public string StatusLabel => !HasConfiguredServer
        ? "需要设置"
        : _isChecking && _snapshot.State is ServerState.Waiting or ServerState.Unknown
        ? "正在检查"
        : _snapshot.State switch
        {
            ServerState.Online => "服务器在线",
            ServerState.Offline => "服务器离线",
            ServerState.Unknown => "无法确认",
            _ => "准备检查"
        };

    public string StatusDetail => !HasConfiguredServer
        ? "添加一个 Minecraft Java 服务器地址"
        : _isChecking
        ? "正在获取最新状态…"
        : _snapshot.State switch
        {
            ServerState.Online => "已收到服务器响应",
            ServerState.Offline => _snapshot.Detail,
            ServerState.Unknown => _snapshot.Detail,
            _ => "等待首次状态检查"
        };

    public string Address => HasConfiguredServer ? _snapshot.Address : "尚未配置";
    public string Players => !HasConfiguredServer
        ? "—"
        : _snapshot.State == ServerState.Online
        ? $"{_snapshot.PlayersOnline} / {_snapshot.PlayersMax}"
        : _snapshot.State == ServerState.Offline ? "0" : "—";
    public string Latency => _snapshot.LatencyMs is { } latency ? $"{latency} ms" : "—";
    public string Motd => !HasConfiguredServer
        ? "完成首次设置后，server-status 会在后台开始守望。"
        : string.IsNullOrWhiteSpace(_snapshot.Motd) ? "等待服务器响应。" : _snapshot.Motd;
    public string RefreshText => !HasConfiguredServer ? "添加服务器" : _isChecking ? "正在检查…" : "立即检查";
    public string MonitoringText => !HasConfiguredServer
        ? "等待完成首次设置"
        : _intervalSeconds < 60
        ? $"后台监控中 · 每 {_intervalSeconds} 秒"
        : $"后台监控中 · 每 {_intervalSeconds / 60} 分钟";
    public IBrush StateBrush => _snapshot.State switch
    {
        ServerState.Online => OnlineBrush,
        ServerState.Offline => OfflineBrush,
        _ => PendingBrush
    };
    public IBrush StateSoftBrush => _snapshot.State switch
    {
        ServerState.Online => OnlineSoftBrush,
        ServerState.Offline => OfflineSoftBrush,
        _ => PendingSoftBrush
    };

    public void UpdateStatus(ServerStatusSnapshot snapshot)
    {
        _snapshot = snapshot;
        RaiseAll();
    }

    public void SetChecking(bool value)
    {
        _isChecking = value;
        Raise(nameof(StatusLabel), nameof(StatusDetail), nameof(RefreshText));
    }

    public void SetInterval(int seconds)
    {
        _intervalSeconds = seconds;
        Raise(nameof(MonitoringText));
    }

    private void RaiseAll() => Raise(
        nameof(StatusLabel), nameof(StatusDetail), nameof(Address), nameof(Players), nameof(Latency),
        nameof(Motd), nameof(RefreshText), nameof(MonitoringText), nameof(StateBrush), nameof(StateSoftBrush));

    private void Raise(params string[] properties)
    {
        foreach (var property in properties)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
