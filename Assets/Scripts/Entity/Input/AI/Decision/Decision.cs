using System;

/// <summary>
/// 决策树节点基类（AI 输入源的思考件，纯 C# 无 Unity 依赖）：读上下文 → 产出意图（TIntent 枚举）。
/// 与状态机同层的框架件，所有节点在 AITreeInputSource 初始化时构造一次。
/// Decide 返回 null 表示"本次评估无结论"（维持现状）——用于防抖：
/// 条件不满足时不改写意图，实体按当前意图继续执行。
/// （旧版 where TBoard : Blackboard 约束已去——黑板已拆进 CommandBuffer/Motor/Vitals，
/// 上下文直接用 Entity 或任意数据载体。）
/// </summary>
public abstract class Decision<TContext, TIntent>
    where TIntent : struct, Enum
{
    /// <summary>评估本节点；返回意图或 null（无结论）</summary>
    public abstract TIntent? Decide(TContext context);
}
