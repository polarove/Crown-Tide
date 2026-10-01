using System;

/// <summary>
/// 决策分支：条件为真走 trueBranch，否则走 falseBranch（经典二叉决策树节点）。
/// </summary>
public sealed class DecisionBranch<TBoard, TIntent> : Decision<TBoard, TIntent>
    where TBoard : Blackboard
    where TIntent : struct, Enum
{
    private readonly Func<TBoard, bool> condition;
    private readonly Decision<TBoard, TIntent> trueBranch;
    private readonly Decision<TBoard, TIntent> falseBranch;

    public DecisionBranch(Func<TBoard, bool> condition,
        Decision<TBoard, TIntent> trueBranch, Decision<TBoard, TIntent> falseBranch)
    {
        this.condition = condition;
        this.trueBranch = trueBranch;
        this.falseBranch = falseBranch;
    }

    public override TIntent? Decide(TBoard board)
    {
        return condition(board) ? trueBranch.Decide(board) : falseBranch.Decide(board);
    }
}
