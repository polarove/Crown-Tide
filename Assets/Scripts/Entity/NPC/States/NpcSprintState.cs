/// <summary>
/// 冲刺（Locomotion 层）：加速生效且有移动指令。速度 = WalkSpeed × SprintMultiplier。
/// 空中照常执行（保持跑速）——奔跑中跳跃落地无缝续跑，不经过 Idle/Walk。
/// </summary>
public sealed class NpcSprintState : NpcStateBase
{
    public NpcSprintState(NpcBlackboard board, StateMachine<NpcBlackboard> machine) : base(board, machine) { }

    public override StateLayer Layer => StateLayer.Locomotion;

    public override string StateName => "Sprint";

    public override void HandleTransitions()
    {
        if (!Board.SprintActive)
        {
            // 开关关掉 / 松开长按：按是否还在移动分流
            Machine.ChangeState(Board.HasMoveInput ? Board.Controller.WalkState : Board.Controller.IdleState);
            return;
        }
        if (!Board.HasMoveInput)
        {
            // 加速标志仍在，只是停下了；再动会经 Idle 的转换回到 Sprint
            Machine.ChangeState(Board.Controller.IdleState);
        }
    }

    public override void Tick()
    {
        ApplyLocomotion(Board.Controller.WalkSpeed * Board.Controller.SprintMultiplier);
    }
}
