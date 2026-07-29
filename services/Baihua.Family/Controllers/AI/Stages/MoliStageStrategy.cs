namespace Baihua.Family.Controllers.AI.Stages;

/// <summary>
/// 「磨砺」阶段策略 — 考官：模拟实战，查漏补缺。
/// </summary>
public class MoliStageStrategy : StageStrategyBase
{
    public override string StageName => "磨砺";
    public override int Order => 4;
    public override string RoleName => "考官";

    public override string[] BlessingTemplates =>
    [
        "{name}严肃道：百炼成钢，你已堪一战。",
        "{name}点头道：模拟虽苦，实战方从容。",
        "{name}正色道：考场如战场，你已备甲胄。"
    ];
}
