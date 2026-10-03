/// <summary>
/// 附身会话的运行时关联（审视 #7 拆自 EntityBrain 的私有嵌套类，改顶级 internal——
/// 存储落在双方 Entity.ActivePossession 上，Brain 侧不再持有异质状态）。
/// Buff 是持续状态的唯一真相（Possessed 载体 / SoulOut 标记），本关联只负责控制权归还：
/// 倒计时由 ModifierList 的 Duration 免费提供；会话存续判定 = 双方 buff 仍在场。
/// 结束顺序：先释放目标，后恢复原角色；原角色已死则死亡观战（DeathView）。
/// </summary>
internal sealed class PossessionLink
{
    public readonly EntityBrain Origin;
    public readonly EntityBrain Target;
    public readonly PossessionEffect Carrier;
    public readonly PossessionEffect Marker;
    public readonly IInputSource? OriginalInput;
    public readonly IInputSource? TargetInput;
    private bool Ended;

    public PossessionLink(EntityBrain origin, EntityBrain target, PossessionEffect carrier, PossessionEffect marker)
    {
        Origin = origin;
        Target = target;
        Carrier = carrier;
        Marker = marker;
        OriginalInput = origin.InputSource;
        TargetInput = target.InputSource;
    }

    public bool IsValid => !Ended && Origin != null && Target != null
        && Origin.Entity != null && Target.Entity != null
        && Origin.isActiveAndEnabled && Target.isActiveAndEnabled
        && !Origin.Entity.IsDead && !Target.Entity.IsDead
        && Origin.Modifiers.IsHolding(Marker) && Target.Modifiers.IsHolding(Carrier);

    public void End()
    {
        if (Ended) return;
        Ended = true;
        // 先清关联，避免死亡/禁用回调重复收尾；先释放目标，后恢复原角色。
        // link 只存在于双方 Bootstrap 之后，Entity 必非空（`!` 为断言）。
        if (Target != null)
        {
            Target.Entity!.ActivePossession = null;
            Target.Modifiers.Remove(Carrier);
            Target.BindInputSource(TargetInput);
        }
        if (Origin != null)
        {
            Origin.Entity!.ActivePossession = null;
            Origin.Modifiers.Remove(Marker);
            bool dead = Origin.Entity == null || Origin.Entity.IsDead;
            Origin.Entity!.DeathView = dead && OriginalInput is PlayerInputSource;
            Origin.BindInputSource(dead ? Origin.AiSource : OriginalInput);
        }
    }
}
