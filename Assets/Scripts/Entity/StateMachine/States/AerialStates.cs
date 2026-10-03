/// <summary>
/// 着地（Aerial 层）：只负责起跳/离地的时机判定，不做任何移动——
/// 水平移动由 Locomotion 层负责，竖直物理由 Brain 管线（Motor）负责。
/// 旧 NpcGroundedState 平移。
/// </summary>
public sealed class EntityGroundedState : EntityState
{
    public EntityGroundedState(Entity entity, EntityStateMachine machine) : base(entity, machine) { }

    public override EnumStateLayer Layer => EnumStateLayer.Aerial;

    public override string StateName => "Grounded";

    public override void HandleTransitions()
    {
        // 起跳：跳跃冲量由本帧稍后的 Brain.TryConsumeJump 施加（玩家按键或 AI 决策排队都走这里）
        if (Entity.Commands.JumpQueued && Entity.Motor.IsGrounded)
        {
            Machine.ChangeState(Entity.Brain.AirState);
            return;
        }
        // 走落悬崖 / 站台移走
        if (!Entity.Motor.IsGrounded)
        {
            Machine.ChangeState(Entity.Brain.AirState);
        }
    }

    public override void Tick(float deltaTime)
    {
        // 无动作：着地时的水平/竖直移动都不归这层
    }
}

/// <summary>
/// 滞空（Aerial 层）：跳跃上升 / 下落 / 走落悬崖时活跃，只负责落地判定——
/// 水平移动由 Locomotion 层照常执行（空中保持地面速度），竖直物理由 Brain 管线负责。
/// 落地转回 Grounded 不经过 Locomotion 层，因此奔跑中落地不闪断。
/// 已知取舍：贴地连跳会短暂经过 Grounded（Enter/Exit 各一次），将来接动画时若闪落地动画，
/// 用"本帧已施加跳跃冲量"抑制即可。
/// </summary>
public sealed class EntityAirState : EntityState
{
    public EntityAirState(Entity entity, EntityStateMachine machine) : base(entity, machine) { }

    public override EnumStateLayer Layer => EnumStateLayer.Aerial;

    public override string StateName => "Air";

    public override void HandleTransitions()
    {
        // 落地（GroundCheck 已把贴地竖直速度钳到 -2，起跳后帧 v > 0，无误判）。
        // 落地帧若按了跳：Grounded 的起跳判定在下帧接手，运动等价（冲量由 Brain.TryConsumeJump 施加）
        if (Entity.Motor.IsGrounded && Entity.Motor.VerticalVelocity < 0f)
        {
            Machine.ChangeState(Entity.Brain.GroundedState);
        }
    }

    public override void Tick(float deltaTime)
    {
        // 无动作：空中的水平移动由 Locomotion 层负责（速度与地面一致）
    }
}
