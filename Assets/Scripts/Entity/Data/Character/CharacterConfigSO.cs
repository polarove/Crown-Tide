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

    [Min(0), Tooltip("信心区间上界；初始 0，事件增减，技能成功释放归零")]
    public int FaithCapacity = 67;

    [Min(0), Tooltip("受击时潮汐事件降低信心的幅度（配正数；0 = 关闭）。" +
             "受伤动摇信心、不问伤害来源——被自己附身的怪打、多人被队友误伤同规则；" +
             "贴边 -FaithCapacity 自然封底，潮汐大技能就在这条负向通道上攒")]
    public int FaithLossPerHit = 5;

    [Min(0), Tooltip("命中敌怪的冠冕事件量；0 = 尚未配置，不补造平衡数值")]
    public int FaithGainPerEnemyHit;

    [Min(0), Tooltip("击杀敌怪额外冠冕事件量；0 = 尚未配置")]
    public int FaithGainPerEnemyKill;

    [Tooltip("本体阵营标识；0 = 未指定。两个非零且不同的阵营视为敌对；附身换输入源不改变阵营")]
    public int FactionId;

    [Header("手部容量")]
    [Tooltip("双手总容量：主手 + 副手武器的 handCost 之和不得超过它。5 = 双匕首(2+2)可行、巨斧(5)单持可行但无法双持、圣剑(9)装不上")]
    public int WeaponCapacity = 5;
}
