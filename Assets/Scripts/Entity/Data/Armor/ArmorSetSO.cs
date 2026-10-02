using Assets.Scripts.Entity.Data.Armor;
using System;
using UnityEngine;

/// <summary>
/// 一档套装效果：达到件数门槛时生效的一条 Modifier。
/// 门槛语义 = 「已穿件数 >= PieceCount 即激活」——**逐档累加**：
/// 4 件套的资产配两条（PieceCount=2 与 =4），穿满 4 件时两条**同时**生效（各自进 ModifierList，
/// 各自走乘法链）；不是「4 件档替换 2 件档」。
/// 注意数值配置：两条若改同一 stat 会相乘，所以高档通常配"增量"（如 4 件档配 1.05 而不是 1.25）。
/// </summary>
[Serializable]
public struct ArmorSetBonus
{
    [Tooltip("件数门槛：2 = 二件套档、4 = 四件套档（护甲共 4 件：头/胸/腿/足）")]
    public int PieceCount;

    [Tooltip("达到门槛时套到 Entity 上的效果；空 = 该档无效果（配错只告警不断链）")]
    public ModifierEffect Modifier;
}

/// <summary>
/// 护甲套装（ScriptableObject，Data 层）：一件套装资产 = 名字 + 档位表（门槛 → 效果）。
/// Data 层只存数据，**不做求值**——「当前穿了几件、该激活哪几档、要挂/摘哪条」全部由
/// Logic 层的 ArmorSetBonusList 在装备变更时重算（见 Logic/Armor/）。
/// 模板纪律：本资产运行时只读，绝不写（换装是运行时状态，不进资产）。
/// 新套装 = 一份本资产 + 几条 ModifierEffect，零代码。
/// </summary>
[CreateAssetMenu(fileName = "ArmorSet", menuName = "Crown Tide/护甲套装")]
public sealed class ArmorSetSO : ScriptableObject
{
    [Tooltip("显示名（调试面板/将来 UI 用）")]
    public string Name = "新套装";

    [Tooltip("档位表：每条 = 一个件数门槛 + 该档的效果。4 件套 = 二件档 + 四件档两条（穿满时两条同时生效）；空 = 该套无任何档位")]
    public ArmorSetBonus[]? Bonuses;

    /// <summary>档位表调试串（HUD/将来 UI/测试断言用；拼串有分配，不上玩法路径）。
    /// 格式：2件→急速、4件→(空)、3件→创伤</summary>
    public string DescribeTiers()
    {
        if (Bonuses == null || Bonuses.Length == 0)
        {
            return "无档位";
        }

        System.Text.StringBuilder sb = new();
        for (int i = 0; i < Bonuses.Length; i++)
        {
            if (sb.Length > 0)
            {
                sb.Append('、');
            }
            sb.Append(Bonuses[i].PieceCount).Append("件→");
            ModifierEffect bonusModifier = Bonuses[i].Modifier;
            sb.Append(bonusModifier != null ? bonusModifier.Name : "(空)");
        }
        return sb.ToString();
    }

    /// <summary>数据自检（编辑器产物只告警，不静默丢弃——配错不断链）：门槛越界 / 同门槛重复 / 空效果 /
    /// 同一效果被两档引用（引用归一会合成一条，语义可能不符预期）
    /// 并顺带把档位表按件数升序排好（Inspector 增删档位后保持可读顺序；只动顺序不动内容）</summary>
    private void OnValidate()
    {
        if (Bonuses == null)
        {
            return;
        }

        Array.Sort(Bonuses, (a, b) => a.PieceCount.CompareTo(b.PieceCount));

        for (int i = 0; i < Bonuses.Length; i++)
        {
            ArmorSetBonus bonus = Bonuses[i];
            if (bonus.PieceCount < 1 || bonus.PieceCount > ArmorSlot.PartCount)
            {
                Debug.LogWarning($"护甲套装「{name}」：第 {i} 档门槛 {bonus.PieceCount} 超出范围（护甲共 {ArmorSlot.PartCount} 件，门槛应为 1~{ArmorSlot.PartCount}），该档永远不会激活");
            }
            if (bonus.Modifier == null)
            {
                Debug.LogWarning($"护甲套装「{name}」：第 {i} 档（{bonus.PieceCount} 件）未配效果，该档只计数不生效");
            }
            for (int j = i + 1; j < Bonuses.Length; j++)
            {
                if (Bonuses[j].PieceCount == bonus.PieceCount)
                {
                    Debug.LogWarning($"护甲套装「{name}」：门槛 {bonus.PieceCount} 件被配了多档，穿到该件数时它们会同时生效——确认是否符合预期");
                }
                if (bonus.Modifier != null && ReferenceEquals(Bonuses[j].Modifier, bonus.Modifier))
                {
                    Debug.LogWarning($"护甲套装「{name}」：效果「{bonus.Modifier.Name}」被 {bonus.PieceCount} 件档与 {Bonuses[j].PieceCount} 件档共用，"
                        + "因条目按 SO 引用归一，两档只会生效一条——确认是否符合预期");
                }
            }
        }
    }
}
