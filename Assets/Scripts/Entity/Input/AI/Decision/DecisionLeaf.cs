using System;

/// <summary>
/// 决策叶子：条件满足时产出指定意图，否则返回 null。
/// </summary>
public sealed class DecisionLeaf<TContext, TIntent> : Decision<TContext, TIntent>
    where TIntent : struct, Enum
{
    private readonly Func<TContext, bool> Condition;
    private readonly TIntent Intent;

    public DecisionLeaf(Func<TContext, bool> condition, TIntent intent)
    {
        Condition = condition;
        Intent = intent;
    }

    public override TIntent? Decide(TContext context)
    {
        return Condition(context) ? Intent : null;
    }
}
