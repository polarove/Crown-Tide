using System;

/// <summary>
/// 决策叶子：条件满足时产出指定意图，否则返回 null。
/// </summary>
public sealed class DecisionLeaf<TContext, TIntent> : Decision<TContext, TIntent>
    where TIntent : struct, Enum
{
    private readonly Func<TContext, bool> condition;
    private readonly TIntent intent;

    public DecisionLeaf(Func<TContext, bool> condition, TIntent intent)
    {
        this.condition = condition;
        this.intent = intent;
    }

    public override TIntent? Decide(TContext context)
    {
        return condition(context) ? intent : null;
    }
}
