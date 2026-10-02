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
    public string displayName = "新护甲";

    [Tooltip("预留：防御修正等参数（本轮无消费者，只占数据形状）")]
    public float defenseMultiplier = 1f;
}
