using Assets.Scripts.Entity.Data.Skill;
using System;
using UnityEngine;

/// <summary>
/// 技能槽（Data 层，[Serializable] 纯类）：冠冕/潮汐/武器三个固定位 + 各自的冷却剩余。
/// 装配、种类、冷却校验；冠冕/潮汐检查信心并归零，武器技能要求非零信心，仅启动独立冷却。
/// 强化资格必须在归零前读取；技能执行由 Brain/Logic 负责。
/// 冷却是"剩余秒数"累积器（-= deltaTime，不用 Time.time 差值——网络时间纪律：
/// 换 NetworkTime / 固定 tick 只改 Brain 一处传参）。
/// </summary>
[Serializable]
public sealed class SkillSlot
{
    // 空 = 该位未装配，是合法状态，故声明为可空
    [Tooltip("冠冕技能（SkillSO；空 = 该位未装配）")]
    public SkillSO? Crown;

    [Tooltip("潮汐技能（SkillSO；空 = 该位未装配）")]
    public SkillSO? Tide;

    [Tooltip("冠冕位冷却剩余秒数（运行时，调试可见；释放成功时置为 cooldown）")]
    public float CrownCooldownRemaining;

    [Tooltip("潮汐位冷却剩余秒数（运行时，调试可见）")]
    public float TideCooldownRemaining;

    [Tooltip("武器主动技能（第三槽；由装备映射逻辑赋值）")]
    public SkillSO? Weapon;

    [Tooltip("武器技能独立冷却剩余秒数；换武器保留")]
    public float WeaponCooldownRemaining;

    /// <summary>Logic 提交第三槽配置；只记录引用，不主动决定武器或改变冷却。</summary>
    public void SetWeaponSkill(SkillSO? skill)
    {
        Weapon = skill;
    }

    /// <summary>冷却步进（每帧由 Brain 调用；三槽共享步进）</summary>
    public void TickCooldown(float deltaTime)
    {
        if (deltaTime < 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
        if (WeaponCooldownRemaining > 0f)
            WeaponCooldownRemaining = Mathf.Max(0f, WeaponCooldownRemaining - deltaTime);
        if (CrownCooldownRemaining > 0f)
        {
            CrownCooldownRemaining = Mathf.Max(0f, CrownCooldownRemaining - deltaTime);
        }
        if (TideCooldownRemaining > 0f)
        {
            TideCooldownRemaining = Mathf.Max(0f, TideCooldownRemaining - deltaTime);
        }
    }

    /// <summary>按种类取技能与冷却槽（冠冕/潮汐/武器三位，不做开放数组）。
    /// skill 返回可空：未装配、非法位或技能种类错位均返回 false，调用方必须用返回值门禁——
    /// 这就是 TryGet 模式的用意，注解如实表达</summary>
    public bool TryGet(EnumSkillType kind, out SkillSO? skill, out float cooldownRemaining)
    {
        if (kind == EnumSkillType.Weapon)
        {
            skill = Weapon;
            cooldownRemaining = WeaponCooldownRemaining;
            return skill != null && skill.Kind == kind;
        }
        if (kind != EnumSkillType.Crown && kind != EnumSkillType.Tide)
        {
            skill = null;
            cooldownRemaining = 0f;
            return false;
        }
        bool isCrown = kind == EnumSkillType.Crown;
        skill = isCrown ? Crown : Tide;
        cooldownRemaining = isCrown ? CrownCooldownRemaining : TideCooldownRemaining;
        return skill != null && skill.Kind == kind;
    }

    /// <summary>纯数据门禁；能力门禁由 Logic 额外检查。满值不锁对应技能。</summary>
    public bool CanCast(EnumSkillType kind, SkillResource? faith)
    {
        if (!TryGet(kind, out SkillSO? skill, out float cooldownRemaining) || skill == null)
        {
            return false;
        }
        if (cooldownRemaining > 0f || float.IsNaN(cooldownRemaining)
            || float.IsInfinity(cooldownRemaining) || cooldownRemaining < 0f
            || (kind != EnumSkillType.Weapon && skill.FaithThreshold <= 0) || skill.Cooldown < 0f
            || float.IsNaN(skill.Cooldown) || float.IsInfinity(skill.Cooldown))
        {
            return false;
        }
        if (kind == EnumSkillType.Weapon) return faith != null && faith.Current != 0
            && skill.IsAttackConfigurationValid(false);
        return faith != null && (long)(int)kind * faith.Current >= skill.FaithThreshold
            && skill.IsAttackConfigurationValid(IsBurstReady(kind, faith));
    }

    /// <summary>纯数据强化资格；不代表已装备、可释放或自动释放。</summary>
    public bool IsBurstReady(EnumSkillType kind, SkillResource? faith)
    {
        return faith != null && faith.Max > 0
            && (kind == EnumSkillType.Crown || kind == EnumSkillType.Tide)
            && (long)(int)kind * faith.Current >= faith.Max;
    }

    /// <summary>再次校验后提交结算；失败不会改变资源或冷却。</summary>
    public bool Consume(EnumSkillType kind, SkillResource? faith)
    {
        if (!CanCast(kind, faith) || !TryGet(kind, out SkillSO? skill, out _) || skill == null)
        {
            return false;
        }
        if (kind == EnumSkillType.Weapon)
        {
            WeaponCooldownRemaining = skill.Cooldown;
            return true; // 武器技能不消费、不归零信心。
        }
        if (kind == EnumSkillType.Crown)
        {
            CrownCooldownRemaining = skill.Cooldown;
        }
        else
        {
            TideCooldownRemaining = skill.Cooldown;
        }
        faith!.Reset();
        return true;
    }
}
