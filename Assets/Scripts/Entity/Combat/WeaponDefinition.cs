using UnityEngine;

/// <summary>
/// 武器定义（ScriptableObject）：一件武器的战斗参数。
/// 攻速修正在 CharacterEquipment 与角色基础节奏组合（÷ 语义：数值越大出手越快）。
/// 挥剑标记解释器：武器可重写攻击状态挂的 Swinging 标签的含义（默认纯标记无效果）——
/// swingDamageTakenMultiplier 就是第一个解释（挥剑期间受伤乘数），将来吸血/破甲同样挂读。
/// M3 起伤害与连招表引用挂进来（装什么会什么）。
/// </summary>
[CreateAssetMenu(fileName = "WeaponDefinition", menuName = "Crown Tide/武器定义")]
public class WeaponDefinition : ScriptableObject
{
    [Tooltip("攻速修正：1 = 不变；1.2 = 快 20%（时长÷1.2）；0.8 = 慢 25%（时长÷0.8）")]
    public float attackSpeed = 1f;

    [Tooltip("挥剑期间受伤乘数（Swinging 标签的解释器）：1 = 不减免（默认，标记纯命名）；0.7 = 挥剑期间受到的伤害 ×0.7；<= 0 视为无效回退 1")]
    public float swingDamageTakenMultiplier = 1f;
}
