/// <summary>
/// 滞空：跳跃上升 / 下落 / 走落悬崖。
/// 速度选择与着地时相同（同旧实现：加速生效就用加速速度，与是否着地无关）。
/// </summary>
public sealed class PlayerAirState : PlayerStateBase
{
    public PlayerAirState(PlayerBlackboard board, StateMachine<PlayerBlackboard> machine) : base(board, machine) { }

    public override string StateName => "Air";

    public override void HandleTransitions()
    {
        // 落地的同一帧又按了跳：直接续跳（冲量稍后由 TryConsumeJump 施加）。
        // 停留 Air 避免 1 帧 Walk 闪跳；起跳后竖直速度 > 0，也不会被下面的落地判定误触
        if (Board.IsGrounded && Board.JumpQueued)
        {
            return;
        }

        // 落地（UpdateGroundCheck 已把贴地竖直速度钳到 -2，起跳后帧 v > 0，无误判）：
        // 按加速标志和移动输入分流
        if (Board.IsGrounded && Board.VerticalVelocity < 0f)
        {
            if (Board.SprintActive && Board.HasMoveInput)
            {
                Machine.ChangeState(Board.Controller.SprintState);
            }
            else if (Board.HasMoveInput)
            {
                Machine.ChangeState(Board.Controller.WalkState);
            }
            else
            {
                Machine.ChangeState(Board.Controller.IdleState);
            }
        }
    }

    public override void Tick()
    {
        float speed = Board.SprintActive
            ? Board.Controller.WalkSpeed * Board.Controller.SprintMultiplier
            : Board.Controller.WalkSpeed;
        Board.Controller.ApplyHorizontalMovement(speed);
    }
}
