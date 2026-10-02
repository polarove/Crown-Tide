/// <summary>
/// 标签集合：命名状态第三机制的载体（纯命名、零效果——"有没有"即全部语义，谁赋予效果由读方解释）。
/// 框架层只做位运算，不认识任何具体枚举——玩法层自带枚举（EntityTag）转 ulong 掩码进出。
/// 写入者分两类（靠 SyncOwned 的位域划分互不踩脚）：
/// - 状态直写：Add/Remove（如攻击状态挂摘 Swinging）；
/// - 容器投影：SyncOwned——效果容器只同步自己域内的位，条目在=位在，条目摘/到期=位清。
/// </summary>
public sealed class TagSet
{
    private ulong bits;

    /// <summary>当前全部标签位（只读；调试面板/快照用）</summary>
    public ulong Bits => bits;

    /// <summary>是否持有 mask 内任意一位（掩码语义：传单个标签即"有没有"，传多个即"有其一"）</summary>
    public bool Has(ulong mask)
    {
        return (bits & mask) != 0ul;
    }

    /// <summary>挂标签（状态直写用；幂等）</summary>
    public void Add(ulong mask)
    {
        bits |= mask;
    }

    /// <summary>摘标签（状态直写用；幂等）</summary>
    public void Remove(ulong mask)
    {
        bits &= ~mask;
    }

    /// <summary>容器域位同步：ownedMask 内的位取 desired，域外一律不碰。
    /// 效果容器每次投影把"当前活跃条目应有的标签位"写进来——条目到期/驱散即自动摘除，
    /// 且不会误清状态直写的位（Swinging 等）。ownedMask 由容器只扩张维护（见 StatusEffectContainer）</summary>
    public void SyncOwned(ulong ownedMask, ulong desired)
    {
        bits = (bits & ~ownedMask) | (desired & ownedMask);
    }
}
