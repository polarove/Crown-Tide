using UnityEngine;

/// <summary>
/// 待机（Locomotion 层）：无移动指令。加速开关可以处于开启状态，但没有指令就不移动。
/// 旧 NpcIdleState 平移（指令读点从黑板换 CommandBuffer）。
/// </summary>
public sealed class EntityIdleState : EntityState
{
    public EntityIdleState(Entity entity, EntityStateMachine machine) : base(entity, machine) { }

    public override EnumStateLayer Layer => EnumStateLayer.Locomotion;

    public override string StateName => "Idle";

    public override void HandleTransitions()
    {
        CommandBuffer commands = Entity.Commands;
        if (commands.SprintActive && commands.HasMoveInput)
        {
            Machine.ChangeState(Entity.Brain.SprintState);
            return;
        }
        if (commands.HasMoveInput)
        {
            Machine.ChangeState(Entity.Brain.WalkState);
        }
    }

    public override void Tick(float deltaTime)
    {
        // 方向为零也保持无条件 Move（CharacterController 依赖 Move 做贴地/去穿插）
        ApplyLocomotion(Entity.Motor.WalkSpeed);
    }
}

/// <summary>
/// 行走（Locomotion 层）：有移动指令、加速未生效。空中照常执行（保持走速）。
/// 玩家推杆与 AI 追击都落在这一个状态——指令不区分来源。
/// </summary>
public sealed class EntityWalkState : EntityState
{
    public EntityWalkState(Entity entity, EntityStateMachine machine) : base(entity, machine) { }

    public override EnumStateLayer Layer => EnumStateLayer.Locomotion;

    public override string StateName => "Walk";

    public override void HandleTransitions()
    {
        CommandBuffer commands = Entity.Commands;
        if (commands.SprintActive && commands.HasMoveInput)
        {
            // 走路中开加速：两段式 Tick 保证转换当帧就用冲刺速度
            Machine.ChangeState(Entity.Brain.SprintState);
            return;
        }
        if (!commands.HasMoveInput)
        {
            Machine.ChangeState(Entity.Brain.IdleState);
        }
    }

    public override void Tick(float deltaTime)
    {
        ApplyLocomotion(Entity.Motor.WalkSpeed);
    }
}

/// <summary>
/// 冲刺（Locomotion 层）：加速生效且有移动指令。速度 = WalkSpeed × SprintMultiplier。
/// 空中照常执行（保持跑速）——奔跑中跳跃落地无缝续跑，不经过 Idle/Walk。
/// Entity.IsSprinting 据本状态活跃判定（CameraRig 读它做奔跑加速的呈现）。
/// </summary>
public sealed class EntitySprintState : EntityState
{
    public EntitySprintState(Entity entity, EntityStateMachine machine) : base(entity, machine) { }

    public override EnumStateLayer Layer => EnumStateLayer.Locomotion;

    public override string StateName => "Sprint";

    public override void HandleTransitions()
    {
        CommandBuffer commands = Entity.Commands;
        if (!commands.SprintActive)
        {
            // 开关关掉 / 松开长按：按是否还在移动分流
            Machine.ChangeState(commands.HasMoveInput ? Entity.Brain.WalkState : Entity.Brain.IdleState);
            return;
        }
        if (!commands.HasMoveInput)
        {
            // 加速标志仍在，只是停下了；再动会经 Idle 的转换回到 Sprint
            Machine.ChangeState(Entity.Brain.IdleState);
        }
    }

    public override void Tick(float deltaTime)
    {
        ApplyLocomotion(Entity.Motor.WalkSpeed * Entity.Motor.SprintMultiplier);
    }
}
