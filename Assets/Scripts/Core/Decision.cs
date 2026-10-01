using System;

/// <summary>
/// 决策树节点基类：读黑板 → 产出意图（TIntent 枚举）。
/// 与状态机同层的纯 C# 框架件，无 Unity 依赖；所有节点在控制器初始化时构造一次。
/// Decide 返回 null 表示"本次评估无结论"（维持现状）——用于防抖：
/// 条件不满足时不改写意图，状态机按当前意图继续执行。
/// </summary>
public abstract class Decision<TBoard, TIntent>
    where TBoard : Blackboard
    where TIntent : struct, Enum
{
    /// <summary>评估本节点；返回意图或 null（无结论）</summary>
    public abstract TIntent? Decide(TBoard board);
}
