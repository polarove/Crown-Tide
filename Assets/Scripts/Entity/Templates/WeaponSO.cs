using Assets.Scripts.Entity.Data.Weapon;
using Assets.Scripts.Entity.Data.Armor;
using System;
using UnityEngine;

/// <summary>
/// 武器（ScriptableObject，Data 层）：一件武器的类型、手部容量占用与出招表引用。
/// 容量模型：handCost 是占用数（巨斧 5 / 匕首 2 / 圣剑 9），双持判定 =
/// 主副手 cost 之和 ≤ 角色的 weaponCapacity（CharacterConfigSO）——
/// 混合对（巨斧+匕首 = 7 > 默认容量 5 被拒；轻量对可行）由容量模型自然裁决，无需白名单表。
/// 出招表单持/双持各一张（双持用主手武器的 dual 表）——装什么武器会什么招，全在数据。
/// 新武器 = 一份 WeaponSO + 两张 WeaponComboGraph，零代码。
/// </summary>
[CreateAssetMenu(fileName = "Weapon", menuName = "Crown Tide/武器")]
public class WeaponSO : ScriptableObject
{
    [Header("基础")]
    [Tooltip("显示名（调试面板/将来战斗 UI 用）")]
    public string Name = "新武器";

    [Tooltip("武器类型（分类键：将来按类型挂武器模型/音效等表现资源）")]
    public EnumWeaponType Type = EnumWeaponType.Fist;

    [Header("手部容量")]
    [Tooltip("占用容量：与另一把武器的 cost 相加 ≤ 角色手部总容量才可同时持握（巨斧 5 / 匕首 2 / 圣剑 9）")]
    public int Cost = 0;

    [Header("战斗参数")]
    [Tooltip("攻速修正：1 = 不变；1.2 = 快 20%（攻击段时长 ÷ 此值）；0.8 = 慢 25%")]
    public float AttackSpeed = 1f;

    [Tooltip("挥剑期间受伤乘数（Swinging 标签的解释器）：1 = 不减免；0.7 = 挥剑期间受伤 ×0.7；<= 0 视为无效回退 1")]
    public float SwingDamageTakenMultiplier = 1f;

    [Header("出招表")]
    [Tooltip("单持出招表：决定步态键/移速修正/普攻连段（无武器时 WeaponSlot 兜底空手默认，见 WeaponSlot.Sheet）")]
    public WeaponComboGraph? ComboGraphSingle;

    [Tooltip("双持出招表（以本武器为主手时生效）；空 = 回退用单持表（可双持但动画不分单双的轻武器适用）")]
    public WeaponComboGraph? ComboGraphDual;

    [Header("护甲套装联动")]
    [Tooltip("指定护甲套装；武器暂不计入护甲件数（计数规则待确认）")]
    public ArmorSetSO? Set;

    [Tooltip("护甲套装对应档位已生效时额外施加本武器的效果；一般配置 2／4 件档")]
    public ArmorSetBonus[] SetBonuses = Array.Empty<ArmorSetBonus>();
}
