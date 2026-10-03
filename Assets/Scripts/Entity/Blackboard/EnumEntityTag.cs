using System;

/// <summary>
/// 命名状态标签（Logic 层玩法枚举）：纯命名、零效果——"有没有"即全部语义，
/// 谁赋予效果由读方（解释器）决定。例：攻击状态挂摘 Swinging，武器 SO 读它决定
/// 挥剑期间是否减伤/吸血——写的人不定义效果，读的人才定义（读写解耦）。
/// 框架层（CharacterTagSet）不认识本枚举，调用侧统一转 ulong 掩码进出。
/// int 底层 + [Flags]（Unity Inspector 对 ulong 底层枚举不显示，钦定 int）。
/// 32 位分配登记表（新增标签在此加行，防位冲突）：
/// bit0 Swinging   —— 状态直写：攻击状态 Enter/Exit 挂摘；武器解释器消费（减伤/将来的吸血）
/// bit1 Controlled —— 容器投影：有失控（控制类）Modifier 条目活跃
/// bit2 Riding     —— 预留：骑乘（坐骑状态/骑乘增益共用）
/// </summary>
[Flags]
public enum EnumEntityTag : int
{
    None = 0,
    Swinging = 1 << 0,
    Controlled = 1 << 1,
    Riding = 1 << 2,
}

/// <summary>EnumEntityTag 的调试输出（仅 OnGUI 面板用；拼串有分配，不上玩法路径）</summary>
public static class EnumEntityTagDebug
{
    /// <summary>把标签集合格式化成名列表（未登记的位显示 bit 序号）</summary>
    public static string Describe(this CharacterTagSet tags)
    {
        ulong bits = tags.Bits;
        if (bits == 0ul)
        {
            return "无";
        }

        System.Text.StringBuilder sb = new();
        for (int i = 0; i < 32; i++)
        {
            ulong bit = 1ul << i;
            if ((bits & bit) == 0ul)
            {
                continue;
            }
            if (sb.Length > 0)
            {
                sb.Append('、');
            }
            EnumEntityTag value = (EnumEntityTag)bit;
            sb.Append(System.Enum.IsDefined(typeof(EnumEntityTag), value) ? value.ToString() : "bit" + i);
        }
        return sb.ToString();
    }
}
