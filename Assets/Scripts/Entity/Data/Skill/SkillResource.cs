using UnityEngine;


/// <summary>
/// 技能资源（信心值，Data 层纯类，CharacterVitals 持有）。
/// 对称区间 [-Capacity, +Capacity]（Capacity 读 config 活值）：
/// - 冠冕技能 Update(+delta) 涨信心、潮汐技能 Update(-delta) 降信心；
/// - 钟摆闸门 CanApply：涨方向未到顶 / 降方向未到底才放行——贴边即锁该方向，
///   逼玩家在两系技能间摆动（防止单一技能依赖，需求钦定）；
/// - 初始值 0（居中，两侧都放得出）。
/// Update(int delta) 是唯一写口（需求钦定签名）——增量钳到区间内；
/// 本类只管数值不变量，不认识技能（冠冕/潮汐的 delta 配在 SkillSO.faithDelta）。
/// </summary>
public sealed class SkillResource
{
    private readonly CharacterVitals Owner;

    /// <summary>当前值（[-Capacity, +Capacity]）</summary>
    public int Current { get; private set; }

    /// <summary>区间上界（读角色配置活值——Play 模式改资产即时生效）</summary>
    public int Max => Owner != null ? Owner.FaithCapacity : 0;

    /// <summary>区间下界（对称 = -上界）</summary>
    public int Min => -Max;

    public SkillResource(CharacterVitals owner, int initial)
    {
        Owner = owner;
        Current = initial;
    }

    /// <summary>方向闸门（技能消费的闸门之一）：本增量在方向上还有空间吗。
    /// 涨（delta > 0）要求 Current < Max；降（delta < 0）要求 Current > Min；
    /// delta = 0 恒可用。贴边锁向——用冠冕推到顶就锁冠冕，必须用潮汐拉回（防依赖钟摆）</summary>
    public bool CanApply(int delta)
    {
        if (delta > 0)
        {
            return Current < Max;
        }
        if (delta < 0)
        {
            return Current > Min;
        }
        return true;
    }

    /// <summary>增减唯一写口：钳到 [-Capacity, +Capacity]。
    /// 调用侧需先 CanApply 校验方向语义（未校验时增量照加，只是贴边无效）</summary>
    public void Update(int delta)
    {
        if (Owner != null)
        {
            Current = Mathf.Clamp(Current + delta, Min, Max);
        }
        else
        {
            Current += delta;
        }
    }
}
