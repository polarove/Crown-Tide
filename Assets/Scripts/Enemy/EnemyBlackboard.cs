/// <summary>
/// 敌人行为意图（决策树产出，状态机消费）。
/// </summary>
public enum EnemyBehavior
{
    Idle,       // 待机
    Chase       // 追击
}

/// <summary>
/// 敌人黑板：NPC 黑板 + 敌人特有数据（感知/AI 数据后续按需追加）。
/// 召唤物可照此模式写自己的黑板与行为枚举。
/// </summary>
public sealed class EnemyBlackboard : NpcBlackboard
{
    // ---- 服务引用 ----
    public EnemyController Controller;

    // ---- 决策数据 ----
    public EnemyBehavior DesiredBehavior;   // 决策树低频写入，状态机转换读取
}
