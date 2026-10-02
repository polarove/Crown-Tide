using System;
using UnityEngine;

/// <summary>单条数值修饰：对应 stat 的最终值 = 基础值 × 全部活跃条目的 multiplier 连乘</summary>
[Serializable]
public struct StatModifierEntry
{
    public EnumStatType stat;

    [Tooltip("乘数：1.5 = 提升 50%；0.5 = 减半。Stack 条目的层数 = 该乘数自乘层数次（1.5×3 层 ≈ 3.4）")]
    public float multiplier;
}

/// <summary>
/// 修饰效果数据（ScriptableObject，Logic 层）：Buff 与 Debuff 是同一系统的正负两半，
/// 一条 Modifier = 控制 / 数值 / 周期 三成分，可只开其一（急速=纯数值、眩晕=纯控制、创伤=纯周期）。
/// - 控制成分（hasControl）：失控——ModifierList 投影成 CrowdControl 层状态（多挂载单表达，
///   呈现种类 = controlKind，Brain 注册表映射）；
/// - 数值成分（statModifiers）：移速/攻速等乘法链修饰（读点见 EnumStatType 头注释）；
/// - 周期成分（hasPeriodic）：按 tickInterval 每跳 damagePerTick（创伤/毒的持续伤害）。
/// 附加 grantedTag：条目活跃期间 Entity 持有该标签（纯命名，读方解释）。
/// 模板/实例分层：SO 是共享模板（绝不运行时改——全场生效 + Play 修改持久化写脏），
/// 运行时状态（剩余时长/层数/跳伤累加器）定格在 ModifierList 的条目上。
/// 新 buff/debuff = 一份数据，零代码。
/// </summary>
[CreateAssetMenu(fileName = "ModifierData", menuName = "Crown Tide/修饰效果")]
public class ModifierData : ScriptableObject
{
    [Header("基础")]
    [Tooltip("显示名（调试面板/将来战斗 UI 用）")]
    public string displayName = "新效果";

    [Tooltip("类别（可多选并用）；驱散接口按位与过滤——毒应设 Debuff|Poison")]
    public EnumModifierCategory category = EnumModifierCategory.Debuff;

    [Header("时长与叠加")]
    [Tooltip("持续秒数；<= 0 = 永久（只能被驱散清除）")]
    public float duration = 3f;

    [Tooltip("同一条重复施加的策略：刷新时长 / 叠加层数 / 忽略")]
    public EnumStackPolicy stackPolicy = EnumStackPolicy.Refresh;

    [Tooltip("叠加策略的最大层数；Stack 满层后再施加 = 回落为刷新时长")]
    public int maxStacks = 3;

    [Header("控制成分（失控）")]
    [Tooltip("是否失控：容器投影进 CrowdControl 层压制其余层（冻结而非清除）。施加瞬间目标处于霸体则此成分被拦下（免疫≠解控），数值/周期照常")]
    public bool hasControl;

    [Tooltip("失控强度：容器取活跃条目中最高者呈现（如冰冻压过眩晕）；同强度先挂者保持")]
    public int controlPriority;

    [Tooltip("失控呈现种类：Brain 的注册表映射到哪个 CC 层状态（未注册回落 Stun）")]
    public EnumControlKind controlKind = EnumControlKind.Stun;

    [Header("数值成分")]
    [Tooltip("数值修饰列表（乘法链聚合，读点见 EnumStatType 头注释）")]
    public StatModifierEntry[] statModifiers = Array.Empty<StatModifierEntry>();

    [Header("周期成分")]
    [Tooltip("是否开启周期跳动（创伤/毒的持续伤害）")]
    public bool hasPeriodic;

    [Tooltip("周期间隔秒数；<= 0 视为无周期（防配置事故卡死）")]
    public float tickInterval = 1f;

    [Tooltip("每跳伤害 × 层数，直接走 EntityBrain.TakeDamage（吃 DamageTaken 乘数与挥剑减伤等入口修正）")]
    public float damagePerTick = 5f;

    [Header("标签")]
    [Tooltip("条目活跃期间 Entity 持有的标签（纯命名，读方解释效果——如临时授予的 Riding）")]
    public EnumEntityTag grantedTag = EnumEntityTag.None;
}
