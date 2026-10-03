using Assets.Scripts.Entity.Data.Armor;
using UnityEngine;

/// <summary>
/// 护甲（ScriptableObject，Data 层）：一件护甲的持有形状——部位、护甲值、所属套装。
/// Data 层不处理逻辑：防御计算/重量修正等将来由 Logic 层读这张表实现（ArmorSO.Value 本轮只挂账）；
/// 套装效果由 Logic 层在装备变更时读 Set 计数并挂摘（见 Logic/Armor/ArmorSetBonusList）。
/// </summary>
[CreateAssetMenu(fileName = "Armor", menuName = "Crown Tide/护甲")]
public class ArmorSO : ScriptableObject
{
    [Tooltip("显示名（调试面板/将来战斗 UI 用）")]
    public string Name = "新护甲";

    [Tooltip("护甲值（本轮只挂账：减伤读点在将来的命中入口）")]
    public float Value = 1f;

    [Tooltip("护甲部位：决定挂在哪个 ArmorSlot")]
    public EnumArmorPart Part = EnumArmorPart.Head;

    [Tooltip("所属套装（空 = 散件，不参与套装计数）")]
    public ArmorSetSO? Set;
}
