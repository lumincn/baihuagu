using TaskRunner.Contracts.LocalModels;
using Xunit;

namespace TaskRunner.Family.Tests.Contracts;

public class ModelDatabaseTests
{
    #region AllModels

    [Fact]
    public void AllModels_HasExpectedCount()
    {
        Assert.True(ModelDatabase.AllModels.Count >= 20);
    }

    [Fact]
    public void AllModels_AllHaveId()
    {
        foreach (var model in ModelDatabase.AllModels)
        {
            Assert.NotEmpty(model.Id);
        }
    }

    [Fact]
    public void AllModels_AllHaveName()
    {
        foreach (var model in ModelDatabase.AllModels)
        {
            Assert.NotEmpty(model.Name);
        }
    }

    [Fact]
    public void AllModels_AllHaveOllamaModelName()
    {
        foreach (var model in ModelDatabase.AllModels)
        {
            Assert.NotEmpty(model.OllamaModelName);
        }
    }

    [Fact]
    public void AllModels_AllHaveDescription()
    {
        foreach (var model in ModelDatabase.AllModels)
        {
            Assert.NotEmpty(model.Description);
        }
    }

    [Fact]
    public void AllModels_AllHaveParameterSize()
    {
        foreach (var model in ModelDatabase.AllModels)
        {
            Assert.NotEmpty(model.ParameterSize);
        }
    }

    [Fact]
    public void AllModels_AllHaveQuantization()
    {
        foreach (var model in ModelDatabase.AllModels)
        {
            Assert.NotEmpty(model.Quantization);
        }
    }

    [Fact]
    public void AllModels_AllHavePositiveSize()
    {
        foreach (var model in ModelDatabase.AllModels)
        {
            Assert.True(model.SizeGiB > 0);
        }
    }

    [Fact]
    public void AllModels_AllHavePositiveMinRam()
    {
        foreach (var model in ModelDatabase.AllModels)
        {
            Assert.True(model.MinRamGiB > 0);
        }
    }

    [Fact]
    public void AllModels_AllHaveTags()
    {
        foreach (var model in ModelDatabase.AllModels)
        {
            Assert.NotEmpty(model.Tags);
        }
    }

    [Fact]
    public void AllModels_AllHaveCompany()
    {
        foreach (var model in ModelDatabase.AllModels)
        {
            Assert.NotEmpty(model.Company);
        }
    }

    [Fact]
    public void AllModels_IdsAreUnique()
    {
        var ids = ModelDatabase.AllModels.Select(m => m.Id).ToList();
        var uniqueIds = ids.Distinct().ToList();
        Assert.Equal(ids.Count, uniqueIds.Count);
    }

    [Fact]
    public void AllModels_OllamaNamesAreUnique()
    {
        var names = ModelDatabase.AllModels.Select(m => m.OllamaModelName).ToList();
        var uniqueNames = names.Distinct().ToList();
        Assert.Equal(names.Count, uniqueNames.Count);
    }

    #endregion

    #region GetByTag

