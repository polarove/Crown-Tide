using UnityEngine;

/// <summary>
/// 角色配置（ScriptableObject，Data 层）：一类角色的体质与容量参数——Entity 的"出厂设置"。
/// 多角色 = 多份资产（数值差异全在数据里，代码零改动）；玩家操作不同角色 = 换 Config 引用。
/// 全部字段是活值：Logic/Physics 直读属性，Play 模式改资产即时生效。
/// 武器修正（攻速等）在消费读点与出招表数据组合；Buff/Debuff 的临时乘数在 ModifierList 乘法链。
/// </summary>
[CreateAssetMenu(fileName = "CharacterConfig", menuName = "Crown Tide/角色配置")]
public class CharacterConfigSO : ScriptableObject
{
    [Header("体质")]
    [Tooltip("最大生命值（CharacterVitals.CurrentHp 的上限与初始值，活属性）")]
    public float MaxHealth = 100f;

    [Tooltip("信心区间上界：信心值 ∈ [-此值, +此值]（对称钟摆，初始居中 0）。冠冕技能涨、潮汐技能降，贴边锁向防单一依赖")]
    public int FaithCapacity = 67;

    [Header("手部容量")]
    [Tooltip("双手总容量：主手 + 副手武器的 handCost 之和不得超过它。5 = 双匕首(2+2)可行、巨斧(5)单持可行但无法双持、圣剑(9)装不上")]
    public int WeaponCapacity = 5;
}
