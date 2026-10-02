using System;
using UnityEngine;

/// <summary>
/// 武器出招表（ScriptableObject，Data 层）：一张表决定持该武器时的
/// 移动姿态（locomotionStyle 动画选型键）、移动速度修正、普攻连段结构、
/// 蓄力与瞄准射击的段位数据。单持/双持各一张（WeaponSO 分别引用；双持用主手武器）。
/// 出招表即"装什么会什么"：换表换手感，零代码改动。
/// 本轮落地：普攻连段闭环（AttackState 消费 comboEntries）；
/// 蓄力/瞄准射击/闪避只留数据字段（chargeEntry/shootEntry 非空的语义见消费点），状态后置。
/// </summary>
[CreateAssetMenu(fileName = "WeaponComboGraph", menuName = "Crown Tide/武器出招表")]
public class WeaponComboGraph : ScriptableObject
{
    [Header("移动姿态")]
    [Tooltip("动画选型键：EntityVisual（Presentation 层）读它决定 Idle/Walk/Sprint/Dodge 用哪套动画；本轮只读键不接动画")]
    public EnumLocomotionStyle locomotionStyle = EnumLocomotionStyle.Fist;

    [Tooltip("持械移动速度乘数（Walk/Sprint 状态消费）：0.9 = 持重武器慢一成；1 = 不修正")]
    public float moveSpeedMultiplier = 1f;

    [Header("普攻连段")]
    [Tooltip("普攻段位序列（至少 1 段）；段间衔接靠各段 nextEntry + cancelWindow")]
    public ComboEntry[] comboEntries = Array.Empty<ComboEntry>();

    [Header("扩展段位（本轮只留数据，状态后置）")]
    [Tooltip("是否启用蓄力攻击段位（按住攻击键蓄力）")]
    public bool hasCharge;

    [Tooltip("蓄力攻击段位数据")]
    public ComboEntry chargeEntry;

    [Tooltip("是否启用瞄准射击：启用后瞄准电平 + 攻击边沿 → 射击变体（连招规则，消费点在 EntityBrain.TryConsumeAction）；关闭则瞄准中攻击仍是普攻。盲射命中率等参数将来挂同结构")]
    public bool hasShoot;

    [Tooltip("瞄准射击段位数据")]
    public ComboEntry shootEntry;

    /// <summary>有效连段段数（空表保护：AttackState 至少需要 1 段，缺表时由调用侧兜底）</summary>
    public int ComboCount => comboEntries != null ? comboEntries.Length : 0;
}


/// <summary>
/// 单条攻击段位数据（出招表条目）：前摇 → 命中帧 → 后摇 三段计时 + 连段指向。
/// 本轮无判定无动画——AttackState 只按计时推进与续段；命中判定/动画事件后置时挂同一结构。
/// </summary>
[Serializable]
public struct ComboEntry
{
    [Tooltip("段位显示名（调试面板/将来战斗 UI 用）")]
    public string displayName;

    [Tooltip("前摇时长（秒）：按下攻击到出手判定之间")]
    public float windup;

    [Tooltip("命中帧时长（秒）：出手判定窗口（本轮无判定，纯计时占位）")]
    public float hit;

    [Tooltip("后摇时长（秒）：出手到可以再次行动")]
    public float recovery;

    [Tooltip("连段指向：后摇内取消窗口按攻击键续到第几段（comboEntries 下标）；-1 = 无续段")]
    public int nextEntry;

    [Tooltip("取消窗口（秒）：从后摇开始算，此窗口内按攻击键才接受续段；<= 0 = 整段后摇可续")]
    public float cancelWindow;
}
