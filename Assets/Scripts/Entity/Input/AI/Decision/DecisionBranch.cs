using System;

/// <summary>
/// 决策分支：条件为真走 trueBranch，否则走 falseBranch（经典二叉决策树节点）。
/// </summary>
public sealed class DecisionBranch<TContext, TIntent> : Decision<TContext, TIntent>
    where TIntent : struct, Enum
{
    private readonly Func<TContext, bool> Condition;
    private readonly Decision<TContext, TIntent> TrueBranch;
    private readonly Decision<TContext, TIntent> FalseBranch;

    public DecisionBranch(Func<TContext, bool> condition,
        Decision<TContext, TIntent> trueBranch, Decision<TContext, TIntent> falseBranch)
    {
        Condition = condition;
        TrueBranch = trueBranch;
        FalseBranch = falseBranch;
    }

    public override TIntent? Decide(TContext context)
    {
        return Condition(context) ? TrueBranch.Decide(context) : FalseBranch.Decide(context);
    }
}
