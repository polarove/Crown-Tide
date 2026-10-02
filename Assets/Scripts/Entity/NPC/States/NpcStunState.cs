using UnityEngine;

/// <summary>
/// 眩晕（CrowdControl 层）：压制其余全部层——Locomotion/Aerial/Action 冻结（状态保留但不执行），
/// 物理照常（重力/地面检测在控制器管线）。到时自动解除，被冻结的状态恢复执行。
/// 眩晕期间主动清零移动方向并保持贴地 Move；起跳被控制器管线的眩晕门禁拦下。
/// 时长读控制器 StunDuration（活的 Inspector 值，Enter 时取，Play 模式改了下一次眩晕生效）。
/// </summary>
public sealed class NpcStunState : NpcStateBase
{
    private float endTime;

    public NpcStunState(NpcBlackboard board, StateMachine<NpcBlackboard> machine) : base(board, machine) { }

    public override StateLayer Layer => StateLayer.CrowdControl;

    public override string StateName => "Stun";

    public override void Enter()
    {
        endTime = Time.time + Board.Controller.StunDuration;
    }

    public override void HandleTransitions()
    {
        if (Time.time >= endTime)
        {
            // 解除：清空本层，被压制的层下一帧惰性求值时即恢复
            Machine.ClearState(Layer);
        }
    }

    public override void Tick()
    {
        // 站桩：清掉移动方向（压制中的 Locomotion 不会重新写），方向为零仍 Move 保留贴地
        Board.MoveDirection = Vector3.zero;
        Board.Controller.ApplyHorizontalMovement(0f);
    }
}
