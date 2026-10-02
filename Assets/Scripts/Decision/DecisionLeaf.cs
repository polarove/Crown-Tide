using System;

/// <summary>
/// 决策叶子：条件满足时产出指定意图，否则返回 null。
/// </summary>
public sealed class DecisionLeaf<TBoard, TIntent> : Decision<TBoard, TIntent>
    where TBoard : Blackboard
    where TIntent : struct, Enum
{
    private readonly Func<TBoard, bool> condition;
    private readonly TIntent intent;

    public DecisionLeaf(Func<TBoard, bool> condition, TIntent intent)
    {
        this.condition = condition;
        this.intent = intent;
    }

    public override TIntent? Decide(TBoard board)
    {
        return condition(board) ? intent : null;
    }
}
