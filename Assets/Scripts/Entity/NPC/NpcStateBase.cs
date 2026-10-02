using UnityEngine;

/// <summary>
/// 角色移动层状态基类（Locomotion 层：Idle/Walk/Sprint，玩家与 NPC 共用）。
/// 跳跃/离地/落地不归这层管（Aerial 层的 Grounded/Air 负责），因此空中也照常执行——
/// 空中保持地面速度，落地无缝续跑。
/// Enter/Exit 是动画/特效的预留接口，保持为空——不得放移动逻辑，否则转换帧会双重移动。
/// </summary>
public abstract class NpcStateBase : State<NpcBlackboard>
{
    protected NpcStateBase(NpcBlackboard board, StateMachine<NpcBlackboard> machine)
        : base(board, machine)
    {
    }

    /// <summary>
    /// Locomotion 层统一移动出口：Action 层有声明 LocksMovement 的状态（攻击/闪避/吟唱）时
    /// 站桩（清方向 + 零速 Move 保留贴地），否则按传入速度 × 移速乘数正常移动
    /// （MoveSpeed 乘法链——急速/减速全姿态生效，读点见 StatType）。
    /// Idle/Walk/Sprint 的 Tick 都走这里——攻击锁移动、效果修饰移速都只需一处生效。
    /// </summary>
    protected void ApplyLocomotion(float speed)
    {
        State<NpcBlackboard> actionState = Machine.GetActive(StateLayer.Action);
        if (actionState != null && actionState.LocksMovement)
        {
            Board.MoveDirection = Vector3.zero;
            Board.Controller.ApplyHorizontalMovement(0f);
            return;
        }
        Board.Controller.ApplyHorizontalMovement(speed * Board.Controller.GetStatMultiplier(StatType.MoveSpeed));
    }
}
