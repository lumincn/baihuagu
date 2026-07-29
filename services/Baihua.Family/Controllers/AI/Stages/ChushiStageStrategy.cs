namespace Baihua.Family.Controllers.AI.Stages;

/// <summary>
/// 「出师」阶段策略 — 前辈：实战建议、考前冲刺。
/// </summary>
public class ChushiStageStrategy : StageStrategyBase
{
    public override string StageName => "出师";
    public override int Order => 5;
    public override string RoleName => "前辈";

    public override string[] BlessingTemplates =>
    [
        "{name}长揖道：吾徒已成，前路珍重。",
        "{name}含泪道：青出于蓝，不负所望。",
        "{name}微笑道：山高路远，愿你前程似锦。"
    ];
}
