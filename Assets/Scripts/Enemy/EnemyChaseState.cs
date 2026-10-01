using UnityEngine;

/// <summary>
/// 敌人追击：朝目标直线移动（演示用，无绕障/寻路）；目标丢失回待机。
/// </summary>
public sealed class EnemyChaseState : State<EnemyBlackboard>
{
    public EnemyChaseState(EnemyBlackboard board, StateMachine<EnemyBlackboard> machine) : base(board, machine) { }

    public override string StateName => "EnemyChase";

    public override void HandleTransitions()
    {
        if (Board.Target == null)
        {
            Machine.ChangeState(Board.Controller.IdleState);
        }
    }

    public override void Tick()
    {
        // 朝目标的方向（投影到水平面），限幅 1；方向同时供基类 ApplyRotation 转身
        Vector3 toTarget = Board.Target.position - Board.Controller.transform.position;
        toTarget.y = 0f;
        Board.MoveDirection = Vector3.ClampMagnitude(toTarget, 1f);

        Board.Controller.ApplyHorizontalMovement(Board.Controller.WalkSpeed);
    }
}
