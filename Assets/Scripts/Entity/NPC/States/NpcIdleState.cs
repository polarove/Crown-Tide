/// <summary>
/// 待机（Locomotion 层）：无移动指令。加速开关可以处于开启状态，但没有指令就不移动。
/// </summary>
public sealed class NpcIdleState : NpcStateBase
{
    public NpcIdleState(NpcBlackboard board, StateMachine<NpcBlackboard> machine) : base(board, machine) { }

    public override StateLayer Layer => StateLayer.Locomotion;

    public override string StateName => "Idle";

    public override void HandleTransitions()
    {
        if (Board.SprintActive && Board.HasMoveInput)
        {
            Machine.ChangeState(Board.Controller.SprintState);
            return;
        }
        if (Board.HasMoveInput)
        {
            Machine.ChangeState(Board.Controller.WalkState);
        }
    }

    public override void Tick()
    {
        // 方向为零也保持无条件 Move（CharacterController 依赖 Move 做贴地/去穿插）
        ApplyLocomotion(Board.Controller.WalkSpeed);
    }
}