    [Fact]
    public void GetByTag_Chinese_ReturnsChineseModels()
    {
        var result = ModelDatabase.GetByTag("chinese");

        Assert.NotEmpty(result);
        Assert.All(result, m => Assert.Contains("chinese", m.Tags, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetByTag_Tcm_ReturnsTcmModels()
    {
        var result = ModelDatabase.GetByTag("tcm");

        Assert.NotEmpty(result);
        Assert.All(result, m => Assert.Contains("tcm", m.Tags, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetByTag_Code_ReturnsCodeModels()
    {
        var result = ModelDatabase.GetByTag("code");

        Assert.NotEmpty(result);
        Assert.All(result, m => Assert.Contains("code", m.Tags, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetByTag_Lightweight_ReturnsLightweightModels()
    {
        var result = ModelDatabase.GetByTag("lightweight");

        Assert.NotEmpty(result);
        Assert.All(result, m => Assert.Contains("lightweight", m.Tags, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetByTag_Unknown_ReturnsEmpty()
    {
        var result = ModelDatabase.GetByTag("unknown-tag");

        Assert.Empty(result);
    }

    [Fact]
    public void GetByTag_CaseInsensitive()
    {
        var resultLower = ModelDatabase.GetByTag("chinese");
        var resultUpper = ModelDatabase.GetByTag("CHINESE");

        Assert.Equal(resultLower.Count(), resultUpper.Count());
    }

    #endregion

    #region GetById

    [Fact]
    public void GetById_ExistingId_ReturnsModel()
    {
        var result = ModelDatabase.GetById("qwen2.5-7b");

        Assert.NotNull(result);
        Assert.Equal("qwen2.5-7b", result.Id);
    }

    [Fact]
    public void GetById_UnknownId_ReturnsNull()
    {
        var result = ModelDatabase.GetById("unknown-model");

        Assert.Null(result);
    }

    [Fact]
    public void GetById_CaseInsensitive()
    {
        var resultLower = ModelDatabase.GetById("qwen2.5-7b");
        var resultUpper = ModelDatabase.GetById("QWEN2.5-7B");

        Assert.Equal(resultLower, resultUpper);
    }

    [Fact]
    public void GetById_Empty_ReturnsNull()
    {
        var result = ModelDatabase.GetById("");

        Assert.Null(result);
    }

    #endregion

    #region GetByOllamaName

    [Fact]
    public void GetByOllamaName_ExistingName_ReturnsModel()
    {
        var result = ModelDatabase.GetByOllamaName("qwen2.5:7b");

        Assert.NotNull(result);
        Assert.Equal("qwen2.5:7b", result.OllamaModelName);
    }

    [Fact]
    public void GetByOllamaName_UnknownName_ReturnsNull()
    {
        var result = ModelDatabase.GetByOllamaName("unknown:model");

        Assert.Null(result);
    }

    [Fact]
    public void GetByOllamaName_CaseInsensitive()
    {
        var resultLower = ModelDatabase.GetByOllamaName("qwen2.5:7b");
        var resultUpper = ModelDatabase.GetByOllamaName("QWEN2.5:7B");

        Assert.Equal(resultLower, resultUpper);
    }

    #endregion

    #region GetByLmStudioName

    [Fact]
    public void GetByLmStudioName_ExistingName_ReturnsModel()
    {
        var result = ModelDatabase.GetByLmStudioName("qwen2.5-7b-instruct");

        Assert.NotNull(result);
        Assert.Equal("qwen2.5-7b-instruct", result.LmStudioSearchName);
    }

    [Fact]
    public void GetByLmStudioName_UnknownName_ReturnsNull()
    {
        var result = ModelDatabase.GetByLmStudioName("unknown-model");

        Assert.Null(result);
    }

    [Fact]
    public void GetByLmStudioName_Null_ReturnsNull()
    {
        var result = ModelDatabase.GetByLmStudioName(null!);

        Assert.Null(result);
    }

    #endregion

    #region ModelEntry Properties

    [Fact]
    public void ModelEntry_DefaultValues_AreCorrect()
    {
        var entry = new ModelEntry();

        Assert.Equal("", entry.Id);
        Assert.Equal("", entry.Name);
        Assert.Equal("", entry.OllamaModelName);
        Assert.Null(entry.LmStudioSearchName);
        Assert.Null(entry.HuggingFaceRepo);
        Assert.Null(entry.GgufFilename);
        Assert.Equal("", entry.Description);
        Assert.Equal("", entry.ParameterSize);
        Assert.Equal("", entry.Quantization);
        Assert.Equal(0, entry.SizeGiB);
        Assert.Null(entry.MinVramGiB);
        Assert.Equal(0, entry.MinRamGiB);
        Assert.Empty(entry.Tags);
        Assert.Equal("", entry.Company);
    }

    [Fact]
    public void ModelEntry_CanSetProperties()
    {
        var entry = new ModelEntry
        {
            Id = "test-model",
            Name = "Test Model",
            OllamaModelName = "test:model",
            LmStudioSearchName = "test-model-instruct",
            HuggingFaceRepo = "test/repo",
            GgufFilename = "test.gguf",
            Description = "Test description",
            ParameterSize = "7B",
            Quantization = "Q4_K_M",
            SizeGiB = 4.5,
            MinVramGiB = 6.0,
            MinRamGiB = 8.0,
            Tags = new List<string> { "chat", "code" },
            Company = "TestCompany"
        };

        Assert.Equal("test-model", entry.Id);
        Assert.Equal("Test Model", entry.Name);
        Assert.Equal("test:model", entry.OllamaModelName);
        Assert.Equal("test-model-instruct", entry.LmStudioSearchName);
        Assert.Equal("test/repo", entry.HuggingFaceRepo);
        Assert.Equal("test.gguf", entry.GgufFilename);
        Assert.Equal("Test description", entry.Description);
        Assert.Equal("7B", entry.ParameterSize);
        Assert.Equal("Q4_K_M", entry.Quantization);
        Assert.Equal(4.5, entry.SizeGiB);
        Assert.Equal(6.0, entry.MinVramGiB);
        Assert.Equal(8.0, entry.MinRamGiB);
        Assert.Equal(2, entry.Tags.Count);
        Assert.Equal("TestCompany", entry.Company);
    }

    #endregion
}