/// <summary>
/// 状态层（Logic 层）：正交维度，同层互斥、异层叠加（旧 StateLayer 平移，Enum 前缀化）。
/// 一个 Entity 可同时处于多层状态（如奔跑时跳跃 = Locomotion 层的 Sprint + Aerial 层的 Air），
/// 层内仍是经典状态机（同层 ChangeState 互相替换）。
/// Tick 顺序按枚举声明序：Locomotion → Aerial → Action → CrowdControl。
/// </summary>
public enum EnumStateLayer
{
    Locomotion = 0,     // 水平移动：Idle/Walk/Sprint（玩家与 AI 共用）
    Aerial = 1,         // 竖直姿态：Grounded/Air（起跳/离地/落地判定）
    Action = 2,         // 主动动作：攻击（连段）/将来的闪避/蓄力
    CrowdControl = 3,   // 失控：眩晕/将来的冰冻石化，压制其余全部层
}

/// <summary>
/// 层间压制规则：失控（CrowdControl）压制除自己外的全部层——
/// 被压制的层状态保留但冻结（不转换、不执行），解除后自动恢复原状态；
/// 物理不受影响（重力/地面检测在 Brain 管线，照常运行）。
/// 压制不是清除：眩晕前在跑，解除后若输入仍在则继续跑。
/// </summary>
public static class EnumStateLayerRules
{
    /// <summary>suppressor 层活跃时，target 层是否被压制。suppressor == target 恒为 false</summary>
    public static bool Suppresses(EnumStateLayer suppressor, EnumStateLayer target)
    {
        return suppressor == EnumStateLayer.CrowdControl && suppressor != target;
    }
}
