using TaskRunner.Contracts.Benchmark;
using Xunit;

namespace TaskRunner.Family.Tests.Contracts;

public class BenchmarkPromptsTests
{
    #region VramTiers

    [Fact]
    public void VramTiers_HasExpectedCount()
    {
        Assert.Equal(13, BenchmarkPrompts.VramTiers.Length);
    }

    [Fact]
    public void VramTiers_ContainsExpectedValues()
    {
        Assert.Contains(4, BenchmarkPrompts.VramTiers);
        Assert.Contains(8, BenchmarkPrompts.VramTiers);
        Assert.Contains(12, BenchmarkPrompts.VramTiers);
        Assert.Contains(16, BenchmarkPrompts.VramTiers);
        Assert.Contains(24, BenchmarkPrompts.VramTiers);
        Assert.Contains(32, BenchmarkPrompts.VramTiers);
        Assert.Contains(48, BenchmarkPrompts.VramTiers);
        Assert.Contains(64, BenchmarkPrompts.VramTiers);
        Assert.Contains(96, BenchmarkPrompts.VramTiers);
        Assert.Contains(128, BenchmarkPrompts.VramTiers);
        Assert.Contains(256, BenchmarkPrompts.VramTiers);
        Assert.Contains(512, BenchmarkPrompts.VramTiers);
        Assert.Contains(1024, BenchmarkPrompts.VramTiers);
    }

    [Fact]
    public void VramTiers_IsSortedAscending()
    {
        for (int i = 1; i < BenchmarkPrompts.VramTiers.Length; i++)
        {
            Assert.True(BenchmarkPrompts.VramTiers[i] > BenchmarkPrompts.VramTiers[i - 1]);
        }
    }

    #endregion

    #region TcmVramTiers

    [Fact]
    public void TcmVramTiers_HasExpectedCount()
    {
        Assert.Equal(13, BenchmarkPrompts.TcmVramTiers.Count);
    }

    [Fact]
    public void TcmVramTiers_FirstTier_Has4Gb()
    {
        Assert.Equal(4, BenchmarkPrompts.TcmVramTiers[0].VramGb);
    }

    [Fact]
    public void TcmVramTiers_HasInt4Models()
    {
        foreach (var tier in BenchmarkPrompts.TcmVramTiers)
        {
            Assert.NotEmpty(tier.Int4Models);
        }
    }

    [Fact]
    public void TcmVramTiers_HasInt8Models()
    {
        foreach (var tier in BenchmarkPrompts.TcmVramTiers)
        {
            Assert.NotEmpty(tier.Int8Models);
        }
    }

    [Fact]
    public void TcmVramTiers_ModelsHaveOllamaName()
    {
        foreach (var tier in BenchmarkPrompts.TcmVramTiers)
        {
            foreach (var model in tier.Int4Models)
            {
                Assert.NotEmpty(model.OllamaName);
            }
            foreach (var model in tier.Int8Models)
            {
                Assert.NotEmpty(model.OllamaName);
            }
        }
    }

    #endregion

    #region CodingVramTiers

    [Fact]
    public void CodingVramTiers_HasExpectedCount()
    {
        Assert.Equal(13, BenchmarkPrompts.CodingVramTiers.Count);
    }

    [Fact]
    public void CodingVramTiers_FirstTier_Has4Gb()
    {
        Assert.Equal(4, BenchmarkPrompts.CodingVramTiers[0].VramGb);
    }

    [Fact]
    public void CodingVramTiers_HasInt4Models()
    {
        foreach (var tier in BenchmarkPrompts.CodingVramTiers)
        {
            Assert.NotEmpty(tier.Int4Models);
        }
    }

    #endregion

    #region TcmPrompts

    [Fact]
    public void TcmPrompts_HasExpectedCount()
    {
        Assert.Equal(5, BenchmarkPrompts.TcmPrompts.Count);
    }

    [Fact]
    public void TcmPrompts_AllHaveIds()
    {
        foreach (var prompt in BenchmarkPrompts.TcmPrompts)
        {
            Assert.NotEmpty(prompt.Id);
        }
    }

    [Fact]
    public void TcmPrompts_AllHaveCategory()
    {
        foreach (var prompt in BenchmarkPrompts.TcmPrompts)
        {
            Assert.Equal("tcm", prompt.Category);
        }
    }

    [Fact]
    public void TcmPrompts_AllHaveExpectedKeywords()
    {
        foreach (var prompt in BenchmarkPrompts.TcmPrompts)
        {
            Assert.NotEmpty(prompt.ExpectedKeywords);
        }
    }

    [Fact]
    public void TcmPrompts_AllHaveMaxTokens()
    {
        foreach (var prompt in BenchmarkPrompts.TcmPrompts)
        {
            Assert.True(prompt.MaxTokens > 0);
        }
    }

