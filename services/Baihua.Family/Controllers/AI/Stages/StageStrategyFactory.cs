namespace Baihua.Family.Controllers.AI.Stages;

/// <summary>
/// 阶段策略工厂 — 根据阶段名称获取对应的策略实例。
/// 新增阶段只需添加新的策略类和此处的映射即可。
/// </summary>
public static class StageStrategyFactory
{
    private static readonly IReadOnlyDictionary<string, IStageStrategy> Strategies;
    private static readonly IReadOnlyList<IStageStrategy> OrderedStrategies;

    static StageStrategyFactory()
    {
        var list = new List<IStageStrategy>
        {
            new RudaoStageStrategy(),
            new ZhujiStageStrategy(),
            new JingjinStageStrategy(),
            new MoliStageStrategy(),
            new ChushiStageStrategy()
        };
        OrderedStrategies = list.OrderBy(s => s.Order).ToList().AsReadOnly();
        Strategies = list.ToDictionary(s => s.StageName);
    }

    /// <summary>
    /// 获取指定阶段的策略。如果未找到，返回 null。
    /// </summary>
    public static IStageStrategy? GetStrategy(string stageName)
    {
        return Strategies.GetValueOrDefault(stageName);
    }

    /// <summary>
    /// 获取指定阶段之后的下一阶段策略。如果已是最后阶段，返回 null。
    /// </summary>
    public static IStageStrategy? GetNextStrategy(string stageName)
    {
        if (!Strategies.TryGetValue(stageName, out var current))
            return null;

        var nextOrder = current.Order + 1;
        return OrderedStrategies.FirstOrDefault(s => s.Order == nextOrder);
    }

    /// <summary>
    /// 获取所有已注册的阶段策略，按顺序排列。
    /// </summary>
    public static IReadOnlyList<IStageStrategy> GetAllStrategies() => OrderedStrategies;
}
