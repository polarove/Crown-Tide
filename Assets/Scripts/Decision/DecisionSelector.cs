using System;

/// <summary>
/// 决策选择器：按加入顺序评估子节点，第一个返回非 null 的子节点胜出（优先级选择）。
/// 日常最常用的形态：高优先级行为放前面，最后放一条恒真条件兜底。
/// </summary>
public sealed class DecisionSelector<TBoard, TIntent> : Decision<TBoard, TIntent>
    where TBoard : Blackboard
    where TIntent : struct, Enum
{
    private readonly Decision<TBoard, TIntent>[] children;

    public DecisionSelector(params Decision<TBoard, TIntent>[] children)
    {
        this.children = children;
    }

    public override TIntent? Decide(TBoard board)
    {
        foreach (Decision<TBoard, TIntent> child in children)
        {
            TIntent? result = child.Decide(board);
            if (result.HasValue)
            {
                return result;
            }
        }
        return null;
    }
}
