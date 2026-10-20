using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ServerStatus.Models;

namespace ServerStatus.Services;

public sealed class MinecraftStatusClient : IServerStatusClient
{
    private const int MaxPacketSize = 2 * 1024 * 1024;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(7);
    private readonly HttpClient _httpClient;

    public MinecraftStatusClient(HttpMessageHandler? handler = null)
    {
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler, true);
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("server-status/2.1 desktop-utility");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
    }

    public async Task<ServerStatusSnapshot> CheckAsync(string address, CancellationToken cancellationToken = default)
    {
        ServerEndpoint requested;
        try
        {
            requested = ServerEndpoint.Parse(address);
        }
        catch (ArgumentException error)
        {
            return ServerStatusSnapshot.Unknown(address, error.Message);
        }

        using var raceCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var candidates = requested.Candidates();
        var remaining = new List<Task<ProbeResult>>();
        foreach (var candidate in candidates)
        {
            remaining.Add(CaptureAsync(() => PingDirectAsync(candidate, raceCancellation.Token)));
            remaining.Add(CaptureAsync(() => QueryCompatibleApiAsync(candidate, raceCancellation.Token)));
            remaining.Add(CaptureAsync(() => QueryMcStatusAsync(candidate, raceCancellation.Token)));
        }

        var completedStatuses = new List<ServerStatusSnapshot>();
        var errors = new List<Exception>();
        while (remaining.Count > 0)
        {
            var completed = await Task.WhenAny(remaining);
            remaining.Remove(completed);
            var result = await completed;
            if (result.Error is not null)
            {
                errors.Add(result.Error);
                continue;
            }

            if (result.Status is null)
            {
                continue;
            }

            var status = result.Status with
            {
                Address = requested.Display,
                CheckedAt = DateTimeOffset.UtcNow
            };
            completedStatuses.Add(status);

            if (status.State == ServerState.Online || status.Source == "motd")
            {
                await raceCancellation.CancelAsync();
                return status;
            }
        }

        var confirmedMotdOffline = completedStatuses.FirstOrDefault(status => status.Source == "motd");
        if (confirmedMotdOffline is not null)
        {
            return confirmedMotdOffline;
        }

        var confirmedOffline = completedStatuses.FirstOrDefault(status => status.State == ServerState.Offline);
        if (confirmedOffline is not null)
        {
            return confirmedOffline;
        }

        var detail = FriendlyError(errors.FirstOrDefault());
        return ServerStatusSnapshot.Unknown(requested.Display, detail);
    }

    public async Task<ServerStatusSnapshot> PingDirectAsync(ServerEndpoint endpoint, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        using var client = new TcpClient { NoDelay = true };
        var stopwatch = Stopwatch.StartNew();
        await client.ConnectAsync(endpoint.Host, endpoint.Port, timeout.Token);
        await using var stream = client.GetStream();

        using var handshakeBody = new MemoryStream();
        WriteVarInt(handshakeBody, 0);
        WriteVarInt(handshakeBody, 47);
        WriteString(handshakeBody, endpoint.Host);
        var portBytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(portBytes, checked((ushort)endpoint.Port));
        handshakeBody.Write(portBytes);
        WriteVarInt(handshakeBody, 1);

        await WritePacketAsync(stream, handshakeBody.ToArray(), timeout.Token);
        await WritePacketAsync(stream, new byte[] { 0 }, timeout.Token);

        var packetLength = await ReadVarIntAsync(stream, timeout.Token);
        if (packetLength is <= 0 or > MaxPacketSize)
        {
            throw new InvalidDataException("服务器返回了无效数据。");
        }

        var packet = new byte[packetLength];
        await stream.ReadExactlyAsync(packet, timeout.Token);
        stopwatch.Stop();

        var offset = 0;
        if (ReadVarInt(packet, ref offset) != 0)
        {
            throw new InvalidDataException("服务器返回了无效状态包。");
        }
        var jsonLength = ReadVarInt(packet, ref offset);
        if (jsonLength < 0 || offset + jsonLength > packet.Length)
        {
            throw new InvalidDataException("服务器状态数据不完整。");
        }

        using var document = JsonDocument.Parse(packet.AsMemory(offset, jsonLength));
        return ParseOnlinePayload(document.RootElement, endpoint, stopwatch.ElapsedMilliseconds, "direct");
    }

    private async Task<ServerStatusSnapshot> QueryCompatibleApiAsync(ServerEndpoint endpoint, CancellationToken cancellationToken)
    {
        var uri = new UriBuilder("https", "ping.cornbread2100.com", -1, "/ping")
        {
            Query = $"ip={Uri.EscapeDataString(endpoint.Host)}&port={endpoint.Port}&protocol=47&_={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
        }.Uri;

        var stopwatch = Stopwatch.StartNew();
        using var response = await SendAsync(uri, cancellationToken);
        stopwatch.Stop();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("version", out _))
        {
            throw new InvalidDataException("兼容检测未返回有效状态。");
        }
        return ParseOnlinePayload(document.RootElement, endpoint, Math.Max(1, stopwatch.ElapsedMilliseconds), "compatible");
    }

    private async Task<ServerStatusSnapshot> QueryMcStatusAsync(ServerEndpoint endpoint, CancellationToken cancellationToken)
    {
        var encoded = Uri.EscapeDataString(endpoint.Display);
        var uri = new Uri($"https://api.mcstatus.io/v2/status/java/{encoded}?query=false&timeout=5&_={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
        var stopwatch = Stopwatch.StartNew();
        using var response = await SendAsync(uri, cancellationToken);
        stopwatch.Stop();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("online", out var onlineElement) || onlineElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException("备用检测未返回有效状态。");
        }

        var host = GetString(root, "host") ?? endpoint.Host;
        var port = GetInt(root, "port") ?? endpoint.Port;
        var resolved = port == ServerEndpoint.DefaultPort ? host : $"{host}:{port}";
        if (!onlineElement.GetBoolean())
        {
            return new ServerStatusSnapshot
            {
                State = ServerState.Offline,
                Address = endpoint.Display,
                ResolvedAddress = resolved,
                Detail = "服务器离线",
                Motd = "后台监控会继续检查。",
                LatencyMs = Math.Max(1, stopwatch.ElapsedMilliseconds),
                Source = "mcstatus"
            };
        }

        var players = root.TryGetProperty("players", out var playersElement) ? playersElement : default;
        var version = root.TryGetProperty("version", out var versionElement) ? versionElement : default;
        var motdElement = root.TryGetProperty("motd", out var motd) ? motd : default;
        var motdText = motdElement.ValueKind == JsonValueKind.Object
            ? GetString(motdElement, "clean") ?? GetString(motdElement, "raw") ?? string.Empty
            : string.Empty;

        return Classify(new ServerStatusSnapshot
        {
            State = ServerState.Online,
            Address = endpoint.Display,
            ResolvedAddress = resolved,
            PlayersOnline = GetInt(players, "online") ?? 0,
            PlayersMax = GetInt(players, "max") ?? 0,
            LatencyMs = Math.Max(1, stopwatch.ElapsedMilliseconds),
            Motd = MotdClassifier.StripMinecraftFormatting(motdText),
            Detail = GetString(version, "name_clean") ?? GetString(version, "name_raw") ?? "在线",
            Source = "mcstatus"
        });
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        return response;
    }

    private static ServerStatusSnapshot ParseOnlinePayload(JsonElement root, ServerEndpoint endpoint, long latency, string source)
    {
        var players = root.TryGetProperty("players", out var playersElement) ? playersElement : default;
        var version = root.TryGetProperty("version", out var versionElement) ? versionElement : default;
        var description = root.TryGetProperty("description", out var descriptionElement)
            ? TextFromComponent(descriptionElement)
            : string.Empty;

        return Classify(new ServerStatusSnapshot
        {
            State = ServerState.Online,
            Address = endpoint.Display,
            ResolvedAddress = endpoint.Display,
            PlayersOnline = GetInt(players, "online") ?? 0,
            PlayersMax = GetInt(players, "max") ?? 0,
            LatencyMs = Math.Max(1, latency),
            Motd = MotdClassifier.StripMinecraftFormatting(description),
            Detail = GetString(version, "name") ?? "在线",
            Source = source
        });
    }

    private static ServerStatusSnapshot Classify(ServerStatusSnapshot status)
    {
        if (!MotdClassifier.IsOffline(status.Motd))
        {
            return status;
        }
        return status with
        {
            State = ServerState.Offline,
            PlayersOnline = 0,
            Detail = "Aternos 已返回离线状态",
            Source = "motd"
        };
    }

    public static string TextFromComponent(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Concat(element.EnumerateArray().Select(TextFromComponent)),
            JsonValueKind.Object => TextFromObject(element),
            _ => string.Empty
        };
    }

    private static string TextFromObject(JsonElement element)
    {
        var text = GetString(element, "text") ?? GetString(element, "translate") ?? string.Empty;
        if (element.TryGetProperty("extra", out var extra))
        {
            text += TextFromComponent(extra);
        }
        return text;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number
            : null;

    private static async Task<ProbeResult> CaptureAsync(Func<Task<ServerStatusSnapshot>> operation)
    {
        try
        {
            return new ProbeResult(await operation(), null);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return new ProbeResult(null, error);
        }
    }

    private static string FriendlyError(Exception? error) => error switch
    {
        SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.TryAgain } => "找不到服务器地址",
        SocketException { SocketErrorCode: SocketError.ConnectionRefused } => "服务器拒绝连接",
        TimeoutException => "连接超时",
        OperationCanceledException => "连接超时",
        HttpRequestException requestError => requestError.HttpRequestError == HttpRequestError.NameResolutionError
            ? "找不到服务器地址"
            : "网络未返回明确状态",
        null => "网络未返回明确状态",
        _ => error.Message
    };

    private static void WriteVarInt(Stream stream, int value)
    {
        var remaining = unchecked((uint)value);
        do
        {
            var current = (byte)(remaining & 0x7f);
            remaining >>= 7;
            if (remaining != 0) current |= 0x80;
            stream.WriteByte(current);
        } while (remaining != 0);
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteVarInt(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static async Task WritePacketAsync(Stream stream, byte[] payload, CancellationToken cancellationToken)
    {
        using var packet = new MemoryStream();
        WriteVarInt(packet, payload.Length);
        packet.Write(payload);
        await stream.WriteAsync(packet.ToArray(), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<int> ReadVarIntAsync(Stream stream, CancellationToken cancellationToken)
    {
        var result = 0;
        var shift = 0;
        var oneByte = new byte[1];
        for (var index = 0; index < 5; index++)
        {
            await stream.ReadExactlyAsync(oneByte, cancellationToken);
            var current = oneByte[0];
            result |= (current & 0x7f) << shift;
            if ((current & 0x80) == 0) return result;
            shift += 7;
        }
        throw new InvalidDataException("VarInt 过长。");
    }

    private static int ReadVarInt(ReadOnlySpan<byte> buffer, ref int offset)
    {
        var result = 0;
        var shift = 0;
        for (var index = 0; index < 5 && offset < buffer.Length; index++)
        {
            var current = buffer[offset++];
            result |= (current & 0x7f) << shift;
            if ((current & 0x80) == 0) return result;
            shift += 7;
        }
        throw new InvalidDataException("VarInt 无效。");
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed record ProbeResult(ServerStatusSnapshot? Status, Exception? Error);
}
