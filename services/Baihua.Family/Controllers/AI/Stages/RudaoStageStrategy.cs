namespace Baihua.Family.Controllers.AI.Stages;

/// <summary>
/// 「入道」阶段策略 — 引路人：温和引导，了解基础，建立学习方向。
/// </summary>
public class RudaoStageStrategy : StageStrategyBase
{
    public override string StageName => "入道";
    public override int Order => 1;
    public override string RoleName => "引路人";

    public override string[] BlessingTemplates =>
    [
        "{name}微微一笑：你已迈出第一步，路虽远，行则将至。",
        "{name}点头道：基础已定，前路可期。",
        "{name}轻声道：入门虽易，守道方难，望你持之。"
    ];
}
