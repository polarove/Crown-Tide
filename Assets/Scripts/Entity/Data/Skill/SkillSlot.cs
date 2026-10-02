using System;
using UnityEngine;

/// <summary>
/// 技能槽（Data 层，[Serializable] 纯类）：冠冕/潮汐两个固定位 + 各自的冷却剩余。
/// 双闸门（已定案）：冷却与信心方向闸门相互独立——CanCast 两道都过才放行，
/// Consume 在释放成功后同时扣冷却与写信心增量（写回 Data，由 Logic 层调用）。
/// 信心是钟摆不是钱包：增量 = 位方向 × 幅度（(int)kind × faithDelta，冠冕 +1/潮汐 -1），
/// 贴边即锁该方向（SkillResource.CanApply 方向闸门——防单一技能依赖，需求钦定）。
/// 冷却是"剩余秒数"累积器（-= deltaTime，不用 Time.time 差值——网络时间纪律：
/// 换 NetworkTime / 固定 tick 只改 Brain 一处传参）。
/// </summary>
[Serializable]
public sealed class SkillSlot
{
    [Tooltip("冠冕技能（SkillSO；空 = 该位未装配）")]
    public SkillSO crownSkill;

    [Tooltip("潮汐技能（SkillSO；空 = 该位未装配）")]
    public SkillSO tideSkill;

    [Tooltip("冠冕位冷却剩余秒数（运行时，调试可见；释放成功时置为 cooldown）")]
    public float crownCooldownRemaining;

    [Tooltip("潮汐位冷却剩余秒数（运行时，调试可见）")]
    public float tideCooldownRemaining;

    /// <summary>冷却步进（每帧由 Brain 调用；两处共享一个实现）</summary>
    public void TickCooldown(float deltaTime)
    {
        if (crownCooldownRemaining > 0f)
        {
            crownCooldownRemaining = Mathf.Max(0f, crownCooldownRemaining - deltaTime);
        }
        if (tideCooldownRemaining > 0f)
        {
            tideCooldownRemaining = Mathf.Max(0f, tideCooldownRemaining - deltaTime);
        }
    }

    /// <summary>按种类取技能与冷却槽（冠冕/潮汐两位是数据形状钦定的，不做开放数组）</summary>
    public bool TryGet(EnumSkillKind kind, out SkillSO skill, out float cooldownRemaining)
    {
        bool isCrown = kind == EnumSkillKind.Crown;
        skill = isCrown ? crownSkill : tideSkill;
        cooldownRemaining = isCrown ? crownCooldownRemaining : tideCooldownRemaining;
        return skill != null;
    }

    /// <summary>释放闸门校验（不扣减）：技能已装配 && 冷却结束 && 信心方向闸门放行。
    /// 信心增量 = (int)kind × faithDelta（枚举值即方向因子：冠冕 +1 涨、潮汐 -1 降，
    /// 需求钦定）——方向由技能位钦定，SO 只配正数幅度，不可能配错方向</summary>
    public bool CanCast(EnumSkillKind kind, SkillResource faith)
    {
        if (!TryGet(kind, out SkillSO skill, out float cooldownRemaining))
        {
            return false;
        }
        if (cooldownRemaining > 0f)
        {
            return false;
        }
        return faith != null && faith.CanApply((int)kind * skill.faithDelta);
    }

    /// <summary>释放成功结算（Logic 层确认起手后调用）：写冷却 + 写信心增量（写回 Data）。
    /// 增量 = (int)kind × faithDelta（位方向 × 幅度），钳在 ±faithCapacity</summary>
    public void Consume(EnumSkillKind kind, SkillResource faith)
    {
        if (!TryGet(kind, out SkillSO skill, out _))
        {
            return;
        }
        if (kind == EnumSkillKind.Crown)
        {
            crownCooldownRemaining = skill.cooldown;
        }
        else
        {
            tideCooldownRemaining = skill.cooldown;
        }
        faith?.Update((int)kind * skill.faithDelta);
    }
}
