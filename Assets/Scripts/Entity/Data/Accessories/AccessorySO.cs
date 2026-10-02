using UnityEngine;

/// <summary>
/// 饰品（ScriptableObject，Data 层）：占位持有——能装备、能卸下（AccessorySlot）。
/// 需求原文：佩戴时获得指定 buff 的饰品，或佩戴后绑定快捷键实现某个功能（如打开背包）——
/// 这些是 Logic/Presentation 层的事，Data 层只存引用，不处理逻辑。
/// </summary>
[CreateAssetMenu(fileName = "Accessory", menuName = "Crown Tide/饰品")]
public class AccessorySO : ScriptableObject
{
    [Tooltip("显示名（调试面板/将来战斗 UI 用）")]
    public string DisplayName = "新饰品";

    [Tooltip("佩戴时获得的 buff（将来 Logic 层在装备事件里 Apply；本轮只存引用）")]
    public ModifierEffect GrantedModifier;

    [Tooltip("佩戴后绑定的快捷键功能标识（将来 Presentation/外围系统读；空 = 纯属性饰品）")]
    public string HotkeyAction;
}
