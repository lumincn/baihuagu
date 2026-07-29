using TaskRunner.Services;
using Xunit;

namespace TaskRunner.Family.Tests.Services;

public class AppPathsTests
{
    [Fact]
    public void GetConfigDirectory_WithEnvVar_ReturnsEnvDir()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Environment.SetEnvironmentVariable("YJ_DATA_DIR", tempDir);
        try
        {
            var result = AppPaths.GetConfigDirectory();
            Assert.Equal(tempDir, result);
            Assert.True(Directory.Exists(result));
        }
        finally
        {
            Environment.SetEnvironmentVariable("YJ_DATA_DIR", null);
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir);
        }
    }

    [Fact]
    public void GetConfigDirectory_WithoutEnvVar_ReturnsBaseDirectory()
    {
        Environment.SetEnvironmentVariable("YJ_DATA_DIR", null);
        var result = AppPaths.GetConfigDirectory();
        Assert.Equal(AppDomain.CurrentDomain.BaseDirectory, result);
    }

    [Fact]
    public void GetConfigDirectory_WithEmptyEnvVar_FallsBackToBase()
    {
        Environment.SetEnvironmentVariable("YJ_DATA_DIR", "  ");
        var result = AppPaths.GetConfigDirectory();
        Assert.Equal(AppDomain.CurrentDomain.BaseDirectory, result);
        Environment.SetEnvironmentVariable("YJ_DATA_DIR", null);
    }
}
