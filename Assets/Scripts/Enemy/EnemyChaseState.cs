using UnityEngine;

/// <summary>
/// 敌人追击：朝目标直线移动（演示用，无绕障/寻路）；意图为待机时回待机。
/// </summary>
public sealed class EnemyChaseState : State<EnemyBlackboard>
{
    public EnemyChaseState(EnemyBlackboard board, StateMachine<EnemyBlackboard> machine) : base(board, machine) { }

    public override string StateName => "EnemyChase";

    public override void HandleTransitions()
    {
        if (Board.DesiredBehavior == EnemyBehavior.Idle)
        {
            Machine.ChangeState(Board.Controller.IdleState);
        }
    }

    public override void Tick()
    {
        // 判空保护：感知每帧同步，但意图按 decisionInterval 刷新——
        // Target 被清空后最长一个决策间隔内 DesiredBehavior 仍可能是 Chase
        if (Board.Target == null)
        {
            return;
        }

        // 朝目标的方向（投影到水平面），限幅 1；方向同时供基类 ApplyRotation 转身
        Vector3 toTarget = Board.Target.position - Board.Controller.transform.position;
        toTarget.y = 0f;
        Board.MoveDirection = Vector3.ClampMagnitude(toTarget, 1f);

        Board.Controller.ApplyHorizontalMovement(Board.Controller.WalkSpeed);
    }
}
