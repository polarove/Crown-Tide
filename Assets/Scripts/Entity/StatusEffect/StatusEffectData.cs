using System;
using UnityEngine;

/// <summary>同一效果重复施加时的叠加策略</summary>
public enum StackPolicy
{
    Refresh = 0,   // 刷新时长：重置倒计时（默认）
    Stack,         // 叠加层数：层数 +1 并重置倒计时，满层后回落为刷新时长
    Ignore,        // 忽略：已挂的那条继续跑，本次施加无操作
}

/// <summary>效果类别（[Flags] 可并用）：增益/减益 × 作用媒介两个维度。
/// 驱散接口按位与过滤——例：毒 = Debuff|Poison，"驱毒"只清 Poison、"净化"清全部 Debuff</summary>
[Flags]
public enum StatusEffectCategory
{
    None = 0,
    Buff = 1 << 0,
    Debuff = 1 << 1,
    Poison = 1 << 2,
    Magic = 1 << 3,
    Physical = 1 << 4,
    All = Buff | Debuff | Poison | Magic | Physical,
}

/// <summary>单条数值修饰：对应 stat 的最终值 = 基础值 × 全部活跃条目的 multiplier 连乘</summary>
[Serializable]
public struct StatModifierEntry
{
    public StatType stat;

    [Tooltip("乘数：1.5 = 提升 50%；0.5 = 减半。Stack 条目的层数 = 该乘数自乘层数次（1.5×3 层 ≈ 3.4）")]
    public float multiplier;
}

/// <summary>
/// 状态效果数据（ScriptableObject）：Buff 与 Debuff 是同一系统的正负两半，一条效果 =
/// 控制 / 数值 / 周期 三成分，可只开其一（急速=纯数值、眩晕=纯控制、创伤=纯周期）。
/// - 控制成分（hasControl）：失控——容器投影成 CrowdControl 层状态（多挂载单表达）；
/// - 数值成分（statModifiers）：移速/攻速等乘法链修饰（读点见 StatType）；
/// - 周期成分（hasPeriodic）：按 tickInterval 每跳 damagePerTick（创伤/毒的持续伤害）。
/// 附加 grantedTag：条目活跃期间黑板持有该标签（纯命名，读方解释）。
/// </summary>
[CreateAssetMenu(fileName = "StatusEffectData", menuName = "Crown Tide/状态效果")]
public class StatusEffectData : ScriptableObject
{
    [Header("基础")]
    [Tooltip("显示名（调试面板/将来战斗 UI 用）")]
    public string displayName = "新效果";

    [Tooltip("类别（可多选并用）；驱散接口按位与过滤——毒应设 Debuff|Poison")]
    public StatusEffectCategory category = StatusEffectCategory.Debuff;

    [Header("时长与叠加")]
    [Tooltip("持续秒数；<= 0 = 永久（只能被驱散清除）")]
    public float duration = 3f;

    [Tooltip("同一效果重复施加的策略：刷新时长 / 叠加层数 / 忽略")]
    public StackPolicy stackPolicy = StackPolicy.Refresh;

    [Tooltip("叠加策略的最大层数；Stack 满层后再施加 = 回落为刷新时长")]
    public int maxStacks = 3;

    [Header("控制成分（失控）")]
    [Tooltip("是否失控：容器投影进 CrowdControl 层压制其余层（冻结而非清除）。施加瞬间目标处于霸体则此成分被拦下（免疫≠解控），数值/周期照常")]
    public bool hasControl;

    [Tooltip("失控强度：容器取活跃条目中最高者呈现（如冰冻压过眩晕）；同强度先挂者保持")]
    public int controlPriority;

    [Header("数值成分")]
    [Tooltip("数值修饰列表（乘法链聚合，读点见 StatType 头注释）")]
    public StatModifierEntry[] statModifiers = Array.Empty<StatModifierEntry>();

    [Header("周期成分")]
    [Tooltip("是否开启周期跳动（创伤/毒的持续伤害）")]
    public bool hasPeriodic;

    [Tooltip("周期间隔秒数；<= 0 视为无周期（防配置事故卡死）")]
    public float tickInterval = 1f;

    [Tooltip("每跳伤害 × 层数，直接走 TakeDamage（吃 DamageTaken 乘数与挥剑减伤等入口修正）")]
    public float damagePerTick = 5f;

    [Header("标签")]
    [Tooltip("条目活跃期间黑板持有的标签（纯命名，读方解释效果——如临时授予霸体感的 Riding）")]
    public EntityTag grantedTag = EntityTag.None;
}
