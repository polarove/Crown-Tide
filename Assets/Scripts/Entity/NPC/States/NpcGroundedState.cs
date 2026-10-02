/// <summary>
/// 着地（Aerial 层）：只负责起跳/离地的时机判定，不做任何移动——
/// 水平移动由 Locomotion 层负责，竖直物理由控制器管线负责。
/// </summary>
public sealed class NpcGroundedState : NpcStateBase
{
    public NpcGroundedState(NpcBlackboard board, StateMachine<NpcBlackboard> machine) : base(board, machine) { }

    public override StateLayer Layer => StateLayer.Aerial;

    public override string StateName => "Grounded";

    public override void HandleTransitions()
    {
        // 起跳：跳跃冲量由本帧稍后的 TryConsumeJump 施加（玩家按键或 AI 决策排队都走这里）
        if (Board.JumpQueued && Board.IsGrounded)
        {
            Machine.ChangeState(Board.Controller.AirState);
            return;
        }
        // 走落悬崖 / 站台移走
        if (!Board.IsGrounded)
        {
            Machine.ChangeState(Board.Controller.AirState);
        }
    }

    public override void Tick()
    {
        // 无动作：着地时的水平/竖直移动都不归这层
    }
}
