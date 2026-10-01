/// <summary>
/// 冲刺：加速生效且有移动输入。速度 = WalkSpeed × SprintMultiplier。
/// </summary>
public sealed class PlayerSprintState : PlayerStateBase
{
    public PlayerSprintState(PlayerBlackboard board, StateMachine<PlayerBlackboard> machine) : base(board, machine) { }

    public override string StateName => "Sprint";

    public override void HandleTransitions()
    {
        if (CheckGroundedToAirTransitions())
        {
            return;
        }
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
        Board.Controller.ApplyHorizontalMovement(Board.Controller.WalkSpeed * Board.Controller.SprintMultiplier);
    }
}
