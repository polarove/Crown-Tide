/// <summary>
/// 玩家状态基类：绑定 PlayerBlackboard，并集中放置玩家状态共用的转换判定。
/// Enter/Exit 是动画/特效的预留接口，保持为空——不得放移动逻辑，否则转换帧会双重移动。
/// </summary>
public abstract class PlayerStateBase : State<PlayerBlackboard>
{
    protected PlayerStateBase(PlayerBlackboard board, StateMachine<PlayerBlackboard> machine)
        : base(board, machine)
    {
    }

    /// <summary>
    /// 地面状态（Idle/Walk/Sprint）共用判定：起跳或离地都转 Air。
    /// 已发生转换返回 true，调用方应立即 return。
    /// </summary>
    protected bool CheckGroundedToAirTransitions()
    {
        if (Board.JumpQueued && Board.IsGrounded)
        {
            // 起跳：跳跃冲量由本帧稍后的 TryConsumeJump 施加
            Machine.ChangeState(Board.Controller.AirState);
            return true;
        }
        if (!Board.IsGrounded)
        {
            // 走落悬崖 / 站台移走
            Machine.ChangeState(Board.Controller.AirState);
            return true;
        }
        return false;
    }
}
