using BaihuaSdk.Services;
using BaihuaSdk.Signing;
using BaihuaSdk.Transport;
using Moq;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;
using MobileContract.VaultSync;

namespace BaihuaSdk.Tests.Services;

public class SyncServiceTests
{
    // ---- 静态方法测试 ----

    [Theory]
    [InlineData("file.md", true)]
    [InlineData("path/to/readme.json", true)]
    [InlineData("image.png", false)]
    [InlineData("photo.jpg", false)]
    [InlineData("notes.MD", true)] // case insensitive
    public void IsTextFile(string path, bool expected)
    {
        Assert.Equal(expected, SyncServiceImpl.IsTextFile(path));
    }

    [Theory]
    [InlineData("notes/readme.md")]
    [InlineData("simple-file.txt")]
    [InlineData("a/b/c/d/file.json")]
    public void AssertValidRelPath_Valid_ReturnsNormalized(string path)
    {
        var result = SyncServiceImpl.AssertValidRelPath(path);
        Assert.Equal(path, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/absolute/path")]
    [InlineData("../escape")]
    [InlineData("file:name")]
    [InlineData("contains../inside")]
    public void AssertValidRelPath_Invalid_Throws(string path)
    {
        Assert.Throws<ArgumentException>(() => SyncServiceImpl.AssertValidRelPath(path));
    }

    [Fact]
    public void AssertValidRelPath_ConvertsBackslash()
    {
        var result = SyncServiceImpl.AssertValidRelPath(@"dir\file.md");
        Assert.Equal("dir/file.md", result);
    }

    // ---- Mock HttpClient 测试 ----

    private static (HttpClient client, MockHttpMessageHandler handler) CreateMockClient()
    {
        var handler = new MockHttpMessageHandler();
        var client = new HttpClient(handler);
        return (client, handler);
    }

    [Fact]
    public async Task FetchManifestAsync_Success_ReturnsManifest()
    {
        var (client, handler) = CreateMockClient();
        var signerMock = new Mock<IRequestSigner>();
        signerMock.Setup(s => s.SignRequest(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(new Dictionary<string, string> { ["X-Mobile-Signature"] = "test" });

        var manifest = new VaultManifestResponse(
            VaultId: "vault-1",
            VaultName: "Test Vault",
            Cursor: 0,
            Files: new List<ManifestFile>
            {
                new(Op: "upsert", RelPath: "test.md", Mtime: 1000, Size: null, Sha256: null)
            });
        handler.SetupResponse("/mg/manifest", HttpStatusCode.OK, JsonSerializer.Serialize(manifest));

        var service = new SyncServiceImpl(client, signerMock.Object);

        var result = await service.FetchManifestAsync("http://localhost", "vault-1", "device-1");

        Assert.NotNull(result);
        Assert.Single(result.Files!);
        Assert.Equal("test.md", result.Files![0].RelPath);
    }

    [Fact]
    public async Task FetchManifestAsync_ServerError_Throws()
    {
        var (client, handler) = CreateMockClient();
        var signerMock = new Mock<IRequestSigner>();
        signerMock.Setup(s => s.SignRequest(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(new Dictionary<string, string>());

        handler.SetupResponse("/mg/manifest", HttpStatusCode.InternalServerError, "Server error");

        var service = new SyncServiceImpl(client, signerMock.Object);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.FetchManifestAsync("http://localhost", "vault-1", "device-1"));
    }

    [Fact]
    public async Task FetchManifestAsync_NotFound_Throws()
    {
        var (client, handler) = CreateMockClient();
        var signerMock = new Mock<IRequestSigner>();
        signerMock.Setup(s => s.SignRequest(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(new Dictionary<string, string>());

        handler.SetupResponse("/mg/manifest", HttpStatusCode.NotFound, "Not found");

        var service = new SyncServiceImpl(client, signerMock.Object);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.FetchManifestAsync("http://localhost", "vault-1", "device-1"));
    }

    [Fact]
    public async Task DownloadTextFileAsync_Success_ReturnsContent()
    {
        var (client, handler) = CreateMockClient();
        var signerMock = new Mock<IRequestSigner>();
        signerMock.Setup(s => s.SignRequest(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(new Dictionary<string, string>());

        handler.SetupResponse("/mg/file", HttpStatusCode.OK, "# Test Content");

        var service = new SyncServiceImpl(client, signerMock.Object);

        var result = await service.DownloadTextFileAsync("http://localhost", "vault-1", "test.md");

        Assert.Equal("# Test Content", result);
    }

    [Fact]
    public async Task DownloadBinaryFileAsync_Success_ReturnsBytes()
    {
        var (client, handler) = CreateMockClient();
        var signerMock = new Mock<IRequestSigner>();
        signerMock.Setup(s => s.SignRequest(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(new Dictionary<string, string>());

        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header
        handler.SetupResponse("/mg/file", HttpStatusCode.OK, bytes);

        var service = new SyncServiceImpl(client, signerMock.Object);

        var result = await service.DownloadBinaryFileAsync("http://localhost", "vault-1", "image.png");

        Assert.Equal(bytes, result);
    }

    [Fact]
    public void AssertValidRelPath_EscapePath_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            SyncServiceImpl.AssertValidRelPath("../escape.md"));
    }

    [Fact]
    public async Task FetchVaultListAsync_Success_ReturnsList()
    {
        var (client, handler) = CreateMockClient();
        var signerMock = new Mock<IRequestSigner>();
        signerMock.Setup(s => s.SignRequest(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(new Dictionary<string, string>());

        var vaults = new[]
        {
            new VaultInfo(Id: "v1", Name: "Vault 1", Industry: "notes", Source: "server"),
            new VaultInfo(Id: "v2", Name: "Vault 2", Industry: "dev", Source: "server")
        };
        handler.SetupResponse("/mg/vaults", HttpStatusCode.OK, JsonSerializer.Serialize(vaults));

        var service = new SyncServiceImpl(client, signerMock.Object);

        var result = await service.FetchVaultListAsync("http://localhost");

        Assert.Equal(2, result.Count);
        Assert.Equal("v1", result[0].Id);
    }

    [Fact]
    public async Task FetchVaultListAsync_CachesResult()
    {
        var (client, handler) = CreateMockClient();
        var signerMock = new Mock<IRequestSigner>();
        signerMock.Setup(s => s.SignRequest(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(new Dictionary<string, string>());

        var vaults = new[] { new VaultInfo(Id: "v1", Name: "Vault 1", Industry: "notes", Source: "server") };
        handler.SetupResponse("/mg/vaults", HttpStatusCode.OK, JsonSerializer.Serialize(vaults));

        var service = new SyncServiceImpl(client, signerMock.Object);

        // First call
        var result1 = await service.FetchVaultListAsync("http://localhost");
        // Second call (should use cache)
        var result2 = await service.FetchVaultListAsync("http://localhost");

        Assert.Equal(result1, result2);
        Assert.Single(handler.RequestLog); // Only one HTTP request made
    }

    [Fact]
    public void ClearVaultListCache_RemovesCache()
    {
        SyncServiceImpl.ClearVaultListCache("http://localhost");
        // No exception = success
    }
}
