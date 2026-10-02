using Assets.Scripts.Entity.Data.Armor;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 护甲套装引擎（Logic 层，纯 C#，变更驱动）：「当前穿了哪些套装、几件、该激活哪几档」→
/// 与已挂档位做差分 → 摘掉不再达标的、挂上新增的。
///
/// 档位语义（需求钦定，逐档累加）：门槛 = 「已穿件数 >= PieceCount 即激活」——
/// 护甲共 4 件（头/胸/腿/足），2 件时激活二件档，**穿满 4 件时二件档与四件档同时生效**
/// （两条各自进 ModifierList，各自走乘法链，互不替换）。
///
/// 触发时机（变更驱动，不占每帧管线）：装备/卸下护甲后、以及 Brain.Bootstrap（覆盖 Inspector
/// 预配的初始装备）各调一次 Sync；重复调用幂等（已挂的同档位不重复 Apply——避免搅动
/// Refresh/Stack 语义；被外部驱散掉的档位会在下一次 Sync 补回）。
///
/// 模板/实例分层纪律：本类只读 ArmorSetSO / ModifierEffect 资产，运行时状态全在自身容器，
/// 绝不写 SO。容器复用，Sync 路径零 GC。
/// </summary>
public sealed class ArmorSetBonusList
{
    private readonly Entity Entity;

    // ---- 复用工作集（Sync 每次清空重用）----
    private readonly List<ArmorSetSO> HeldSets = new();                 // 当前穿着的套装（去重）
    private readonly Dictionary<ArmorSetSO, int> PieceCounts = new();   // 每套已穿件数（阈值判定/调试用）
    private readonly List<ModifierEffect> Desired = new();              // 本次应生效的全部档位
    private readonly List<ModifierEffect> Applied = new();              // 本服务已挂的档位（差分基准）

    public ArmorSetBonusList(Entity entity)
    {
        Entity = entity;
    }

    /// <summary>重算并同步套装档位。装备/卸下护甲后与 Bootstrap 各调用一次；幂等、零 GC</summary>
    public void Sync()
    {
        ModifierList modifiers = Entity.Brain.Modifiers;
        if (modifiers == null)
        {
            return;   // Bootstrap 装配中途（ModifierList 未建）——静默早退，调用方无需判空
        }

        CollectHeldSets();
        CollectDesiredBonuses();

        // 摘除：本次不再达标的已挂档位（换套/掉件/换档都会走到这里）
        for (int i = Applied.Count - 1; i >= 0; i--)
        {
            ModifierEffect effect = Applied[i];
            if (Desired.Contains(effect))
            {
                continue;
            }
            modifiers.Remove(effect);
            Applied.RemoveAt(i);
        }

        // 挂载：本次应生效的档位。已在挂的只登记、不重复 Apply（防 Refresh/Stack 语义被搅动）；
        // 但若登记过却已不在列表里（外部驱散清掉/时长到期），这里重新 Apply 补回——
        // 否则记录与列表不一致会让该档位一直不生效，直到下次换装
        for (int i = 0; i < Desired.Count; i++)
        {
            ModifierEffect effect = Desired[i];
            bool registered = Applied.Contains(effect);
            if (registered && modifiers.IsHolding(effect))
            {
                continue;
            }

            modifiers.Apply(effect);
            if (registered)
            {
                Applied.Remove(effect);
            }
            Applied.Add(effect);
        }
    }

    /// <summary>某套装当前生效档位数（HUD/将来 UI 只读；未穿/未达标 = 0）</summary>
    public int ActiveTierCount(ArmorSetSO set)
    {
        if (set == null || !PieceCounts.TryGetValue(set, out int pieceCount) || set.Bonuses == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < set.Bonuses.Length; i++)
        {
            if (set.Bonuses[i].Modifier != null && pieceCount >= set.Bonuses[i].PieceCount)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>清空：摘掉本服务挂过的全部档位（将来换角色/回收/重置用）</summary>
    public void Clear()
    {
        ModifierList modifiers = Entity.Brain.Modifiers;
        if (modifiers == null)
        {
            return;
        }

        for (int i = 0; i < Applied.Count; i++)
        {
            modifiers.Remove(Applied[i]);
        }
        Applied.Clear();
    }

    /// <summary>调试串：当前穿着的套装与件数、生效档位数（仅 HUD；拼串有分配，不上玩法路径）</summary>
    public string Describe()
    {
        if (HeldSets.Count == 0)
        {
            return "无";
        }

        System.Text.StringBuilder sb = new();
        for (int i = 0; i < HeldSets.Count; i++)
        {
            ArmorSetSO set = HeldSets[i];
            if (sb.Length > 0)
            {
                sb.Append('｜');
            }
            sb.Append(set.Name)
              .Append(' ')
              .Append(PieceCounts[set])
              .Append("/4·")
              .Append(ActiveTierCount(set))
              .Append("档");
        }
        return sb.ToString();
    }

    /// <summary>收集当前穿着的套装与件数（按部位序；同套去重）</summary>
    private void CollectHeldSets()
    {
        HeldSets.Clear();
        PieceCounts.Clear();

        ArmorSlot slot = Entity.Slots.Armor;
        int partCount = ArmorSlot.PartCount;
        for (int i = 0; i < partCount; i++)
        {
            ArmorSO? piece = slot.Get(i);
            if (piece == null || piece.Set == null)
            {
                continue;   // 空槽或散件（不参与套装计数）
            }

            if (PieceCounts.TryGetValue(piece.Set, out int count))
            {
                PieceCounts[piece.Set] = count + 1;
            }
            else
            {
                PieceCounts[piece.Set] = 1;
                HeldSets.Add(piece.Set);
            }
        }
    }

    /// <summary>求值：凡「已穿件数 >= 门槛」的档位全部收集（逐档累加，不取最高档），并去重</summary>
    private void CollectDesiredBonuses()
    {
        Desired.Clear();

        for (int s = 0; s < HeldSets.Count; s++)
        {
            ArmorSetSO set = HeldSets[s];
            if (set.Bonuses == null)
            {
                continue;
            }

            int pieceCount = PieceCounts[set];
            for (int b = 0; b < set.Bonuses.Length; b++)
            {
                ModifierEffect effect = set.Bonuses[b].Modifier;
                if (effect == null || pieceCount < set.Bonuses[b].PieceCount)
                {
                    continue;
                }
                if (!Desired.Contains(effect))
                {
                    Desired.Add(effect);   // 同一效果被多档引用时只算一条（OnValidate 会告警提醒）
                }
            }
        }
    }
}
