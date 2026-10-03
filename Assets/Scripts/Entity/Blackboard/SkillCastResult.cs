using Assets.Scripts.Entity.Data.Skill;

/// <summary>单次成功释放快照，保留归零前的信心与强化结果；不引用可变技能模板。</summary>
public readonly struct SkillCastResult
{
    public EnumSkillType Kind { get; }
    public int FaithBeforeCast { get; }
    public bool IsBurst { get; }

    public SkillCastResult(EnumSkillType kind, int faithBeforeCast, bool isBurst)
    {
        Kind = kind;
        FaithBeforeCast = faithBeforeCast;
        IsBurst = isBurst;
    }
}
