using System;

/// <summary>
/// 决策分支：条件为真走 trueBranch，否则走 falseBranch（经典二叉决策树节点）。
/// </summary>
public sealed class DecisionBranch<TContext, TIntent> : Decision<TContext, TIntent>
    where TIntent : struct, Enum
{
    private readonly Func<TContext, bool> condition;
    private readonly Decision<TContext, TIntent> trueBranch;
    private readonly Decision<TContext, TIntent> falseBranch;

    public DecisionBranch(Func<TContext, bool> condition,
        Decision<TContext, TIntent> trueBranch, Decision<TContext, TIntent> falseBranch)
    {
        this.condition = condition;
        this.trueBranch = trueBranch;
        this.falseBranch = falseBranch;
    }

    public override TIntent? Decide(TContext context)
    {
        return condition(context) ? trueBranch.Decide(context) : falseBranch.Decide(context);
    }
}
