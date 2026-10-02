/// <summary>
/// 行走（Locomotion 层）：有移动指令、加速未生效。空中照常执行（保持走速）。
/// 玩家推杆与 NPC 追击都落在这一个状态——指令不区分来源。
/// </summary>
public sealed class NpcWalkState : NpcStateBase
{
    public NpcWalkState(NpcBlackboard board, StateMachine<NpcBlackboard> machine) : base(board, machine) { }

    public override StateLayer Layer => StateLayer.Locomotion;

    public override string StateName => "Walk";

    public override void HandleTransitions()
    {
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
        ApplyLocomotion(Board.Controller.WalkSpeed);
    }
}
