using UnityEngine;

/// <summary>
/// 敌人待机：意图为追击时转追击（意图由决策树低频写入 DesiredBehavior）。
/// </summary>
public sealed class EnemyIdleState : State<EnemyBlackboard>
{
    public EnemyIdleState(EnemyBlackboard board, StateMachine<EnemyBlackboard> machine) : base(board, machine) { }

    public override string StateName => "EnemyIdle";

    public override void HandleTransitions()
    {
        if (Board.DesiredBehavior == EnemyBehavior.Chase)
        {
            Machine.ChangeState(Board.Controller.ChaseState);
        }
    }

    public override void Tick()
    {
        // 站桩：清掉移动方向（避免追击残留方向让 ApplyRotation 继续转身），
        // 方向为零仍调用 Move，保留贴地/去穿插（复用基类能力）
        Board.MoveDirection = Vector3.zero;
        Board.Controller.ApplyHorizontalMovement(0f);
    }
}
