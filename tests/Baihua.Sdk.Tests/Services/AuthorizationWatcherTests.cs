using BaihuaSdk.Models;
using BaihuaSdk.Push;
using BaihuaSdk.Services;
using BaihuaSdk.Tests.Push;
using MobileContract.Pairing;
using MobileContract.Services;
using Moq;
using Xunit;

namespace BaihuaSdk.Tests.Services;

public class AuthorizationWatcherTests
{
    private static RegisterDeviceResult AuthorizedResult(string secret = "secret-123") =>
        new() { Success = true, Authorized = true, SharedSecret = secret };

    private static RegisterDeviceResult NotAuthorizedResult(string requestId = "req-1") =>
        new() { Success = true, Authorized = false, RequestId = requestId };

    [Fact]
    public async Task WaitForAuthorizationAsync_AlreadyAuthorized_ReturnsImmediately()
    {
        var registrationMock = new Mock<IDeviceRegistrationService>();
        registrationMock.Setup(r => r.RegisterDeviceAsync("http://server", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthorizedResult());

        var pushService = new TestablePushWebSocketService(new HttpClient());
        var watcher = new AuthorizationWatcher(registrationMock.Object, pushService);

        var result = await watcher.WaitForAuthorizationAsync("http://server", "device");

        Assert.True(result.IsAuthorized);
        Assert.Equal("secret-123", result.SharedSecret);
    }

    [Fact]
    public async Task WaitForAuthorizationAsync_WebSocketPushAuthorized_ReturnsAuthorized()
    {
        var registrationMock = new Mock<IDeviceRegistrationService>();
        registrationMock.SetupSequence(r => r.RegisterDeviceAsync("http://server", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NotAuthorizedResult())
            .ReturnsAsync(AuthorizedResult("secret-ws"));

        var pushService = new TestablePushWebSocketService(new HttpClient());
        var watcher = new AuthorizationWatcher(registrationMock.Object, pushService);

        var task = watcher.WaitForAuthorizationAsync("http://server", "device", pollInterval: TimeSpan.FromSeconds(10));

        // 等待连接建立
        await Task.Delay(200);
        pushService.LastMockSocket!.EnqueueTextMessage("""{"type":"Authorized","deviceName":"device"}""");
        pushService.LastMockSocket!.EnqueueClose();

        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.IsAuthorized);
        Assert.Equal("secret-ws", result.SharedSecret);
    }

    [Fact]
    public async Task WaitForAuthorizationAsync_PollingFindsAuthorization_ReturnsAuthorized()
    {
        var registrationMock = new Mock<IDeviceRegistrationService>();
        registrationMock.SetupSequence(r => r.RegisterDeviceAsync("http://server", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NotAuthorizedResult())
            .ReturnsAsync(NotAuthorizedResult())
            .ReturnsAsync(AuthorizedResult("secret-poll"));

        // 让 WebSocket 连接失败
        var pushService = new TestablePushWebSocketService(new HttpClient(), () => throw new Exception("connect failed"));
        var watcher = new AuthorizationWatcher(registrationMock.Object, pushService);

        var result = await watcher.WaitForAuthorizationAsync(
            "http://server",
            "device",
            webSocketConnectionTimeout: TimeSpan.FromMilliseconds(100),
            pollInterval: TimeSpan.FromMilliseconds(100));

        Assert.True(result.IsAuthorized);
        Assert.Equal("secret-poll", result.SharedSecret);
    }

    [Fact]
    public async Task WaitForAuthorizationAsync_WebSocketConnectedThenDisconnected_FallsBackToPolling()
    {
        var registrationMock = new Mock<IDeviceRegistrationService>();
        registrationMock.SetupSequence(r => r.RegisterDeviceAsync("http://server", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NotAuthorizedResult())
            .ReturnsAsync(NotAuthorizedResult())
            .ReturnsAsync(AuthorizedResult("secret-fallback"));

        var pushService = new TestablePushWebSocketService(new HttpClient());
        var watcher = new AuthorizationWatcher(registrationMock.Object, pushService);

        var task = watcher.WaitForAuthorizationAsync(
            "http://server",
            "device",
            pollInterval: TimeSpan.FromMilliseconds(100));

        await Task.Delay(200);
        // 模拟服务端断开 WebSocket
        pushService.LastMockSocket!.EnqueueClose();

        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.IsAuthorized);
        Assert.Equal("secret-fallback", result.SharedSecret);
    }

    [Fact]
    public async Task WaitForAuthorizationAsync_Cancellation_ThrowsOperationCanceled()
    {
        var registrationMock = new Mock<IDeviceRegistrationService>();
        registrationMock.Setup(r => r.RegisterDeviceAsync("http://server", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NotAuthorizedResult());

        var pushService = new TestablePushWebSocketService(new HttpClient(), () => throw new Exception("connect failed"));
        var watcher = new AuthorizationWatcher(registrationMock.Object, pushService);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            watcher.WaitForAuthorizationAsync(
                "http://server",
                "device",
                webSocketConnectionTimeout: TimeSpan.FromMilliseconds(50),
                pollInterval: TimeSpan.FromDays(1),
                cts.Token));
    }

    [Fact]
    public async Task CheckAuthorizationAsync_Authorized_ReturnsAuthorized()
    {
        var registrationMock = new Mock<IDeviceRegistrationService>();
        registrationMock.Setup(r => r.RegisterDeviceAsync("http://server", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthorizedResult("secret-check"));

        var watcher = new AuthorizationWatcher(registrationMock.Object, new TestablePushWebSocketService(new HttpClient()));

        var result = await watcher.CheckAuthorizationAsync("http://server");

        Assert.True(result.IsAuthorized);
        Assert.Equal("secret-check", result.SharedSecret);
    }

    [Fact]
    public async Task CheckAuthorizationAsync_NotAuthorized_ReturnsRequestId()
    {
        var registrationMock = new Mock<IDeviceRegistrationService>();
        registrationMock.Setup(r => r.RegisterDeviceAsync("http://server", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NotAuthorizedResult("req-abc"));

        var watcher = new AuthorizationWatcher(registrationMock.Object, new TestablePushWebSocketService(new HttpClient()));

        var result = await watcher.CheckAuthorizationAsync("http://server");

        Assert.False(result.IsAuthorized);
        Assert.Equal("req-abc", result.RequestId);
    }

    [Fact]
    public async Task WaitForAuthorizationAsync_FiresWebSocketConnectionStateChanged()
    {
        var registrationMock = new Mock<IDeviceRegistrationService>();
        registrationMock.Setup(r => r.RegisterDeviceAsync("http://server", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NotAuthorizedResult());

        var pushService = new TestablePushWebSocketService(new HttpClient());
        var watcher = new AuthorizationWatcher(registrationMock.Object, pushService);

        var states = new List<bool>();
        watcher.WebSocketConnectionStateChanged += states.Add;

        var task = watcher.WaitForAuthorizationAsync("http://server", "device", pollInterval: TimeSpan.FromDays(1));
        await Task.Delay(200);
        pushService.LastMockSocket!.EnqueueClose();

        try { await task.WaitAsync(TimeSpan.FromSeconds(2)); } catch { /* ignored */ }

        Assert.Contains(true, states);
    }
}
