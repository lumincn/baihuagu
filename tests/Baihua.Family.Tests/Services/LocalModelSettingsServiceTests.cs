using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using TaskRunner.Services;
using Xunit;

namespace TaskRunner.Family.Tests.Services;

public class LocalModelSettingsServiceTests
{
    [Fact]
    public void LocalModelSettingsService_Constructor_LoadsFromFile()
    {
        var mockConfig = new Mock<IConfiguration>();
        mockConfig.Setup(c => c["LocalAI:DownloadDirectory"]).Returns((string?)null);

        var mockLogger = new Mock<ILogger<LocalModelSettingsService>>();

        var service = new LocalModelSettingsService(mockConfig.Object, mockLogger.Object);

        Assert.NotNull(service);
    }

    [Fact]
    public void LocalModelSettingsService_LoadLocalModelConfigFromFile_NoConfigFile_NoException()
    {
        var mockConfig = new Mock<IConfiguration>();
        var mockLogger = new Mock<ILogger<LocalModelSettingsService>>();

        var service = new LocalModelSettingsService(mockConfig.Object, mockLogger.Object);

        service.LoadLocalModelConfigFromFile();
    }
}