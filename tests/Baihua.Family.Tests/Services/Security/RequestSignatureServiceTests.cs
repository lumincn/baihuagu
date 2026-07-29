using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TaskRunner.Core.Shared.Security;
using TaskRunner.Data;
using TaskRunner.Services;
using Xunit;

namespace TaskRunner.Family.Tests.Services.Security;

public class RequestSignatureServiceTests
{
    private const string TestSecret = "test-secret-key-12345";

    private RequestSignatureService CreateService(string? secret = null)
    {
        var config = new Dictionary<string, string?>
        {
            { "MobileAuth:SharedSecret", secret }
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config)
            .Build();

        var serverAddressServiceMock = new Mock<ServerAddressService>(
            new Mock<IDbContextFactory<FamilyDbContext>>().Object,
            NullLogger<ServerAddressService>.Instance,
            configuration
        );

        return new RequestSignatureService(serverAddressServiceMock.Object, configuration, NullLogger<RequestSignatureService>.Instance);
    }

    [Fact]
    public void IsConfigured_ReturnsFalse_WhenSecretNotSet()
    {
        var service = CreateService(null);
        Assert.False(service.IsConfigured);
    }

    [Fact]
    public void IsConfigured_ReturnsTrue_WhenSecretSet()
    {
        var service = CreateService(TestSecret);
        Assert.True(service.IsConfigured);
    }

    [Fact]
    public void VerifySignature_ReturnsFalse_WhenNotConfigured()
    {
        var service = CreateService(null);
        var result = service.VerifySignature("GET", "/api/test", null, "1234567890:signature");
        Assert.False(result);
    }

    [Fact]
    public void VerifySignature_ReturnsFalse_WhenMissingSignatureHeader()
    {
        var service = CreateService(TestSecret);
        var result = service.VerifySignature("GET", "/api/test", null, null);
        Assert.False(result);
    }

    [Fact]
    public void VerifySignature_ReturnsFalse_WhenInvalidSignatureFormat()
    {
        var service = CreateService(TestSecret);
        var result = service.VerifySignature("GET", "/api/test", null, "invalid");
        Assert.False(result);
    }

    [Fact]
    public void VerifySignature_ReturnsFalse_WhenTimestampOutOfRange()
    {
        var service = CreateService(TestSecret);
        var pastTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        var signature = service.ComputeSignature("GET", "/api/test", null, pastTimestamp);
        var result = service.VerifySignature("GET", "/api/test", null, $"{pastTimestamp}:{signature}");
        Assert.False(result);
    }

    [Fact]
    public void VerifySignature_ReturnsTrue_WhenValidSignature()
    {
        var service = CreateService(TestSecret);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = service.ComputeSignature("GET", "/api/test", null, timestamp);
        var result = service.VerifySignature("GET", "/api/test", null, $"{timestamp}:{signature}");
        Assert.True(result);
    }

    [Fact]
    public void VerifySignature_ReturnsTrue_WithRequestBody()
    {
        var service = CreateService(TestSecret);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var body = "{\"name\":\"test\",\"value\":123}";
        var signature = service.ComputeSignature("POST", "/api/test", body, timestamp);
        var result = service.VerifySignature("POST", "/api/test", body, $"{timestamp}:{signature}");
        Assert.True(result);
    }

    [Fact]
    public void VerifySignature_ReturnsFalse_WithWrongBody()
    {
        var service = CreateService(TestSecret);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = service.ComputeSignature("POST", "/api/test", "{\"name\":\"test\"}", timestamp);
        var result = service.VerifySignature("POST", "/api/test", "{\"name\":\"different\"}", $"{timestamp}:{signature}");
        Assert.False(result);
    }

    [Fact]
    public void VerifySignature_ReturnsFalse_WithWrongSecret()
    {
        var service = CreateService(TestSecret);
        var otherService = CreateService("different-secret");
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = otherService.ComputeSignature("GET", "/api/test", null, timestamp);
        var result = service.VerifySignature("GET", "/api/test", null, $"{timestamp}:{signature}");
        Assert.False(result);
    }

    [Fact]
    public void ComputeSignature_ProducesSameResultForSameInputs()
    {
        var service = CreateService(TestSecret);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sig1 = service.ComputeSignature("GET", "/api/test", "body", timestamp);
        var sig2 = service.ComputeSignature("GET", "/api/test", "body", timestamp);
        Assert.Equal(sig1, sig2);
    }

    [Fact]
    public void ComputeSignature_DifferentMethod_ProducesDifferentResult()
    {
        var service = CreateService(TestSecret);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sig1 = service.ComputeSignature("GET", "/api/test", null, timestamp);
        var sig2 = service.ComputeSignature("POST", "/api/test", null, timestamp);
        Assert.NotEqual(sig1, sig2);
    }

    [Fact]
    public void ComputeSignature_DifferentPath_ProducesDifferentResult()
    {
        var service = CreateService(TestSecret);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sig1 = service.ComputeSignature("GET", "/api/test1", null, timestamp);
        var sig2 = service.ComputeSignature("GET", "/api/test2", null, timestamp);
        Assert.NotEqual(sig1, sig2);
    }
}