    [Fact]
    public void TcmPrompts_ContainsExpectedIds()
    {
        Assert.Contains(BenchmarkPrompts.TcmPrompts, p => p.Id == "tcm-01");
        Assert.Contains(BenchmarkPrompts.TcmPrompts, p => p.Id == "tcm-02");
        Assert.Contains(BenchmarkPrompts.TcmPrompts, p => p.Id == "tcm-03");
        Assert.Contains(BenchmarkPrompts.TcmPrompts, p => p.Id == "tcm-04");
        Assert.Contains(BenchmarkPrompts.TcmPrompts, p => p.Id == "tcm-05");
    }

    #endregion

    #region CodingPrompts

    [Fact]
    public void CodingPrompts_HasExpectedCount()
    {
        Assert.Equal(5, BenchmarkPrompts.CodingPrompts.Count);
    }

    [Fact]
    public void CodingPrompts_AllHaveCategory()
    {
        foreach (var prompt in BenchmarkPrompts.CodingPrompts)
        {
            Assert.Equal("coding", prompt.Category);
        }
    }

    [Fact]
    public void CodingPrompts_ContainsExpectedIds()
    {
        Assert.Contains(BenchmarkPrompts.CodingPrompts, p => p.Id == "code-01");
        Assert.Contains(BenchmarkPrompts.CodingPrompts, p => p.Id == "code-02");
        Assert.Contains(BenchmarkPrompts.CodingPrompts, p => p.Id == "code-03");
        Assert.Contains(BenchmarkPrompts.CodingPrompts, p => p.Id == "code-04");
        Assert.Contains(BenchmarkPrompts.CodingPrompts, p => p.Id == "code-05");
    }

    #endregion

    #region GetPromptsByCategory

    [Fact]
    public void GetPromptsByCategory_Tcm_ReturnsTcmPrompts()
    {
        var result = BenchmarkPrompts.GetPromptsByCategory("tcm");

        Assert.Equal(5, result.Count);
        Assert.All(result, p => Assert.Equal("tcm", p.Category));
    }

    [Fact]
    public void GetPromptsByCategory_Coding_ReturnsCodingPrompts()
    {
        var result = BenchmarkPrompts.GetPromptsByCategory("coding");

        Assert.Equal(5, result.Count);
        Assert.All(result, p => Assert.Equal("coding", p.Category));
    }

    [Fact]
    public void GetPromptsByCategory_Other_ReturnsAllPrompts()
    {
        var result = BenchmarkPrompts.GetPromptsByCategory("unknown");

        Assert.Equal(10, result.Count);
    }

    [Fact]
    public void GetPromptsByCategory_Empty_ReturnsAllPrompts()
    {
        var result = BenchmarkPrompts.GetPromptsByCategory("");

        Assert.Equal(10, result.Count);
    }

    #endregion

    #region GetTiersByCategory

    [Fact]
    public void GetTiersByCategory_Tcm_ReturnsTcmTiers()
    {
        var result = BenchmarkPrompts.GetTiersByCategory("tcm");

        Assert.Equal(13, result.Count);
    }

    [Fact]
    public void GetTiersByCategory_Coding_ReturnsCodingTiers()
    {
        var result = BenchmarkPrompts.GetTiersByCategory("coding");

        Assert.Equal(13, result.Count);
    }

    [Fact]
    public void GetTiersByCategory_Other_ReturnsAllTiers()
    {
        var result = BenchmarkPrompts.GetTiersByCategory("unknown");

        Assert.Equal(26, result.Count);
    }

    #endregion

    #region GetModelsByCategory

    [Fact]
    public void GetModelsByCategory_Tcm_ReturnsModels()
    {
        var result = BenchmarkPrompts.GetModelsByCategory("tcm");

        Assert.NotEmpty(result);
        Assert.All(result, m => Assert.Equal("tcm", m.Category));
    }

    [Fact]
    public void GetModelsByCategory_Coding_ReturnsModels()
    {
        var result = BenchmarkPrompts.GetModelsByCategory("coding");

        Assert.NotEmpty(result);
        Assert.All(result, m => Assert.Equal("coding", m.Category));
    }

    [Fact]
    public void GetModelsByCategory_ModelsHaveOllamaName()
    {
        var result = BenchmarkPrompts.GetModelsByCategory("tcm");

        Assert.All(result, m => Assert.NotEmpty(m.OllamaName));
    }

    [Fact]
    public void GetModelsByCategory_ModelsHaveTags()
    {
        var result = BenchmarkPrompts.GetModelsByCategory("tcm");

        Assert.All(result, m => Assert.NotEmpty(m.Tags));
    }

    #endregion
}