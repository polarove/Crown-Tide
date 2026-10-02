using UnityEngine;

/// <summary>
/// 附身效果（ScriptableObject，Logic 层；ModifierEffect 的语义子类）：一份资产 = 附身里的一个角色。
/// 需求钦定：**附身会话用 buff 表达，不要专门的会话管理器**——
/// - 挂上「被附身」buff = 会话开始（Duration 即时长，到期自动摘 = 会话结束）；
/// - 施法者身上挂「灵魂出窍」纯标记 buff（无任何数值/控制成分，只是"我不在"的牌子）；
/// - 判定与复用：有 Possessed = 身体已被占用（附身闸门）；有 SoulOut = 我已在附身中。
///   判定只看 buff 在场，不依赖任何场景级单例——多人下每个客户端都能用同一套规则初步裁决。
/// 为什么单独一个 SO 类：附身要能被"按语义查出来"（ModifierList 的泛型查询），
/// 而不是给每个 ModifierEffect 加一堆布尔开关；同时资产菜单上独立成项，配置意图明确。
/// 机制执行（换绑输入源、到期换回）在 EntityBrain.Possession：本资产只是数据与角色声明。
/// </summary>
[CreateAssetMenu(fileName = "PossessionEffect", menuName = "Crown Tide/附身效果")]
public sealed class PossessionEffect : ModifierEffect, IPossessionEffect
{
    [Header("附身")]
    [Tooltip("本效果在附身里的角色：Possessed = 会话载体（Duration 即附身时长）；SoulOut = 施法者侧纯标记")]
    public EnumPossessionRole PossessionRole = EnumPossessionRole.Possessed;

    EnumPossessionRole IPossessionEffect.PossessionRole => PossessionRole;
}
