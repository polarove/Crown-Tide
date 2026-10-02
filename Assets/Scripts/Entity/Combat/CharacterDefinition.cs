using UnityEngine;

/// <summary>
/// 角色定义（ScriptableObject）：一类角色的基础战斗节奏与体质参数。
/// 多角色 = 多份资产（重剑士慢、刺客快），数值差异全在数据里，代码零改动。
/// 武器带来的修正（攻速等）在 CharacterEquipment 里与本表基础值组合出最终值；
/// Buff/Debuff 的临时修正在消费读点再乘（效果容器乘法链，见 StatType）。
/// 后续角色差异（基础移速/体重/跳跃力）也挂这张表。
/// </summary>
[CreateAssetMenu(fileName = "CharacterDefinition", menuName = "Crown Tide/角色定义")]
public class CharacterDefinition : ScriptableObject
{
    [Header("体质")]
    [Tooltip("最大生命值（CurrentHealth 的上限与初始值，活属性——Play 模式改了下一帧生效）")]
    public float maxHealth = 100f;
}
