using Moq;
using Moq.Protected;
using TaskRunner.Contracts.LocalModels;
using TaskRunner.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace TaskRunner.Family.Tests.Services;

public class OllamaLibraryClientTests
{
    private readonly Mock<ILogger<OllamaLibraryClient>> _mockLogger;

    public OllamaLibraryClientTests()
    {
        _mockLogger = new Mock<ILogger<OllamaLibraryClient>>();
    }

    [Fact]
    public void InferParamSizeFromName_WithGB_ReturnsCorrect()
    {
        var result = InferParamSizeFromName("llama3.1-70B");
        Assert.Equal("70B", result);
    }

    [Fact]
    public void InferParamSizeFromName_WithMB_ReturnsCorrect()
    {
        var result = InferParamSizeFromName("phi3-3.8B");
        Assert.Equal("3.8B", result);
    }

    [Fact]
    public void InferParamSizeFromName_NoSize_ReturnsUnknown()
    {
        var result = InferParamSizeFromName("generic-model");
        Assert.Equal("unknown", result);
    }

    [Fact]
    public void EstimateSizeGiB_7BModel_ReturnsApproximateSize()
    {
        var result = EstimateSizeGiB("7B");
        Assert.True(result >= 4 && result <= 5, $"Expected ~4.2GB, got {result}");
    }

    [Fact]
    public void EstimateSizeGiB_70BModel_ReturnsApproximateSize()
    {
        var result = EstimateSizeGiB("70B");
        Assert.True(result >= 40 && result <= 45, $"Expected ~42GB, got {result}");
    }

    [Fact]
    public void EstimateSizeGiB_UnknownParamSize_ReturnsDefault()
    {
        var result = EstimateSizeGiB("unknown");
        Assert.Equal(5, result);
    }

    [Fact]
    public void EstimateSizeGiB_13BModel_ReturnsApproximateSize()
    {
        var result = EstimateSizeGiB("13B");
        Assert.True(result >= 7 && result <= 9, $"Expected ~7.8GB, got {result}");
    }

    [Fact]
    public void EstimateHardwareRequirements_SmallModel_ReturnsRamOnly()
    {
        var (minRam, minVram) = EstimateHardwareRequirements(2);
        Assert.True(minRam >= 2.4 && minRam <= 3, $"Expected ~2.4GB RAM, got {minRam}");
        Assert.Null(minVram);
    }

    [Fact]
    public void EstimateHardwareRequirements_LargeModel_ReturnsRamAndVram()
    {
        var (minRam, minVram) = EstimateHardwareRequirements(10);
        Assert.True(minRam >= 11 && minRam <= 13, $"Expected ~12GB RAM, got {minRam}");
        Assert.NotNull(minVram);
        Assert.True(minVram >= 10 && minVram <= 12, $"Expected ~11GB VRAM, got {minVram}");
    }

    [Fact]
    public void EstimateHardwareRequirements_MediumModel_ReturnsRamOnly()
    {
        var (minRam, minVram) = EstimateHardwareRequirements(3);
        Assert.True(minRam >= 3.6 && minRam <= 4, $"Expected ~3.6GB RAM, got {minRam}");
        Assert.Null(minVram);
    }

    [Fact]
    public void CapitalizeName_Lowercase_ReturnsCapitalized()
    {
        var result = CapitalizeName("llama");
        Assert.Equal("Llama", result);
    }

    [Fact]
    public void CapitalizeName_Empty_ReturnsEmpty()
    {
        var result = CapitalizeName("");
        Assert.Equal("", result);
    }

    [Fact]
    public void CapitalizeName_Null_ReturnsNull()
    {
        var result = CapitalizeName(null!);
        Assert.Equal(null, result);
    }

    [Fact]
    public void ExtractModelNames_FromHtml_ReturnsUniqueNames()
    {
        var html = """
            <a href="/library/llama3.1">Llama 3.1</a>
            <a href="/library/mistral">Mistral</a>
            <a href="/library/llama3.1">Duplicate</a>
            <a href="/library/phi-3">Phi-3</a>
            """;
        var result = ExtractModelNames(html);
        Assert.Equal(3, result.Count);
        Assert.Contains("llama3.1", result);
        Assert.Contains("mistral", result);
        Assert.Contains("phi-3", result);
    }

    [Fact]
    public void ExtractModelNames_EmptyHtml_ReturnsEmpty()
    {
        var result = ExtractModelNames("");
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractModelNames_NoLibraryLinks_ReturnsEmpty()
    {
        var html = "<a href=\"/other/page\">Not Library</a>";
        var result = ExtractModelNames(html);
        Assert.Empty(result);
    }

    [Fact]
    public void GetCachedModels_EmptyCache_ReturnsEmptyList()
    {
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        var client = new OllamaLibraryClient(mockHttpClientFactory.Object, _mockLogger.Object);
        
        var result = client.GetCachedModels();
        
        Assert.Empty(result);
    }

    private static string InferParamSizeFromName(string name)
    {
        var method = typeof(OllamaLibraryClient).GetMethod("InferParamSizeFromName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (string)method!.Invoke(null, new object[] { name })!;
    }

    private static double EstimateSizeGiB(string paramSize)
    {
        var method = typeof(OllamaLibraryClient).GetMethod("EstimateSizeGiB", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (double)method!.Invoke(null, new object[] { paramSize })!;
    }

    private static (double MinRam, double? MinVram) EstimateHardwareRequirements(double sizeGiB)
    {
        var method = typeof(OllamaLibraryClient).GetMethod("EstimateHardwareRequirements", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return ((double, double?))method!.Invoke(null, new object[] { sizeGiB })!;
    }

    private static string CapitalizeName(string name)
    {
        var method = typeof(OllamaLibraryClient).GetMethod("CapitalizeName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (string)method!.Invoke(null, new object[] { name })!;
    }

    private static List<string> ExtractModelNames(string html)
    {
        var method = typeof(OllamaLibraryClient).GetMethod("ExtractModelNames", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (List<string>)method!.Invoke(null, new object[] { html })!;
    }
}