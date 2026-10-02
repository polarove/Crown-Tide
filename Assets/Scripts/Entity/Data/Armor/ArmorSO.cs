using Assets.Scripts.Entity.Data.Armor;
using UnityEngine;

/// <summary>
/// 护甲（ScriptableObject，Data 层）：占位持有——能装备、能卸下（ArmorSlot）。
/// Data 层不处理逻辑：防御计算/重量修正等将来由 Logic 层读这张表实现，
/// 本轮只有数据形状。
/// </summary>
[CreateAssetMenu(fileName = "Armor", menuName = "Crown Tide/护甲")]
public class ArmorSO : ScriptableObject
{
    [Tooltip("显示名（调试面板/将来战斗 UI 用）")]
    public string Name = "新护甲";

    [Tooltip("护甲值")]
    public float Value = 1f;

    [Tooltip("护甲部位：决定挂在哪个 ArmorSlot")]
    public EnumArmorPart Part = EnumArmorPart.Head;
}
