using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Baihua.Data;

/// <summary>
/// FamilyDbContext 设计时工厂（dotnet ef migrations add 用）：
/// 迁移生成不需要运行任何服务进程，直接以默认路径构造上下文。
/// </summary>
public class FamilyDbContextFactory : IDesignTimeDbContextFactory<FamilyDbContext>
{
    public FamilyDbContext CreateDbContext(string[] args)
    {
        var dbPath = FamilyDbContext.GetDbPath();
        var options = new DbContextOptionsBuilder<FamilyDbContext>()
            .UseSqlite($"Data Source={dbPath};Foreign Keys=True;", sqlite => sqlite.MigrationsAssembly("Baihua.Data"))
            .Options;
        return new FamilyDbContext(options);
    }
}
