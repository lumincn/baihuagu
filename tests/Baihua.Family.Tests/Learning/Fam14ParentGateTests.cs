using System.Text.RegularExpressions;
using Xunit;

namespace Baihua.Family.Tests.Learning;

/// <summary>
/// FAM-14 红测试：家长模式门控（openclaw 菜单项能力门控）。
///
/// 验收标准：
///   - 无对应 capability 的用户 → 任何场景下 openclaw 菜单不可见
///   - 管理员/开发者 → openclaw 菜单正常显示（回归）
///
/// 红测试方式：NavMenuItem 注册表是 FamilyNavMenu.razor 的私有静态数据，测试项目不引用
/// Baihua.Web 组件程序集，采用**源码级契约测试**锁定注册表行为：
///   1) 契约红：openclaw 菜单项必须声明 RequiredFeature（当前无 → 红）
///   2) 回归锚：openclaw 菜单项仍注册在菜单中（防误删）
///
/// 门控渲染机制（_featureVisibility + visibleItems 过滤 RequiredFeature）已存在于
/// FamilyNavMenu.razor；本测试锁定"openclaw 必须挂上门控"这一注册表契约。
/// CapabilityService 的 mock 渲染测试需组件测试框架（bUnit），超出本任务 0.5h 范围，
/// 列为后续可选增强。
/// </summary>
public class Fam14ParentGateTests
{
    private static readonly string NavMenuPath = RepoPath.FindUp(Path.Combine(
        "services", "Baihua.Web", "Shared", "FamilyNavMenu.razor"));

    private static string[] ReadMenuLines()
    {
        return File.ReadAllLines(NavMenuPath);
    }

    /// <summary>提取 openclaw 菜单项注册行（AllItems 数组中的 new(...) 行）</summary>
    private static string? FindOpenClawMenuItemLine(string[] lines)
    {
        return lines.FirstOrDefault(l =>
            l.Contains("\"openclaw\"", StringComparison.OrdinalIgnoreCase) &&
            l.Contains("new(", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OpenClawMenuItem_MustDeclareRequiredFeatureGate()
    {
        var lines = ReadMenuLines();
        var itemLine = FindOpenClawMenuItemLine(lines);

        Assert.NotNull(itemLine);
        // 契约：openclaw 菜单项必须声明 RequiredFeature 能力门控
        // （当前注册行 `new("openclaw", "OpenClaw", "bi bi-robot", 1)` 无 RequiredFeature → 红）
        Assert.Contains("RequiredFeature", itemLine,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OpenClawMenuItem_StillRegisteredInMenu()
    {
        var lines = ReadMenuLines();
        // 回归锚：门控不等于移除——菜单项仍必须存在（管理员可见）
        Assert.NotNull(FindOpenClawMenuItemLine(lines));
    }
}
