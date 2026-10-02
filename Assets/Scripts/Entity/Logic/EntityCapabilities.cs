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
    private readonly Entity Entity;

    /// <summary>宿主状态机（唯一解引用点）：Brain 未装配时用空条件运算符收敛。
    /// 收在属性里而不是每处写法不同，是让 null 分析有单点可依</summary>
    private EntityStateMachine? Machine => Entity.Brain?.StateMachine;

    public EntityCapabilities(Entity entity)
    {
        Entity = entity;
    }

    /// <summary>能否移动：活着 && 未失控 && 无锁移动声明（攻击/闪避期间）</summary>
    public bool CanMove()
    {
        if (Entity.Vitals.IsDead || IsControlled())
        {
            return false;
        }
        EntityState? actionState = Machine?.GetActive(EnumStateLayer.Action);
        return actionState == null || !actionState.LocksMovement;
    }

    /// <summary>能否起手主动动作（攻击/技能）：活着 && 未失控 && Action 层空闲。
    /// 连段续击不查这里（续段是 AttackState 内部事务，不走起手门禁）</summary>
    public bool CanAct()
    {
        return CanUseSkill();
    }

    /// <summary>技能共用释放门禁；允许被控制时释放的技能必须显式配置例外。</summary>
    public bool CanUseSkill(bool allowWhileControlled = false)
    {
        return !Entity.Vitals.IsDead && (allowWhileControlled || !IsControlled())
            && Machine?.GetActive(EnumStateLayer.Action) == null;
    }

    /// <summary>普通技能统一查询：通用能力 + 数据条件；附身复用 CanUseSkill。</summary>
    public bool CanCastSkill(Assets.Scripts.Entity.Data.Skill.EnumSkillType kind)
    {
        return Entity.Slots.Skills.TryGet(kind, out SkillSO? skill, out _) && skill != null
            && CanUseSkill(skill.AllowWhileControlled)
            && Entity.Slots.Skills.CanCast(kind, Entity.Vitals.Faith);
    }

    /// <summary>能否跳跃：活着 && 未失控 && 在地面（冲量类效果都要过 CC 门禁，
    /// 否则会绕过状态机的层压制——旧管线的教训）</summary>
    public bool CanJump()
    {
        return !Entity.Vitals.IsDead && !IsControlled() && Entity.Motor.IsGrounded;
    }

    /// <summary>是否处于失控（CrowdControl 层有活跃状态 = 被 CC 压制中）</summary>
    public bool IsControlled()
    {
        return Machine?.GetActive(EnumStateLayer.CrowdControl) != null;
    }

    /// <summary>控制免疫（霸体）：施加失控类 Modifier 时的仲裁入口。
    /// 当前 = 任一活跃状态声明霸体；将来装备/Buff 常驻霸体在此或运算。
    /// 注意：免疫 ≠ 解控——只拦"新施加的控制成分"，不清已生效的</summary>
    public bool HasControlImmunity()
    {
        return Machine?.HasSuperArmor() == true;
    }
}
