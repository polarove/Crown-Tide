using UnityEngine;

/// <summary>
/// 眩晕（CrowdControl 层）：失控效果的默认呈现状态（容器 ResolveControlState 映射，将来冰冻/石化
/// 由具体角色覆写映射加自己的状态）。压制其余全部层——Locomotion/Aerial/Action 冻结（状态保留但不执行），
/// 物理照常（重力/地面检测在控制器管线）。
/// 时长职责在效果容器（条目到期/驱散/被更高优先级呈现替换），本状态不计时——
/// 只轮询"还有没有失控条目"来决定退场，Enter/Exit 无操作。
/// 多挂载单表达：多条失控同时活跃时只有最高优先级者的映射状态在 CC 层（同强度先挂保持）；
/// 更强者接管时容器先清本层再推新状态，逐个解除自动降级到剩余最高者。
/// 站桩：清移动方向 + 零速 Move 贴地；起跳/攻击被管线门禁拦下。
/// </summary>
public sealed class NpcStunState : NpcStateBase
{
    public NpcStunState(NpcBlackboard board, StateMachine<NpcBlackboard> machine) : base(board, machine) { }

    public override StateLayer Layer => StateLayer.CrowdControl;

    public override string StateName => "Stun";

    public override void HandleTransitions()
    {
        if (!Board.Effects.HasControlActive)
        {
            // 失控条目全部消失（到期/驱散）：清空本层，被压制的层下一帧惰性求值时即恢复。
            // 取舍：容器在条目移除的同帧就清了标签投影，但本层要等这里的轮询——
            // 解除当帧的门禁（跳/攻击判 CC 层活跃）会多拦一帧，观感级差异
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
