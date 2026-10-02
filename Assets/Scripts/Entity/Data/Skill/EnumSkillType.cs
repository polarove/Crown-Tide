using System;
using System.Collections.Generic;
using System.Text;

namespace Assets.Scripts.Entity.Data.Skill
{
    /// <summary>技能种类：冠冕 / 潮汐（SkillSlot 的两个固定位）。
    /// 枚举值表示阈值方向：冠冕检查正区间，潮汐检查负区间；释放均归零。
    /// 0 不是合法位（CommandBuffer.SkillSlotQueued 拿它当"无请求"哨兵）</summary>
    public enum EnumSkillType
    {
        Crown = 1,      // 冠冕技能位（+1：涨信心方向）
        Tide = -1,      // 潮汐技能位（-1：降信心方向）
    }
}
