using UnityEngine;

/// <summary>仅保留原组件上的调试资产序列化字段，避免现有场景和预制体丢引用。
/// 调试采集与效果执行全部由 EntityDebugCommands 负责。</summary>
public sealed partial class PlayerInputSource
{
    [Header("调试效果（拖演示 SO：右键 Create → Crown Tide → 修饰效果）")]
    [Tooltip("F3：对自身施加（眩晕演示——失控/打断/自动解除全链路）")]
    public ModifierEffect? DebugStunModifier;
    [Tooltip("F4：对自身施加（急速演示——移速乘数）")]
    public ModifierEffect? DebugHasteModifier;
    [Tooltip("F5：对自身施加（创伤演示——周期跳伤/死亡占位）")]
    public ModifierEffect? DebugWoundModifier;

}
