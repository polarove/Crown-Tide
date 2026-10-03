using Assets.Scripts.Entity.Data.Skill;
using System;
using UnityEngine;

/// <summary>技能数据模板。只声明条件与效果，不执行行为，不存实体运行时状态。</summary>
[CreateAssetMenu(fileName = "Skill", menuName = "Crown Tide/技能")]
public class SkillSO : ScriptableObject
{
    public string Name = "新技能";
    public EnumSkillType Kind = EnumSkillType.Crown;

    [Header("释放条件")]
    [Min(1), Tooltip("信心阈值幅度；冠冕 ≥ 此值，潮汐 ≤ -此值")]
    public int FaithThreshold = 33;

    [Tooltip("显式允许被控制期间释放，供解控技能配置")]
    public bool AllowWhileControlled;

    [Min(0f)]
    public float Cooldown = 5f;

    [Header("攻击执行（复用统一出招段，默认关闭）")]
    public bool HasAttack;
    public ComboEntry Attack;

    [Tooltip("满信心使用独立攻击段；关闭时沿用普通段")]
    public bool UseBurstAttack;
    public ComboEntry BurstAttack;

    [Header("统一 Effect（施加到释放者）")]
    [Tooltip("普通释放的持续效果；空列表合法，不补造角色专属技能")]
    public ModifierEffect[] Effects = Array.Empty<ModifierEffect>();

    [Tooltip("满信心时替代普通效果；空列表回退普通效果。需按键释放，不自动释放")]
    public ModifierEffect[] BurstEffects = Array.Empty<ModifierEffect>();

    [Tooltip("潮汐成功释放后额外授予的效果；可配置冠冕吸血，数值与时长待设计确认")]
    public ModifierEffect[] AfterTideEffects = Array.Empty<ModifierEffect>();

    [Tooltip("成功释放时先驱散这些类别；None = 不驱散，解控技能须同时显式允许控制中释放")]
    public EnumModifierCategory DispelOnCast = EnumModifierCategory.None;

    /// <summary>纯配置校验；统一数据门禁和 UI 资格查询共用，不执行技能。</summary>
    public bool IsAttackConfigurationValid(bool burst)
    {
        if (!HasAttack) return true;
        ComboEntry attack = burst && UseBurstAttack ? BurstAttack : Attack;
        return Finite(attack.Windup) && attack.Windup >= 0f
            && Finite(attack.Windup + attack.Hit + attack.Recovery)
            && Finite(attack.Hit) && attack.Hit > 0f
            && Finite(attack.Recovery) && attack.Recovery >= 0f
            && Finite(attack.MeleeDamage) && attack.MeleeDamage > 0f
            && attack.IsHitVolumeValid();
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
