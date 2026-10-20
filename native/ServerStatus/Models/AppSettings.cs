namespace ServerStatus.Models;

public sealed record AppSettings
{
    public const string DefaultAddress = "";

    public string ServerAddress { get; init; } = DefaultAddress;
    public int IntervalSeconds { get; init; } = 30;
    public bool Notifications { get; init; } = true;
    public bool Sound { get; init; } = true;
    public bool LaunchAtLogin { get; init; }

    public AppSettings Normalize()
    {
        _ = ServerEndpoint.Parse(ServerAddress);
        return this with
        {
            ServerAddress = ServerAddress.Trim(),
            IntervalSeconds = Math.Clamp(IntervalSeconds, 15, 300)
        };
    }
}
