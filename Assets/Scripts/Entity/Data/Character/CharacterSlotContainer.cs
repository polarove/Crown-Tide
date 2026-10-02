using Assets.Scripts.Entity.Data;
using Assets.Scripts.Entity.Data.Armor;
using System;
using UnityEngine;

/// <summary>
/// 槽位容器（Data 层，Entity 的"装备数据库"）：四槽聚合 + 装备校验 + 变更事件。
/// 回答"Entity 有什么"（武器/技能/护甲/饰品/出招表）；Logic 读它判断，Presentation 读它展示，
/// 本类不做任何玩法逻辑（需求钦定：饰品绑快捷键等逻辑不进 Data 层）。
/// 手部容量由角色配置传入（config 活值直读——Play 模式改容量即时影响装备判定）。
/// EquipmentChanged 事件：装备增删后触发一次，表现层/UI 订阅（将来网络同步也挂这里做增量标记）。
/// </summary>
public sealed class CharacterSlotContainer : MonoBehaviour
{
    [Header("空手兜底")]
    [Tooltip("空手出招表（两手皆空 / 武器缺表时的回退表）；空 = AttackState 用内置默认节奏（双拳）。"
        + "注意：本字段是 ScriptableObject 引用，不能在此 new——运行时用 ScriptableObject.CreateInstance 或拖资产")]
    public WeaponComboGraph? UnarmedComboGraph;

    [Header("槽位（运行时可由 Logic/调试代码写；Inspector 预配初始装备）")]
    [Tooltip("武器槽：主/副手 + 容量判定 + 出招表解析")]
    public WeaponSlot Weapons = new();

    [Tooltip("技能槽：冠冕/潮汐两位 + 冷却")]
    public SkillSlot Skills = new();

    [Tooltip("护甲槽：占位持有")]
    public ArmorSlot Armor = new();

    [Tooltip("饰品槽：占位持有（Data 层不处理逻辑）")]
    public AccessorySlot Accessories = new();

    /// <summary>当前武器占用的手部容量（空手 = 0；两把都可空，故用 ?.）</summary>
    public int WeaponCapacityConsumed => (Weapons.MainHand?.Cost ?? 0) + (Weapons.OffHand?.Cost ?? 0);

    /// <summary>装备变更事件（表现层/UI 订阅；装备/卸下成功后触发，含初始装配不触发）</summary>
    public event Action? EquipmentChanged;

    /// <summary>当前持用的出招表（双持取主手 dual 表，回退链见 WeaponSlot.ResolveComboGraph）。
    /// 空 = 空手且没配兜底表 → 消费方按"缺表"兜底（AttackState 内置默认节奏 / 移速修正 ×1）</summary>
    public WeaponComboGraph? CurrentComboGraph => Weapons.ResolveComboGraph(UnarmedComboGraph);

    /// <summary>装备武器到指定手（容量校验在内）。成功才落库并发事件；失败原样返回结果码</summary>
    public EnumEquipResult TryEquipWeapon(WeaponSO newWeapon, EnumHandSlotType hand, int weaponCapacity)
    {
        EnumEquipResult result = Weapons.CanEquip(newWeapon, hand, weaponCapacity);
        if (result != EnumEquipResult.Success)
        {
            return result;
        }

        if (hand == EnumHandSlotType.MainHand)
        {
            Weapons.MainHand = newWeapon;
        }
        else
        {
            Weapons.OffHand = newWeapon;
        }
        EquipmentChanged?.Invoke();
        return result;
    }

    /// <summary>卸下指定手武器（返回卸掉的武器；空手返回 null）</summary>
    public WeaponSO? UnequipWeapon(EnumHandSlotType hand)
    {
        WeaponSO? removed = hand == EnumHandSlotType.MainHand ? Weapons.MainHand : Weapons.OffHand;
        if (hand == EnumHandSlotType.MainHand)
        {
            Weapons.MainHand = null;
        }
        else
        {
            Weapons.OffHand = null;
        }
        if (removed != null)
        {
            EquipmentChanged?.Invoke();
        }
        return removed;
    }

    /// <summary>装备护甲（按 ArmorSO.Part 自动落槽，同部位直接替换旧件）。
    /// 护甲没有容量闸门（区别于武器），唯一失败是空引用——复用 InvalidWeapon 语义。
    /// 成功后触发 EquipmentChanged；Brain 订阅并同步套装，Data 不调用 Logic。</summary>
    public EnumEquipResult TryEquipArmor(ArmorSO newArmor)
    {
        if (newArmor == null)
        {
            return EnumEquipResult.InvalidWeapon;
        }

        Armor.Equip(newArmor);
        EquipmentChanged?.Invoke();
        return EnumEquipResult.Success;
    }

    /// <summary>卸下护甲（返回卸掉的件；未穿返回 null）。
    /// 卸下成功才重算套装并触发 EquipmentChanged</summary>
    public ArmorSO? UnequipArmor(EnumArmorPart part)
    {
        ArmorSO? removed = Armor.Unequip(part);
        if (removed != null)
        {
            EquipmentChanged?.Invoke();
        }
        return removed;
    }
}
