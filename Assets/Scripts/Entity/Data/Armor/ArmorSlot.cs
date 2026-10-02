using Assets.Scripts.Entity.Data.Armor;
using System;
using UnityEngine;

/// <summary>
/// 护甲槽（Data 层，[Serializable] 纯类）：占位持有——能装备、能卸下。
/// Data 层不处理逻辑（需求钦定）：防御计算将来由 Logic 层在命中入口读 ArmorSO 实现。
/// </summary>
[Serializable]
public sealed class ArmorSlot
{
    [Tooltip("当前护甲（ArmorSO）；空 = 未穿戴")]
    public ArmorSO Head;

    [Tooltip("当前护甲（ArmorSO）；空 = 未穿戴")]
    public ArmorSO Chest;

    [Tooltip("当前护甲（ArmorSO）；空 = 未穿戴")]
    public ArmorSO Legs;

    [Tooltip("当前护甲（ArmorSO）；空 = 未穿戴")]
    public ArmorSO Feet;

    // 按 EnumArmorPart 的顺序返回字段引用（不是副本）
    private ArmorSO Get(EnumArmorPart part) => part switch
    {
        EnumArmorPart.Head => Head,
        EnumArmorPart.Chest => Chest,
        EnumArmorPart.Legs => Legs,
        EnumArmorPart.Feet => Feet,
        _ => null,
    };

    private void Set(EnumArmorPart part, ArmorSO value)
    {
        switch (part)
        {
            case EnumArmorPart.Head: Head = value; break;
            case EnumArmorPart.Chest: Chest = value; break;
            case EnumArmorPart.Legs: Legs = value; break;
            case EnumArmorPart.Feet: Feet = value; break;
        }
    }

    /// <summary>装备（直接替换）。返回被替换掉的旧护甲（可能为 null）。</summary>
    public ArmorSO Equip(ArmorSO newArmor)
    {
        if (newArmor == null) return null;
        var old = Get(newArmor.Part);
        Set(newArmor.Part, newArmor);
        return old;
    }

    /// <summary>卸下。返回卸掉的护甲；未穿戴返回 null。</summary>
    public ArmorSO Unequip(EnumArmorPart part)
    {
        var removed = Get(part);
        Set(part, null);
        return removed;
    }
}
