using Assets.Scripts.Entity.Data.Skill;
using System;
using UnityEngine;

/// <summary>
/// 技能槽（Data 层，[Serializable] 纯类）：冠冕/潮汐两个固定位 + 各自的冷却剩余。
/// 装配、种类、冷却、信心阈值校验；成功结算启动冷却并归零。
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

    /// <summary>冷却步进（每帧由 Brain 调用；两处共享一个实现）</summary>
    public void TickCooldown(float deltaTime)
    {
        if (CrownCooldownRemaining > 0f)
        {
            CrownCooldownRemaining = Mathf.Max(0f, CrownCooldownRemaining - deltaTime);
        }
        if (TideCooldownRemaining > 0f)
        {
            TideCooldownRemaining = Mathf.Max(0f, TideCooldownRemaining - deltaTime);
        }
    }

    /// <summary>按种类取技能与冷却槽（冠冕/潮汐两位是数据形状钦定的，不做开放数组）。
    /// skill 返回可空：未装配、非法位或技能种类错位均返回 false，调用方必须用返回值门禁——
    /// 这就是 TryGet 模式的用意，注解如实表达</summary>
    public bool TryGet(EnumSkillType kind, out SkillSO? skill, out float cooldownRemaining)
    {
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
            || skill.FaithThreshold <= 0 || skill.Cooldown < 0f
            || float.IsNaN(skill.Cooldown) || float.IsInfinity(skill.Cooldown))
        {
            return false;
        }
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
