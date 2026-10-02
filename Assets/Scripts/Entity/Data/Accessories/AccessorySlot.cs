using System;
using UnityEngine;

/// <summary>
/// 饰品槽（Data 层，[Serializable] 纯类）：固定位数组占位持有——能装备、能卸下。
/// 需求钦定"Data 层不处理逻辑"：佩戴给 buff（Logic 层在装备事件里 Apply grantedModifier）、
/// 绑快捷键开背包（Presentation/外围系统读 hotkeyAction）都不在这里做。
/// </summary>
[Serializable]
public sealed class AccessorySlot
{
    [Tooltip("饰品位（AccessorySO；空位 = 未佩戴）")]
    public AccessorySO[] Accessories = new AccessorySO[6];

    /// <summary>找第一个空位装入；满员返回 false（调用侧提示）</summary>
    public bool Equip(AccessorySO accessory)
    {
        for (int i = 0; i < Accessories.Length; i++)
        {
            if (Accessories[i] == null)
            {
                Accessories[i] = accessory;
                return true;
            }
        }
        return false;
    }

    /// <summary>卸下指定位（越界/空位返回 null）</summary>
    public AccessorySO Unequip(int index)
    {
        if (index < 0 || index >= Accessories.Length)
        {
            return null;
        }
        AccessorySO removed = Accessories[index];
        Accessories[index] = null;
        return removed;
    }
}
