using UnityEngine;

/// <summary>
/// 角色定义（ScriptableObject）：一类角色的基础战斗节奏与体质参数。
/// 多角色 = 多份资产（重剑士慢、刺客快），数值差异全在数据里，代码零改动。
/// 武器带来的修正（攻速等）在 CharacterEquipment 里与本表基础值组合出最终值。
/// 后续角色差异（基础移速/体重/跳跃力）也挂这张表。
/// </summary>
[CreateAssetMenu(fileName = "CharacterDefinition", menuName = "Crown Tide/角色定义")]
public class CharacterDefinition : ScriptableObject
{
    [Header("攻击节奏（基础值，武器攻速会修正）")]
    [Tooltip("前摇时长（秒）：按下攻击到出手判定之间")]
    public float attackWindup = 0.15f;
    [Tooltip("命中帧时长（秒）：出手判定窗口")]
    public float attackHit = 0.1f;
    [Tooltip("后摇时长（秒）：出手到可以再次行动")]
    public float attackRecovery = 0.3f;
}
