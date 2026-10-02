/// <summary>
/// 状态层：正交维度，同层互斥、异层叠加。
/// 一个实体可同时处于多层状态（如奔跑时跳跃 = Locomotion 层的 Sprint + Aerial 层的 Air），
/// 层内仍是经典状态机（同层 ChangeState 互相替换）。
/// Tick 顺序按枚举声明序：Locomotion → Aerial → Action → CrowdControl。
/// </summary>
public enum StateLayer
{
    Locomotion = 0,     // 水平移动：玩家 Idle/Walk/Sprint、敌人 Idle/Chase 等
    Aerial = 1,         // 竖直姿态：Grounded/Air（起跳/离地/落地判定）
    Action = 2,         // 主动动作：攻击/闪避/交互（战斗系统 M1 起使用，先预留）
    CrowdControl = 3,   // 失控：眩晕/击倒/冰冻等，压制其余全部层
}

/// <summary>
/// 层间压制规则：失控（CrowdControl）压制除自己外的全部层——
/// 被压制的层状态保留但冻结（不转换、不执行），解除后自动恢复原状态；
/// 物理不受影响（重力/地面检测在控制器管线，照常运行）。
/// 压制不是清除：眩晕前在跑，解除后若输入仍在则继续跑。
/// </summary>
public static class StateLayerRules
{
    /// <summary>suppressor 层活跃时，target 层是否被压制。suppressor == target 恒为 false。</summary>
    public static bool Suppresses(StateLayer suppressor, StateLayer target)
    {
        return suppressor == StateLayer.CrowdControl && suppressor != target;
    }
}
