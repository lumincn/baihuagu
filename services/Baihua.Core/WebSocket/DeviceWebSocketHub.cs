using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TaskRunner.Core.Shared.WebSocket;

public class DeviceWebSocketHub
{
    private readonly ConcurrentDictionary<string, WebSocketConnection> _connections = new();
    private readonly ILogger<DeviceWebSocketHub> _logger;

    public DeviceWebSocketHub(ILogger<DeviceWebSocketHub> logger)
    {
        _logger = logger;
    }

    public int ConnectedCount => _connections.Count;

    public async Task AcceptAsync(System.Net.WebSockets.WebSocket webSocket, string? deviceName = null)
    {
        var connectionId = Guid.NewGuid().ToString("N")[..8];
        var connection = new WebSocketConnection(connectionId, webSocket, deviceName);
        _connections[connectionId] = connection;
        _logger.LogInformation("WebSocket 客户端连接: {ConnectionId}, deviceName={DeviceName}", connectionId, deviceName);

        try
        {
            var buffer = new byte[4096];
            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closed", CancellationToken.None);
                    break;
                }
            }
        }
        catch (WebSocketException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _connections.TryRemove(connectionId, out _);
            _logger.LogInformation("WebSocket 客户端断开: {ConnectionId}", connectionId);
        }
    }

    public async Task BroadcastAsync(string action, string? deviceName = null, string? requestId = null,
        string? type = null, string? vaultId = null, string? vaultName = null)
    {
        if (_connections.IsEmpty) return;

        var msg = new Dictionary<string, object?>
        {
            ["action"] = action,
            ["deviceName"] = deviceName,
            ["requestId"] = requestId,
            ["timestamp"] = DateTime.UtcNow.ToString("o")
        };
        if (!string.IsNullOrEmpty(type)) msg["type"] = type;
        if (!string.IsNullOrEmpty(vaultId)) msg["vaultId"] = vaultId;
        if (!string.IsNullOrEmpty(vaultName)) msg["vaultName"] = vaultName;

        var message = JsonSerializer.Serialize(msg);
        var bytes = Encoding.UTF8.GetBytes(message);

        var dead = new List<string>();

        foreach (var kvp in _connections)
        {
            try
            {
                if (kvp.Value.Socket.State == WebSocketState.Open)
                {
                    await kvp.Value.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
                }
                else
                {
                    dead.Add(kvp.Key);
                }
            }
            catch
            {
                dead.Add(kvp.Key);
            }
        }

        foreach (var id in dead)
        {
            _connections.TryRemove(id, out _);
        }
    }

    private record WebSocketConnection(string Id, System.Net.WebSockets.WebSocket Socket, string? DeviceName);
}