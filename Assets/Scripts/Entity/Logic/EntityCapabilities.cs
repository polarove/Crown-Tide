/// <summary>
/// 能力仲裁（Logic 层，纯 C#）："能不能"的统一查询口（需求钦定：状态机能不能流转、
/// 动作/技能能不能执行、Modifier 能不能套上，由 Capability 统一查询）。
/// 旧代码散落三处"CC 层是否活跃"门禁（跳跃/攻击/技能）全部收编于此——
/// 将来加新门禁（装备要求/资源不足等）改这里，调用点不动。
/// 也是霸体合成的汇合点：状态霸体（HasSuperArmor）|| 将来装备/Buff 霸体。
/// Brain 构造一次持有；不写任何状态，纯查询。
/// </summary>
public sealed class EntityCapabilities
{
    private readonly Entity entity;

    public EntityCapabilities(Entity entity)
    {
        this.entity = entity;
    }

    /// <summary>能否移动：活着 && 未失控 && 无锁移动声明（攻击/闪避期间）</summary>
    public bool CanMove()
    {
        if (entity.Vitals.IsDead || IsControlled())
        {
            return false;
        }
        EntityState actionState = entity.Brain.Machine.GetActive(EnumStateLayer.Action);
        return actionState == null || !actionState.LocksMovement;
    }

    /// <summary>能否起手主动动作（攻击/技能）：活着 && 未失控 && Action 层空闲。
    /// 连段续击不查这里（续段是 AttackState 内部事务，不走起手门禁）</summary>
    public bool CanAct()
    {
        return !entity.Vitals.IsDead && !IsControlled()
            && entity.Brain.Machine.GetActive(EnumStateLayer.Action) == null;
    }

    /// <summary>能否跳跃：活着 && 未失控 && 在地面（冲量类效果都要过 CC 门禁，
    /// 否则会绕过状态机的层压制——旧管线的教训）</summary>
    public bool CanJump()
    {
        return !entity.Vitals.IsDead && !IsControlled() && entity.Motor.IsGrounded;
    }

    /// <summary>是否处于失控（CrowdControl 层有活跃状态 = 被 CC 压制中）</summary>
    public bool IsControlled()
    {
        return entity.Brain.Machine.GetActive(EnumStateLayer.CrowdControl) != null;
    }

    /// <summary>控制免疫（霸体）：施加失控类 Modifier 时的仲裁入口。
    /// 当前 = 任一活跃状态声明霸体；将来装备/Buff 常驻霸体在此或运算。
    /// 注意：免疫 ≠ 解控——只拦"新施加的控制成分"，不清已生效的</summary>
    public bool HasControlImmunity()
    {
        return entity.Brain.Machine.HasSuperArmor();
    }
}
