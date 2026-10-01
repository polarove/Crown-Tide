/// <summary>
/// 待机：无移动输入。加速开关可以处于开启状态，但没有输入就不移动（同旧实现）。
/// </summary>
public sealed class PlayerIdleState : PlayerStateBase
{
    public PlayerIdleState(PlayerBlackboard board, StateMachine<PlayerBlackboard> machine) : base(board, machine) { }

    public override string StateName => "Idle";

    public override void HandleTransitions()
    {
        if (CheckGroundedToAirTransitions())
        {
            return;
        }
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
        Board.Controller.ApplyHorizontalMovement(Board.Controller.WalkSpeed);
    }
}
