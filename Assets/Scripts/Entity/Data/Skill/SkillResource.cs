using System;

/// <summary>信心内存数据。事件更新钳制到对称区间，成功释放后归零；不依赖 Logic。</summary>
public sealed class SkillResource
{
    private readonly CharacterVitals Owner;
    public int Current { get; private set; }
    public int Max => Math.Max(0, Owner.FaithCapacity);
    public int Min => -Max;

    public SkillResource(CharacterVitals owner, int initial)
    {
        Owner = owner;
        Current = Clamp(initial);
    }

    /// <summary>long 中间值防止极端事件数值溢出后反向跳转。</summary>
    public void Update(int delta) => Current = Clamp((long)Current + delta);
    public void Reset() => Current = 0;
    private int Clamp(long value) => (int)Math.Max(Min, Math.Min(Max, value));
}
