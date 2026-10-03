using Assets.Scripts.Entity.Data.Weapon;
using System;
using UnityEngine;

/// <summary>
/// 武器出招表（ScriptableObject，Data 层）：一张表决定持该武器时的
/// 移动姿态（locomotionStyle 动画选型键）、移动速度修正、普攻连段结构、
/// 蓄力段位数据。单持/双持各一张（WeaponSO 分别引用；双持用主手武器）。
/// 出招表即"装什么会什么"：换表换手感，零代码改动。
/// 本轮落地：普攻连段闭环（AttackState 消费 comboEntries）；
/// 蓄力/闪避尚未实现；蓄力仅预留数据字段。
/// </summary>
[CreateAssetMenu(fileName = "WeaponComboGraph", menuName = "Crown Tide/武器出招表")]
public class WeaponComboGraph : ScriptableObject
{
    [Header("移动姿态")]
    [Tooltip("动画选型键：EntityVisual（Presentation 层）读它决定 Idle/Walk/Sprint/Dodge 用哪套动画；本轮只读键不接动画")]
    public EnumWeaponLocomotionStyle LocomotionStyle = EnumWeaponLocomotionStyle.Fist;

    [Tooltip("持械移动速度乘数（Walk/Sprint 状态消费）：0.9 = 持重武器慢一成；1 = 不修正")]
    public float MoveSpeedMultiplier = 1f;

    [Header("普攻连段")]
    [Tooltip("普攻段位序列（至少 1 段）；段间衔接靠各段 nextEntry + cancelWindow")]
    public ComboEntry[] ComboEntries = Array.Empty<ComboEntry>();

    [Header("扩展段位（本轮只留数据，状态后置）")]
    [Tooltip("是否启用蓄力攻击段位（按住攻击键蓄力）")]
    public bool HasCharge;

    [Tooltip("蓄力攻击段位数据")]
    public ComboEntry ChargeEntry;

    /// <summary>有效连段段数（空表保护：AttackState 至少需要 1 段，缺表时由调用侧兜底）</summary>
    public int ComboCount => ComboEntries != null ? ComboEntries.Length : 0;
}


/// <summary>
/// 单条攻击段位数据（出招表条目）：前摇 → 命中帧 → 后摇 三段计时 + 连段指向。
/// AttackState 在命中窗口执行可配置的近战判定；动画后置。
/// </summary>
[Serializable]
public struct ComboEntry
{
    [Tooltip("段位显示名（调试面板/将来战斗 UI 用）")]
    public string Name;

    [Tooltip("前摇时长（秒）：按下攻击到出手判定之间")]
    public float Windup;

    [Tooltip("命中窗口秒数；>0 时每帧检测，同段每目标只结算一次")]
    public float Hit;

    [Tooltip("后摇时长（秒）：出手到可以再次行动")]
    public float Recovery;

    [Tooltip("连段指向：后摇内取消窗口按攻击键续到第几段（comboEntries 下标）；-1 = 无续段")]
    public int NextEntry;

    [Tooltip("取消窗口（秒）：从后摇开始算，此窗口内按攻击键才接受续段；<= 0 = 整段后摇可续")]
    public float CancelWindow;

    [Header("近战命中（默认关闭，按段配置）")]
    [Min(0f), Tooltip("本段基础伤害；0 = 不造成伤害")]
    public float MeleeDamage;

    [Tooltip("默认球形兼容旧配置；扇形沿角色水平朝向检测")]
    public EnumMeleeHitShape HitShape;

    [Min(0f), Tooltip("球形半径／扇形水平半径；0 = 不启用近战检测")]
    public float HitRadius;

    [Range(0f, 360f), Tooltip("扇形总角度（度），例如140表示左右各70度；球形不使用")]
    public float HitAngle;

    [Min(0f), Tooltip("扇形总高度，围绕命中中心上下各一半；球形不使用")]
    public float HitHeight;

    [Tooltip("命中中心相对实体原点的局部偏移，随角色朝向旋转")]
    public Vector3 HitOffset;

    [Tooltip("可命中的碰撞层；未配置（0）不检测")]
    public LayerMask HitLayers;

    [Tooltip("是否检测 Trigger 型受击碰撞体；默认只检测实体碰撞体")]
    public bool IncludeTriggers;

    /// <summary>纯数据校验；无效扇形不能绕过统一技能门禁。</summary>
    public bool IsHitVolumeValid()
    {
        if (!Finite(HitRadius) || HitRadius <= 0f || HitLayers.value == 0
            || !Finite(HitOffset.x) || !Finite(HitOffset.y) || !Finite(HitOffset.z)) return false;
        return HitShape switch
        {
            EnumMeleeHitShape.Sphere => true,
            EnumMeleeHitShape.Sector => Finite(HitAngle) && HitAngle > 0f && HitAngle <= 360f
                && Finite(HitHeight) && HitHeight > 0f,
            _ => false,
        };
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

public enum EnumMeleeHitShape
{
    Sphere = 0,
    Sector = 1,
}
