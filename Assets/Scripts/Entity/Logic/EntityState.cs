using UnityEngine;

/// <summary>
/// 状态基类（Logic 层，全实体唯一保留的继承点——多态分发的"万不得已"：
/// 机器需要统一句柄存层槽、需要 Enter/Exit 生命周期钩子、七状态共享 Entity 引用与移动助手。
/// 组件层面零继承的架构里，这里是刻意豁免的唯一一层（对抗审查裁决，深 1：abstract → sealed 状态）。
/// 状态实例在 EntityBrain.Bootstrap 构造一次，运行期零 new。
/// Enter/Exit 是动画/特效的预留接口，保持为空——不得放移动逻辑，否则转换帧会双重移动。
/// 能力声明（GrantsSuperArmor/LocksMovement）是构造期设定的普通属性（非 virtual）：
/// 状态在 Bootstrap 里 new，配置天然在构造点——"声明能力，别处仲裁"。
/// </summary>
public abstract class EntityState
{
    /// <summary>本状态所属的层（同层互斥、异层叠加，见 EnumStateLayer）</summary>
    public abstract EnumStateLayer Layer { get; }

    /// <summary>状态名（调试显示用）</summary>
    public abstract string StateName { get; }

    /// <summary>动作型霸体：本状态活跃期间免疫 CrowdControl（眩晕/击退等）的进入。
    /// 构造期设定；随状态生命周期自动生效/失效，不会忘关。
    /// 注意：霸体是免疫不是解控——已生效的 CC 不因此清除；挡不挡伤害由命中入口决定。
    /// 同族修饰（无敌帧等）都走这个模式：状态声明能力，外部系统（Capability）在入口仲裁</summary>
    public bool GrantsSuperArmor { get; protected set; }

    /// <summary>声明"本状态活跃期间锁定移动"（攻击/闪避/吟唱等主动动作）。
    /// Locomotion 层的 ApplyLocomotion 消费此声明：站桩（方向清零 + 零速 Move 保留贴地），
    /// 状态退出自动恢复</summary>
    public bool LocksMovement { get; protected set; }

    /// <summary>本状态的宿主实体（门面聚合了全部组件读点）</summary>
    protected Entity Entity { get; }

    /// <summary>宿主的状态机（转换用）</summary>
    protected EntityStateMachine Machine { get; }

    protected EntityState(Entity entity, EntityStateMachine machine)
    {
        Entity = entity;
        Machine = machine;
    }

    /// <summary>进入状态时调用（动画/特效预留）</summary>
    public virtual void Enter() { }

    /// <summary>离开状态时调用（动画/特效预留）</summary>
    public virtual void Exit() { }

    /// <summary>纯转换判定，只允许调用 Machine.ChangeState / ClearState，不做移动</summary>
    public abstract void HandleTransitions();

    /// <summary>本帧动作（选速度 + 调 Motor）。deltaTime 入参（网络时间纪律）</summary>
    public abstract void Tick(float deltaTime);

    /// <summary>
    /// Locomotion 层统一移动出口（旧 NpcStateBase.ApplyLocomotion 平移）：
    /// Action 层有声明 LocksMovement 的状态（攻击/闪避）时站桩（清方向 + 零速 Move 保留贴地），
    /// 否则按传入速度 × 移速乘法链 × 出招表移速修正正常移动。
    /// Idle/Walk/Sprint 的 Tick 都走这里——攻击锁移动、效果修饰移速、持械移速修正都只在此一处生效。
    /// </summary>
    protected void ApplyLocomotion(float baseSpeed)
    {
        EntityState actionState = Machine.GetActive(EnumStateLayer.Action);
        if (actionState != null && actionState.LocksMovement)
        {
            Entity.Commands.MoveDirection = Vector3.zero;
            Entity.Motor.MoveHorizontal(0f, Vector3.zero);
            return;
        }

        WeaponComboGraph comboGraph = Entity.Slots.CurrentComboGraph;
        float comboGraphMultiplier = comboGraph != null ? comboGraph.MoveSpeedMultiplier : 1f;
        float finalSpeed = baseSpeed
            * comboGraphMultiplier
            * Entity.Brain.Modifiers.GetStatMultiplier(EnumStatType.MoveSpeed);
        Entity.Motor.MoveHorizontal(finalSpeed, Entity.Commands.MoveDirection);
    }
}
