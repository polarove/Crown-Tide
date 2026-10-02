using Assets.Scripts.Entity.Data;
using System;
using UnityEngine;

/// <summary>
/// 武器槽（Data 层，[Serializable] 纯类，内嵌 SlotContainer 一个字段区）：
/// 主/副手各一个 WeaponSO 引用 + 容量判定 + 出招表解析。
/// 双持规则（容量模型，需求钦定）：main.handCost + secondary.handCost ≤ 角色手部总容量
/// （CharacterConfigSO.weaponCapacity，Entity 装备时传入）——
/// 容量 5 时：双匕首 4 可行、巨斧 5 单持可行但无法双持、圣剑 9 装不上。
/// 出招表解析（需求钦定）：双持用主手武器的 dual 表（null 回退其单持表）；
/// 单持用该武器的单持表；空手用容器传入的空手兜底表。
/// 纯数据 + 纯判定，不做装备副作用（EquipmentChanged 事件由 SlotContainer 发）。
/// </summary>
[Serializable]
public sealed class WeaponSlot
{
    // null = 空手/单持，是合法状态，故声明为可空（ArmorSlot 同例）
    [Tooltip("主手武器（双持时出招表以主手为准）；空 = 空手")]
    public WeaponSO? MainHand;

    [Tooltip("副手武器；空 = 单持")]
    public WeaponSO? OffHand;

    /// <summary>是否双持（副手有武器即双持；能装进来说明容量判定已过）</summary>
    public bool IsDualWield => MainHand != null && OffHand != null;

    /// <summary>当前持用的出招表。unarmedComboGraph 由 SlotContainer 传入（空手兜底表，可空）。
    /// 武器没配表（配置缺失）同样回退空手表——攻击状态永远有节奏可用，不因缺数据断链</summary>
    public WeaponComboGraph? ResolveComboGraph(WeaponComboGraph? unarmedComboGraph)
    {
        if (MainHand == null && OffHand == null)
        {
            return unarmedComboGraph;
        }
        // 非空性用同一处判断收口（不靠 IsDualWield 属性——编译器不做属性内的流分析）
        if (MainHand != null && OffHand != null && MainHand.ComboGraphDual != null)
        {
            return MainHand.ComboGraphDual;
        }
        return MainHand != null && MainHand.ComboGraphSingle != null ? MainHand.ComboGraphSingle : unarmedComboGraph;
    }

    /// <summary>装备校验（不落库）：weapon 装到 hand 是否合法。
    /// weaponCapacity 由角色配置传入（Entity → SlotContainer → 这里）。
    /// 同手位已有武器 = 先卸再装（SameHandOccupied）；容量按"另一把的 cost + 这把的 cost"算</summary>
    public EnumEquipResult CanEquip(WeaponSO weapon, EnumHandSlotType hand, int weaponCapacity)
    {
        if (weapon == null)
        {
            return EnumEquipResult.InvalidWeapon;
        }

        WeaponSO? occupied = hand == EnumHandSlotType.MainHand ? MainHand : OffHand;
        if (occupied != null)
        {
            return EnumEquipResult.SameHandOccupied;
        }

        WeaponSO? other = hand == EnumHandSlotType.MainHand ? OffHand : MainHand;
        int totalCost = weapon.Cost + (other != null ? other.Cost : 0);
        if (totalCost > weaponCapacity)
        {
            return EnumEquipResult.CapacityExceeded;
        }
        return EnumEquipResult.Success;
    }
}
