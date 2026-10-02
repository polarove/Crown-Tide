/// <summary>
/// 附身标记效果的角色（需求钦定：附身会话用 buff 表达，不用专门的会话管理器）。
/// 一场附身 = 两个角色的一对 buff：
/// - SoulOut：施法者身上的**纯标记**（如"灵魂出窍"）——表示"我的操作权正在别处"；
/// - Possessed：被附身者身上的**会话载体**（如"被附身"）——它的 Duration 就是本次附身时长，
///   到期即结束（回到出窍者在场时的行为）。
/// 判定（可在多人环境复用的初步闸门）：有 Possessed = 这具身体已被占用；有 SoulOut = 我已在附身中。
/// </summary>
public enum EnumPossessionRole
{
    /// <summary>未被指定（配错/未配）：不参与附身机制</summary>
    None = 0,

    /// <summary>施法者侧纯标记（灵魂出窍）</summary>
    SoulOut = 1,

    /// <summary>被附身者侧会话载体（Duration = 附身时长）</summary>
    Possessed = 2,
}

/// <summary>
/// 附身效果接口（Logic 层）：让 ModifierList 能按语义查询"有没有在附身"，
/// 而不需要认识具体资产类型。实现见 PossessionEffect（ModifierEffect 的子类）。
/// 由 EntityBrain 消费（换绑输入源）；本接口不承载换绑动作——那是 Brain 的邻居能力，
/// 避免把输入源绑定细节灌进 ModifierList。
/// </summary>
public interface IPossessionEffect
{
    /// <summary>本效果在附身里扮演的角色</summary>
    EnumPossessionRole PossessionRole { get; }
}
