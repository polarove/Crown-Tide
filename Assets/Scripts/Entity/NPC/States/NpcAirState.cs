/// <summary>
/// 滞空（Aerial 层）：跳跃上升 / 下落 / 走落悬崖时活跃，只负责落地判定——
/// 水平移动由 Locomotion 层照常执行（空中保持地面速度），竖直物理由控制器管线负责。
/// 落地转回 Grounded 不经过 Locomotion 层，因此奔跑中落地不闪断。
/// 已知取舍：贴地连跳会短暂经过 Grounded（Enter/Exit 各一次），将来接动画时若闪落地动画，
/// 用"本帧已施加跳跃冲量"抑制即可。
/// </summary>
public sealed class NpcAirState : NpcStateBase
{
    public NpcAirState(NpcBlackboard board, StateMachine<NpcBlackboard> machine) : base(board, machine) { }

    public override StateLayer Layer => StateLayer.Aerial;

    public override string StateName => "Air";

    public override void HandleTransitions()
    {
        // 落地（UpdateGroundCheck 已把贴地竖直速度钳到 -2，起跳后帧 v > 0，无误判）。
        // 落地帧若按了跳：Grounded 的起跳判定在下帧接手，运动等价（冲量由 TryConsumeJump 施加）
        if (Board.IsGrounded && Board.VerticalVelocity < 0f)
        {
            Machine.ChangeState(Board.Controller.GroundedState);
        }
    }

    public override void Tick()
    {
        // 无动作：空中的水平移动由 Locomotion 层负责（速度与地面一致）
    }
}
