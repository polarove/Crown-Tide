using System;
using UnityEngine;

/// <summary>
/// 槽位容器（Data 层，Entity 的"装备数据库"）：四槽聚合 + 装备校验 + 变更事件。
/// 回答"Entity 有什么"（武器/技能/护甲/饰品/出招表）；Logic 读它判断，Presentation 读它展示，
/// 本类不做任何玩法逻辑（需求钦定：饰品绑快捷键等逻辑不进 Data 层）。
/// 手部容量由角色配置传入（config 活值直读——Play 模式改容量即时影响装备判定）。
/// EquipmentChanged 事件：装备增删后触发一次，表现层/UI 订阅（将来网络同步也挂这里做增量标记）。
/// </summary>
public sealed class EntitySlotContainer : MonoBehaviour
{
    [Header("空手兜底")]
    [Tooltip("空手出招表（两手皆空 / 武器缺表时的回退表）；默认是双拳")]
    public WeaponComboGraph unarmedComboGraph = new();

    [Header("槽位（运行时可由 Logic/调试代码写；Inspector 预配初始装备）")]
    [Tooltip("武器槽：主/副手 + 容量判定 + 出招表解析")]
    public WeaponSlot weapon = new();

    [Tooltip("技能槽：冠冕/潮汐两位 + 冷却")]
    public SkillSlot skills = new();

    [Tooltip("护甲槽：占位持有")]
    public ArmorSlot armor = new();

    [Tooltip("饰品槽：占位持有（Data 层不处理逻辑）")]
    public AccessorySlot accessories = new();

    /// <summary>装备变更事件（表现层/UI 订阅；装备/卸下成功后触发，含初始装配不触发）</summary>
    public event Action EquipmentChanged;

    /// <summary>当前持用的出招表（双持取主手 dual 表，回退链见 WeaponSlot.ResolveComboGraph）</summary>
    public WeaponComboGraph CurrentComboGraph => weapon.ResolveComboGraph(unarmedComboGraph);

    /// <summary>装备武器到指定手（容量校验在内）。成功才落库并发事件；失败原样返回结果码</summary>
    public EnumEquipResult TryEquipWeapon(WeaponSO newWeapon, EnumHandSlot hand, int weaponCapacity)
    {
        EnumEquipResult result = weapon.CanEquip(newWeapon, hand, weaponCapacity);
        if (result != EnumEquipResult.Success)
        {
            return result;
        }

        if (hand == EnumHandSlot.Main)
        {
            weapon.main = newWeapon;
        }
        else
        {
            weapon.secondary = newWeapon;
        }
        EquipmentChanged?.Invoke();
        return result;
    }

    /// <summary>卸下指定手武器（返回卸掉的武器；空手返回 null）</summary>
    public WeaponSO UnequipWeapon(EnumHandSlot hand)
    {
        WeaponSO removed = hand == EnumHandSlot.Main ? weapon.main : weapon.secondary;
        if (hand == EnumHandSlot.Main)
        {
            weapon.main = null;
        }
        else
        {
            weapon.secondary = null;
        }
        if (removed != null)
        {
            EquipmentChanged?.Invoke();
        }
        return removed;
    }
}
