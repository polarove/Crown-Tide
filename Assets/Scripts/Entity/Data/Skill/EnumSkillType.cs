using System;
using System.Collections.Generic;
using System.Text;

namespace Assets.Scripts.Entity.Data.Skill
{
    /// <summary>技能种类：冠冕 / 潮汐（SkillSlot 的两个固定位）。
    /// 枚举值本身是信心方向因子（需求钦定）：实际信心增量 = (int)kind × SkillSO.faithDelta ——
    /// 冠冕 +1 涨、潮汐 -1 降，方向由位钦定，SO 只配正数幅度，不可能配错方向。
    /// 0 不是合法位（CommandBuffer.SkillSlotQueued 拿它当"无请求"哨兵）</summary>
    public enum EnumSkillType
    {
        Crown = 1,      // 冠冕技能位（+1：涨信心方向）
        Tide = -1,      // 潮汐技能位（-1：降信心方向）
    }
}
