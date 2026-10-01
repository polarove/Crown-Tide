/// <summary>
/// 行走：有移动输入、加速未生效。
/// </summary>
public sealed class PlayerWalkState : PlayerStateBase
{
    public PlayerWalkState(PlayerBlackboard board, StateMachine<PlayerBlackboard> machine) : base(board, machine) { }

    public override string StateName => "Walk";

    public override void HandleTransitions()
    {
        if (CheckGroundedToAirTransitions())
        {
            return;
        }
        if (Board.SprintActive && Board.HasMoveInput)
        {
            // 走路中开加速：两段式 Tick 保证转换当帧就用冲刺速度
            Machine.ChangeState(Board.Controller.SprintState);
            return;
        }
        if (!Board.HasMoveInput)
        {
            Machine.ChangeState(Board.Controller.IdleState);
        }
    }

    public override void Tick()
    {
        Board.Controller.ApplyHorizontalMovement(Board.Controller.WalkSpeed);
    }
}
