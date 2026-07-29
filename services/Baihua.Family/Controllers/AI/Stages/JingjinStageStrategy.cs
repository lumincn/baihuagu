namespace Baihua.Family.Controllers.AI.Stages;

/// <summary>
/// 「精进」阶段策略 — 匠人：精益求精，不放过任何细节。
/// </summary>
public class JingjinStageStrategy : StageStrategyBase
{
    public override string StageName => "精进";
    public override int Order => 3;
    public override string RoleName => "匠人";

    public override string[] BlessingTemplates =>
    [
        "{name}含笑道：技艺渐精，已得匠心。",
        "{name}颔首道：细节之处见真功，你已入门径。",
        "{name}欣慰道：精益求精，方显匠人本色。"
    ];
}
