using System;

/// <summary>
/// Modifier 域枚举集合：叠加策略、类别（驱散过滤）、失控呈现种类。
/// 全部是数据键，语义解释在读方（ModifierList / Brain 注册表）。
/// </summary>

/// <summary>同一条 Modifier 重复施加时的叠加策略</summary>
public enum EnumStackPolicy
{
    Ignore = 0,        // 忽略：已挂的那条继续跑，本次施加无操作
    Refresh,   // 刷新时长：重置倒计时（默认）
    Stack,         // 叠加层数：层数 +1 并重置倒计时，满层后回落为刷新时长
}

/// <summary>Modifier 类别（[Flags] 可并用）：增益/减益 × 作用媒介两个维度。
/// 驱散接口按位与过滤——例：毒 = Debuff|Poison，"驱毒"只清 Poison、"净化"清全部 Debuff</summary>
[Flags]
public enum EnumModifierCategory
{
    None = 0,
    Buff = 1 << 0,
    Debuff = 1 << 1,
    Poison = 1 << 2,
    Magic = 1 << 3,
    Physical = 1 << 4,
    ArmorSet = 1 << 5,
    All = Buff | Debuff | Poison | Magic | Physical,
}

/// <summary>失控呈现种类：Modifier SO 配哪个、EntityBrain 的注册表查哪个 CC 层状态。
/// 新失控（冰冻/石化/击倒）= 枚举加成员 + 状态类 + 注册一行，全"加"零"改"</summary>
public enum EnumControlKind
{
    None = 0,
    Stun = 1,
}
