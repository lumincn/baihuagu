using Microsoft.JSInterop;
using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace Baihua.Web.Services;

/// <summary>
/// 管理语言/文化设置，支持中英文切换
/// 默认使用中文 (zh-CN)
/// </summary>
public class CultureService
{
    private const string StorageKey = "bh_preferred_culture";
    private readonly IJSRuntime _jsRuntime;
    private readonly NavigationManager _navigation;

    public CultureService(IJSRuntime jsRuntime, NavigationManager navigation)
    {
        _jsRuntime = jsRuntime;
        _navigation = navigation;
    }

    /// <summary>
    /// 初始化时从 localStorage 读取上次选中的语言，若未设置则默认 zh-CN
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            var stored = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", StorageKey);
            var culture = string.IsNullOrEmpty(stored) ? "zh-CN" : stored;
            SetCulture(culture);
        }
        catch
        {
            // 首次运行或 JS 不可用时使用默认中文
            SetCulture("zh-CN");
        }
    }

    /// <summary>
    /// 切换到指定语言并刷新页面
    /// </summary>
    public async Task SwitchToAsync(string culture)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, culture);
        }
        catch { }
        SetCulture(culture);
        _navigation.NavigateTo(_navigation.Uri, forceLoad: true);
    }

    /// <summary>
    /// 获取当前语言显示名称
    /// </summary>
    public string GetCurrentLanguageName()
    {
        var culture = CultureInfo.CurrentUICulture.Name;
        return culture switch
        {
            "zh-CN" => "中文",
            "en" => "English",
            _ => culture
        };
    }

    /// <summary>
    /// 获取当前语言代码
    /// </summary>
    public string GetCurrentCulture() => CultureInfo.CurrentUICulture.Name;

    /// <summary>
    /// 可切换的语言列表
    /// </summary>
    public static (string Code, string Name)[] SupportedCultures => new[]
    {
        ("zh-CN", "中文"),
        ("en", "English")
    };

    private static void SetCulture(string culture)
    {
        var ci = new CultureInfo(culture);
        CultureInfo.DefaultThreadCurrentCulture = ci;
        CultureInfo.DefaultThreadCurrentUICulture = ci;
    }
}

