using Microsoft.JSInterop;

namespace WebUI.Services;

/// <summary>
/// 用户类型服务 - 管理专业人员/非专业人员模式
/// 存储在 localStorage 中，纯前端偏好设置
/// </summary>
public class UserTypeService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<UserTypeService> _logger;
    private bool _initialized = false;

    public UserType CurrentType { get; private set; } = UserType.Beginner;

    public bool IsProfessional => CurrentType == UserType.Professional;
    public bool IsBeginner => CurrentType == UserType.Beginner;

    public event EventHandler? UserTypeChanged;

    public UserTypeService(IJSRuntime jsRuntime, ILogger<UserTypeService> logger)
    {
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    /// <summary>
    /// 初始化：从 localStorage 读取用户类型
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;
        try
        {
            var stored = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", "yj_user_type");
            if (!string.IsNullOrEmpty(stored) && Enum.TryParse<UserType>(stored, out var type))
            {
                CurrentType = type;
            }
            _initialized = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取用户类型失败，使用默认值");
        }
    }

    /// <summary>
    /// 设置用户类型
    /// </summary>
    public async Task SetUserTypeAsync(UserType type)
    {
        if (CurrentType == type) return;
        CurrentType = type;
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "yj_user_type", type.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "保存用户类型失败");
        }
        UserTypeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 检查是否已选择过用户类型
    /// </summary>
    public async Task<bool> HasSelectedAsync()
    {
        try
        {
            var stored = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", "yj_user_type");
            return !string.IsNullOrEmpty(stored);
        }
        catch
        {
            return false;
        }
    }
}

public enum UserType
{
    Beginner,      // 非专业人员 - 简化界面
    Professional   // 专业人员 - 完整功能
}

