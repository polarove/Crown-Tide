using System;

/// <summary>
/// 决策选择器：按加入顺序评估子节点，第一个返回非 null 的子节点胜出（优先级选择）。
/// 日常最常用的形态：高优先级行为放前面，最后放一条恒真条件兜底。
/// </summary>
public sealed class DecisionSelector<TContext, TIntent> : Decision<TContext, TIntent>
    where TIntent : struct, Enum
{
    private readonly Decision<TContext, TIntent>[] Children;

    public DecisionSelector(params Decision<TContext, TIntent>[] children)
    {
        Children = children;
    }

    public override TIntent? Decide(TContext context)
    {
        for (int i = 0; i < Children.Length; i++)
        {
            TIntent? result = Children[i].Decide(context);
            if (result.HasValue)
            {
                return result;
            }
        }
        return null;
    }
}
