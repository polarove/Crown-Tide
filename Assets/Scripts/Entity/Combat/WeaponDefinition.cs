using UnityEngine;

/// <summary>
/// 武器定义（ScriptableObject）：一件武器的战斗参数。
/// 攻速修正在 CharacterEquipment 与角色基础节奏组合（÷ 语义：数值越大出手越快）。
/// M3 起伤害与连招表引用挂进来（装什么会什么）。
/// </summary>
[CreateAssetMenu(fileName = "WeaponDefinition", menuName = "Crown Tide/武器定义")]
public class WeaponDefinition : ScriptableObject
{
    [Tooltip("攻速修正：1 = 不变；1.2 = 快 20%（时长÷1.2）；0.8 = 慢 25%（时长÷0.8）")]
    public float attackSpeed = 1f;
}
