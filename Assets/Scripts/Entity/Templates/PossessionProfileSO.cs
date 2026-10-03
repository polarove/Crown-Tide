using UnityEngine;

/// <summary>技能击杀后的附身配置；共享模板不保存控制权或剩余时间。</summary>
[CreateAssetMenu(fileName = "PossessionProfile", menuName = "Crown Tide/击杀附身配置")]
public sealed class PossessionProfileSO : ScriptableObject
{
    [Tooltip("共用换算系数：绿血=|释放信心|×K；时长毫秒=|释放信心|×K。演示值可调。") ]
    public float K = 1000f;
    public ModifierEffect? PossessedEffect;
    public ModifierEffect? SoulOutEffect;

    public bool IsValid => K > 0f && K / 1000f > 0f && Finite(K) && K <= float.MaxValue / 67f
        && PossessedEffect != null && SoulOutEffect != null
        && PossessedEffect != SoulOutEffect && PureMarker(PossessedEffect) && PureMarker(SoulOutEffect)
        && PossessedEffect.Duration > 0f && Finite(PossessedEffect.Duration)
        && SoulOutEffect.Duration <= 0f && Finite(SoulOutEffect.Duration);

    private static bool PureMarker(ModifierEffect effect) => !effect.HasControl && !effect.HasPeriodic
        && effect.FaithDeltaPerTick == 0 && effect.CrownLifeStealRatio == 0f
        && effect.StatModifiers != null && effect.StatModifiers.Length == 0;

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
