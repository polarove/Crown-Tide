using UnityEngine;

/// <summary>
/// NPC 黑板基类：实体黑板 + 所有不可控单位（敌人/召唤物/友好 NPC）共享的 AI 数据。
/// NPC 统一架构：感知 → 决策树（低频）写 DesiredBehavior → 状态机（每帧）执行。
/// DesiredBehavior 的具体枚举类型由各 NPC 黑板自己声明（如 EnemyBehavior）。
/// </summary>
public abstract class NpcBlackboard : EntityBlackboard
{
    // ---- AI 数据 ----
    public Transform Target;            // 当前目标（如玩家），null = 无目标。由感知/指派逻辑写入
}
