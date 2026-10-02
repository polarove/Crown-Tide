/// <summary>
/// 装备域枚举集合（Data 层）：武器类型、手位、装备结果、技能种类、持械步态。
/// 全部是纯数据键——代码只做相等/范围判断，语义解释在读方。
/// </summary>


/// <summary>
/// 武器类型
/// 值是武器需要的容量（handCost），用于容量判定
/// </summary>
public enum EnumWeaponType
{
    /// <summary>
    /// 双拳
    /// </summary>
    Fist = 0,

    /// <summary>
    /// 匕首
    /// </summary>
    Dagger = 2,

    /// <summary>
    /// 巨斧
    /// </summary>
    GreatAxe = 4,

    /// <summary>
    /// 圣剑
    /// </summary>
    HolySword = 9,
}

/// <summary>手位：主手 / 副手。双持判定与出招表选择都以它为键（双持用主手表）</summary>
public enum EnumHandSlot
{
    Main = 0,
    Secondary = 1,
}

/// <summary>装备尝试的结果（SlotContainer.TryEquip* 返回；UI 提示与 AI 决策共用）</summary>
public enum EnumEquipResult
{
    Success = 0,             // 装备成功
    InvalidWeapon = 1,       // 传入空引用或非武器数据
    CapacityExceeded = 2,    // 容量不足：main.handCost + secondary.handCost > weaponCapacity
    SameHandOccupied = 3,    // 同手位已有武器（先卸下再装备）
}

/// <summary>技能种类：冠冕 / 潮汐（SkillSlot 的两个固定位）。
/// 枚举值本身是信心方向因子（需求钦定）：实际信心增量 = (int)kind × SkillSO.faithDelta——
/// 冠冕 +1 涨、潮汐 -1 降，方向由位钦定，SO 只配正数幅度，不可能配错方向。
/// 0 不是合法位（CommandBuffer.SkillSlotQueued 拿它当"无请求"哨兵）</summary>
public enum EnumSkillKind
{
    Crown = 1,      // 冠冕技能位（+1：涨信心方向）
    Tide = -1,      // 潮汐技能位（-1：降信心方向）
}

/// <summary>持械步态（出招表的动画选型键）：决定 Idle/Walk/Sprint/Dodge 用哪套动画。
/// 本轮 Presentation 层（EntityVisual）只读这个键，不接动画——接动画时按键映射动画剪辑</summary>
public enum EnumLocomotionStyle
{
    Fist = 0,    // 全套类
    Light = 1,   // 轻武器（匕首类）
    Heavy = 2,   // 重武器（巨斧/圣剑类）
    Dual = 3,    // 双持
}
