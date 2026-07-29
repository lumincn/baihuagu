namespace TaskRunner.Controllers.AI.Stages;

/// <summary>
/// 「筑基」阶段策略 — 严师：严格要求，打牢基础。
/// </summary>
public class ZhujiStageStrategy : StageStrategyBase
{
    public override string StageName => "筑基";
    public override int Order => 2;
    public override string RoleName => "严师";

    public override string[] BlessingTemplates =>
    [
        "{name}欣慰道：根基已固，风雨不惧。",
        "{name}正色道：基础扎实，方可远行。",
        "{name}赞许道：功课不辍，根基日深。"
    ];
}
