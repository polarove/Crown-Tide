using UnityEngine;

/// <summary>
/// 敌人黑板：实体黑板 + 敌人特有数据（最小示例，感知/AI 数据后续按需追加）。
/// 召唤物可照此模式写自己的黑板。
/// </summary>
public sealed class EnemyBlackboard : EntityBlackboard
{
    // ---- 服务引用 ----
    public EnemyController Controller;

    // ---- AI 数据 ----
    public Transform Target;            // 当前目标（如玩家），null = 无目标。
                                        // 感知系统 TODO：当前由 EnemyController.target（Inspector 指派）同步
}
