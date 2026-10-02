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
    public ArmorSO Head = new();

    [Tooltip("当前护甲（ArmorSO）；空 = 未穿戴")]
    public ArmorSO Chest = new();

    [Tooltip("当前护甲（ArmorSO）；空 = 未穿戴")]
    public ArmorSO Legs = new();

    [Tooltip("当前护甲（ArmorSO）；空 = 未穿戴")]
    public ArmorSO Feet = new();

    public ArmorSO[] Armors => new ArmorSO[] { Head, Chest, Legs, Feet };

    /// <summary>装备（直接替换；换装 = 一次 Equip 一次 Unequip，事件由 SlotContainer 发）</summary>
    public bool Equip(ArmorSO newArmor)
    {
        for (int i = 0; i < Armors.Length; i++)
        {
            if (Armors[i] == null)
            {
                Armors[i] = newArmor;
                return true;
            }
        }
        return false;
    }

    /// <summary>卸下（返回卸掉的护甲；未穿戴返回 null）</summary>
    public ArmorSO Unequip(int index)
    {
        if (index >= 0 && index < Armors.Length)
        {
            ArmorSO removed = Armors[index];
            Armors[index] = null;
            return removed;
        }
        return null;
    }
}
