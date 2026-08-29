using Baihua.Data;
using Baihua.Family.Services.Medical;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baihua.Family.Tests.Services;

/// <summary>
/// 家庭病历本服务测试：成员档案 / 病历记录（症状/诊断/用药 JSON 往返）/ AI 诊断 / 级联删除。
/// 使用内存 SQLite（now() 由 TestSqliteDb 注册），不触碰真实数据库。
/// </summary>
public class MedicalServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<FamilyDbContext> _factory;

    public MedicalServiceTests()
    {
        _connection = TestDoubles.TestSqliteDb.OpenInMemory();
        var options = new DbContextOptionsBuilder<FamilyDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new FamilyDbContext(options))
        {
            db.Database.EnsureCreated();
        }
        _factory = new FixedOptionsFactory(options);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private MedicalService CreateService() => new(_factory, Mock.Of<ILogger<MedicalService>>());

    [Fact]
    public async Task CreateAndGetMember_JsonListsRoundTrip()
    {
        var service = CreateService();

        var member = await service.CreateMemberAsync(
            "小明", "男", new DateTime(2015, 5, 20), "B",
            new[] { "青霉素过敏", "花生过敏" }, new[] { "哮喘" }, "注意花粉季节",
            CancellationToken.None);
        Assert.NotNull(member);

        var loaded = await service.GetMemberAsync(member!.Id, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal("小明", loaded!.Name);
        Assert.Equal("男", loaded.Gender);
        Assert.Equal(new DateTime(2015, 5, 20).Date, loaded.BirthDate!.Value.Date);

        var allergies = MedicalService.DeserializeStringList(loaded.AllergiesJson);
        Assert.Equal(new[] { "青霉素过敏", "花生过敏" }, allergies);
        Assert.Equal(new[] { "哮喘" }, MedicalService.DeserializeStringList(loaded.ChronicDiseasesJson));
    }

    [Fact]
    public async Task CreateMember_EmptyName_ReturnsNull()
    {
        var service = CreateService();
        var member = await service.CreateMemberAsync("  ", "", null, "", new List<string>(), new List<string>(), null, CancellationToken.None);
        Assert.Null(member);
    }

    [Fact]
    public async Task UpdateMember_ChangeNameAndClearAllergies()
    {
        var service = CreateService();
        var member = await service.CreateMemberAsync(
            "小红", "女", null, "A", new[] { "海鲜过敏" }, new List<string>(), null, CancellationToken.None);
        Assert.NotNull(member);

        var updated = await service.UpdateMemberAsync(
            member!.Id, "小红红", null, null, null, new List<string>(), null, null, CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal("小红红", updated!.Name);
        Assert.Empty(MedicalService.DeserializeStringList(updated.AllergiesJson));
    }

    [Fact]
    public async Task CreateRecord_MedicationsRoundTrip()
    {
        var service = CreateService();
        var member = await service.CreateMemberAsync("爸爸", "男", null, "O", new List<string>(), new List<string>(), null, CancellationToken.None);
        Assert.NotNull(member);

        var record = await service.CreateRecordAsync(
            member!.Id, new DateTime(2026, 8, 1), "感冒发烧",
            new[] { "发热 38.5℃", "流鼻涕" }, new[] { "上呼吸道感染" },
            new[] { ("布洛芬", "每次 1 片", "每日 3 次", "饭后服用"), ("", "x", "y", "z") }, // 空药名应被过滤
            "市一医院 内科 张医生", CancellationToken.None);
        Assert.NotNull(record);

        var loaded = await service.GetRecordAsync(record!.Id, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(new[] { "发热 38.5℃", "流鼻涕" }, MedicalService.DeserializeStringList(loaded!.SymptomsJson));
        Assert.Equal(new[] { "上呼吸道感染" }, MedicalService.DeserializeStringList(loaded.DiagnosesJson));

        var meds = MedicalService.DeserializeMedications(loaded.MedicationsJson);
        Assert.Single(meds);
        Assert.Equal("布洛芬", meds[0].Name);
        Assert.Equal("每次 1 片", meds[0].Dosage);
        Assert.Equal("每日 3 次", meds[0].Frequency);
        Assert.Equal("饭后服用", meds[0].Note);

        var records = await service.GetRecordsAsync(member.Id, CancellationToken.None);
        Assert.Single(records);
    }

    [Fact]
    public async Task DeleteMember_CascadesRecordsAndDiagnoses()
    {
        var service = CreateService();
        var member = await service.CreateMemberAsync("奶奶", "女", null, "", new List<string>(), new[] { "高血压" }, null, CancellationToken.None);
        Assert.NotNull(member);

        await service.CreateRecordAsync(member!.Id, DateTime.Now, "头晕", new[] { "头晕" }, new List<string>(), new List<(string, string?, string?, string?)>(), null, CancellationToken.None);
        await service.SaveDiagnosisAsync(member.Id, "最近头晕", "**可能原因**：血压波动。", ct: CancellationToken.None);

        Assert.True(await service.DeleteMemberAsync(member.Id, CancellationToken.None));
        Assert.Null(await service.GetMemberAsync(member.Id, CancellationToken.None));
        Assert.Empty(await service.GetRecordsAsync(member.Id, CancellationToken.None));
        Assert.Empty(await service.GetDiagnosesAsync(member.Id, CancellationToken.None));
    }

    [Fact]
    public async Task SaveDiagnosis_ThenDelete()
    {
        var service = CreateService();
        var member = await service.CreateMemberAsync("妈妈", "女", null, "", new List<string>(), new List<string>(), null, CancellationToken.None);
        Assert.NotNull(member);

        var diagnosis = await service.SaveDiagnosisAsync(member!.Id, "咳嗽三天", "**仅供参考**", ct: CancellationToken.None);
        var list = await service.GetDiagnosesAsync(member.Id, CancellationToken.None);
        Assert.Single(list);
        Assert.Equal("咳嗽三天", list[0].SymptomText);

        Assert.True(await service.DeleteDiagnosisAsync(diagnosis.Id, CancellationToken.None));
        Assert.Empty(await service.GetDiagnosesAsync(member.Id, CancellationToken.None));
        Assert.False(await service.DeleteDiagnosisAsync(diagnosis.Id, CancellationToken.None));
    }

    private sealed class FixedOptionsFactory(DbContextOptions<FamilyDbContext> options) : IDbContextFactory<FamilyDbContext>
    {
        public FamilyDbContext CreateDbContext() => new(options);
        public Task<FamilyDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
