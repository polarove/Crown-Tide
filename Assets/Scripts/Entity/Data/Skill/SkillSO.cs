using UnityEngine;

/// <summary>
/// 技能（ScriptableObject，Data 层）：冠冕/潮汐技能位的一条技能数据。
/// 双闸门（已定案）：冷却（SkillSlot 记冷却剩余，-= deltaTime）+ 信心方向闸门
/// （SkillResource.CanApply：冠冕涨/潮汐降，贴边锁向——防单一技能依赖，需求钦定）。
/// faithDelta 是**正数幅度**：实际增量 = (int)kind × faithDelta（枚举值即方向因子：
/// 冠冕位 +1 涨、潮汐位 -1 降）——方向由技能位钦定，资产不可能配错方向；
/// 把冠冕推到 +67 上界就锁冠冕，必须换潮汐拉回，钟摆如此往复。
/// 技能的执行效果（位移/伤害/施加 Modifier）后置：将来挂效果引用或段位数据，本结构不动。
/// 新技能 = 一份数据 + 决策树/按键指派，零代码。
/// </summary>
[CreateAssetMenu(fileName = "Skill", menuName = "Crown Tide/技能")]
public class SkillSO : ScriptableObject
{
    [Header("基础")]
    [Tooltip("显示名（调试面板/将来战斗 UI 用）")]
    public string displayName = "新技能";

    [Tooltip("技能种类：决定占用 SkillSlot 的固定位（冠冕/潮汐）")]
    public EnumSkillKind kind = EnumSkillKind.Crown;

    [Header("闸门")]
    [Tooltip("信心幅度（配正数）：实际增量 = 技能位方向 × 此值（冠冕位 +、潮汐位 -），钳在 ±faithCapacity——方向由位钦定不会配错")]
    public int faithDelta = 20;

    [Tooltip("冷却秒数（SkillSlot 记冷却剩余，释放成功后开始倒数；0 = 无冷却）")]
    public float cooldown = 5f;
}
