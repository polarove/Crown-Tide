using Assets.Scripts.Entity.Data.Armor;
using System;
using UnityEngine;

/// <summary>
/// 护甲槽（Data 层，[Serializable] 纯类）：四部位持有 + 查询。
/// Data 层不处理逻辑（需求钦定）：护甲值减伤由 Logic 层在命中入口读 ArmorSO 实现；
/// 套装效果的求值与挂摘在 Logic 的 ArmorSetBonusList（本类只提供"穿了什么、有几件"的只读答案）。
/// </summary>
[Serializable]
public sealed class ArmorSlot
{
    // 这四个字段用 `ArmorSO?`（而不是 `= null!`）：因为"未穿戴/null"是**合法的运行时状态**——
    // Unequip 会明确写 null，Get/Unequip 本就可能返回 null。注解只是把这件事说出来；
    // 读点写法（if (Head != null)）与运行时行为都不受影响。
    [Tooltip("当前护甲（ArmorSO）；空 = 未穿戴")]
    public ArmorSO? Head;

    [Tooltip("当前护甲（ArmorSO）；空 = 未穿戴")]
    public ArmorSO? Chest;

    [Tooltip("当前护甲（ArmorSO）；空 = 未穿戴")]
    public ArmorSO? Legs;

    [Tooltip("当前护甲（ArmorSO）；空 = 未穿戴")]
    public ArmorSO? Feet;

    /// <summary>已穿件数（0~4；套装计数与 HUD 用）</summary>
    public int EquippedCount
    {
        get
        {
            int count = 0;
            if (Head != null) count++;
            if (Chest != null) count++;
            if (Legs != null) count++;
            if (Feet != null) count++;
            return count;
        }
    }

    // 按 EnumArmorPart 的顺序返回字段引用（不是副本）。返回 ArmorSO? 是诚实的：
    // 空槽 / 枚举越界都会给出 null，调用方必须处理（之前声明非空却返回 null，属注解说谎）
    private ArmorSO? Get(EnumArmorPart part) => part switch
    {
        EnumArmorPart.Head => Head,
        EnumArmorPart.Chest => Chest,
        EnumArmorPart.Legs => Legs,
        EnumArmorPart.Feet => Feet,
        _ => null,
    };

    private void Set(EnumArmorPart part, ArmorSO? value)
    {
        switch (part)
        {
            case EnumArmorPart.Head: Head = value; break;
            case EnumArmorPart.Chest: Chest = value; break;
            case EnumArmorPart.Legs: Legs = value; break;
            case EnumArmorPart.Feet: Feet = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(part), part, null);
        }
    }

    // 部位遍历顺序表（注意 EnumArmorPart 是 1 起算：Head=1…Feet=4，
    // 所以绝不能拿枚举值当数组下标——顺序一律走这张表）
    private static readonly EnumArmorPart[] PartOrder =
    {
        EnumArmorPart.Head,
        EnumArmorPart.Chest,
        EnumArmorPart.Legs,
        EnumArmorPart.Feet,
    };

    /// <summary>部位总数（护甲共 4 件；门槛校验/HUD 分母/遍历都用它，别硬编码 4）</summary>
    public static int PartCount => PartOrder.Length;

    /// <summary>按部位顺序取第 index 件（0 = 头，1 = 胸，2 = 腿，3 = 足；越界 = null）。
    /// 给套装求值与 HUD 按序迭代用——比迭代器接口零分配、调用点也更简单</summary>
    public ArmorSO? Get(int index)
    {
        if (index < 0 || index >= PartOrder.Length)
        {
            return null;
        }
        return Get(PartOrder[index]);
    }

    /// <summary>按部位顺序取第 index 个部位枚举（越界 = null；HUD 显示部位名用）</summary>
    public static EnumArmorPart? PartAt(int index)
    {
        if (index < 0 || index >= PartOrder.Length)
        {
            return null;
        }
        return PartOrder[index];
    }

    /// <summary>某套装当前已穿件数（按 ArmorSO.Set 引用比较；set 为空 = 0）。
    /// 按不同部位计数：同一部位只能穿一件，故"件数"天然等于"不同部位数"</summary>
    public int CountOf(ArmorSetSO set)
    {
        if (set == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < PartOrder.Length; i++)
        {
            ArmorSO? piece = Get(PartOrder[i]);
            if (piece != null && ReferenceEquals(piece.Set, set))
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>装备（直接替换同部位旧件）。返回被替换掉的旧护甲（可能为 null）。
    /// 空引用直接抛（配置事故早暴露：静默返回 null 会变成难查的"怎么没生效"）</summary>
    public ArmorSO? Equip(ArmorSO newArmor)
    {
        if (newArmor == null)
        {
            throw new ArgumentNullException(nameof(newArmor), "ArmorSlot.Equip：护甲资产为空");
        }
        var old = Get(newArmor.Part);
        Set(newArmor.Part, newArmor);
        return old;
    }

    /// <summary>卸下。返回卸掉的护甲；未穿戴返回 null。</summary>
    public ArmorSO? Unequip(EnumArmorPart part)
    {
        var removed = Get(part);
        Set(part, null);
        return removed;
    }
}
