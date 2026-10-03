using System;
using System.Collections.Generic;
using System.Text;

namespace Assets.Scripts.Entity.Data.Skill
{
    /// <summary>技能种类：冠冕 / 潮汐 / 武器（SkillSlot 的三个固定位）。
    /// 冠冕与潮汐枚举值表示阈值方向：冠冕检查正区间，潮汐检查负区间；释放均归零。
    /// 0 不是合法位（CommandBuffer.SkillSlotQueued 拿它当"无请求"哨兵）</summary>
    public enum EnumSkillType
    {
        Crown = 1,      // 冠冕技能位（+1：涨信心方向）
        Weapon = 2,     // 第三槽：当前武器主动技能，独立冷却
        Tide = -1,      // 潮汐技能位（-1：降信心方向）
    }
}
