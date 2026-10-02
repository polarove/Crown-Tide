using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 修饰列表（Logic 层，纯 C#，玩法路径零分配）：Buff/Debuff 条目的生命周期 + 投影。
/// 旧 StatusEffectContainer 平移改名（宿主从黑板换成 Entity），职责链逐条保真：
/// Apply（叠加策略/霸体仲裁）→ Tick（时长/周期/到期移除）→ SyncProjection：
/// - 数值成分 → GetStatMultiplier 乘法链（消费读点见 EnumStatType）；
/// - 控制成分 → 多挂载单表达：活跃失控条目取 controlPriority 最高者（同强度先挂者保持），
///   经 Brain 的注册表（EnumControlKind → CC 层状态）映射推入——压制其余层（冻结而非清除）；
/// - 标签成分 → SyncOwned 投影进 Entity 标签（条目摘 = 位清，不碰状态直写位）。
/// 霸体仲裁在施加时刻定死（controlActive = hasControl 且当时非霸体，Capability 仲裁）：
/// 免疫≠解控——施加后才获得的霸体不清在挂的失控，失去霸体也不补挂（要补 = 重新施加一次）。
/// 模板/实例分层：SO 是共享模板，运行时状态定格在下面的条目上（绝不运行时改 SO）。
/// </summary>
public sealed class ModifierList
{
    /// <summary>一条活跃中的效果实例（SO 模板 + 运行时状态）</summary>
    private sealed class ActiveModifier
    {
        public ModifierEffect Effect;
        public bool ControlActive;         // 施加时刻定死：data.hasControl 且当时未处于霸体
        public float RemainingTime;        // data.duration <= 0 时不倒计时（永久，仅驱散可清）
        public int Stacks = 1;
        public float PeriodicAccumulator;  // 周期累加器（跳伤用）
    }

    private readonly Entity entity;
    private readonly List<ActiveModifier> active = new List<ActiveModifier>();   // 挂载序 = 稳定序（同强度先挂者胜）
    private ulong ownedTagMask;   // 容器域标签位（只扩张不收缩——位一旦归容器管，条目摘除后由 SyncOwned 清零）

    public ModifierList(Entity entity)
    {
        this.entity = entity;
    }

    /// <summary>施加效果（命中入口/调试键调用）。
    /// 返回 = 调用后该效果是否活跃在列表中（已在挂、按叠加策略处理，也算 true）。
    /// 注意霸体只拦控制成分：条目照挂、数值/周期照常——判"这发有没有把失控钉住"请查 HasControlActive。
    /// null data 早退 false（调用侧的调试槽忘拖资产已先行警告，这里静默防炸）</summary>
    public bool Apply(ModifierEffect modifier)
    {
        if (modifier == null)
        {
            return false;
        }

        // 同 SO 引用 = 同一条效果（数据驱动的同一性：同一份资产不管从哪里施加都归到一条）
        for (int i = 0; i < active.Count; i++)
        {
            ActiveModifier existing = active[i];
            if (!ReferenceEquals(existing.Effect, modifier))
            {
                continue;
            }

            switch (modifier.StackPolicy)
            {
                case EnumStackPolicy.Refresh:
                    {
                        existing.RemainingTime = modifier.Duration;
                        break;
                    }
                case EnumStackPolicy.Stack:
                    {
                        if (existing.Stacks < modifier.MaxStacks)
                        {
                            existing.Stacks++;
                        }
                        existing.RemainingTime = modifier.Duration;   // 满层后再施加 = 回落为刷新时长
                        break;
                    }
                case EnumStackPolicy.Ignore:
                    {
                        break;   // 已挂的那条继续跑，本次无操作
                    }
            }
            SyncProjection();
            return true;
        }

        active.Add(new ActiveModifier
        {
            Effect = modifier,
            ControlActive = modifier.HasControl && !entity.Brain.Capability.HasControlImmunity(),   // 施加时刻定死
            RemainingTime = modifier.Duration,
            Stacks = 1,
            PeriodicAccumulator = 0f,
        });
        SyncProjection();
        return true;
    }

    /// <summary>每帧步进：时长倒计时、周期跳伤、到期移除，随后重投影。
    /// 由 Brain 管线在状态机之前调用——控制投影当帧压制；帧末施加的效果次帧压制</summary>
    public void Tick(float deltaTime)
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            ActiveModifier modifier = active[i];
            if (modifier.Effect.Duration > 0f)
            {
                modifier.RemainingTime -= deltaTime;
                if (modifier.RemainingTime <= 0f)
                {
                    active.RemoveAt(i);   // 倒序遍历中移除，保序
                    continue;
                }
            }

            // 周期跳伤：interval <= 0 视为无周期（防配置事故把 while 变死循环）
            if (modifier.Effect.HasPeriodic && modifier.Effect.TickInterval > 0f)
            {
                modifier.PeriodicAccumulator += deltaTime;
                while (modifier.PeriodicAccumulator >= modifier.Effect.TickInterval)
                {
                    modifier.PeriodicAccumulator -= modifier.Effect.TickInterval;
                    entity.Brain.TakeDamage(modifier.Effect.DamagePerTick * modifier.Stacks);
                }
            }
        }
        SyncProjection();
    }

    /// <summary>统计乘数：全部活跃条目连乘；Stack 条目按层数自乘（指数叠加）。无修饰 = 1</summary>
    public float GetStatMultiplier(EnumStatType type)
    {
        float result = 1f;
        for (int i = 0; i < active.Count; i++)
        {
            ActiveModifier modifier = active[i];
            StatModifierEntry[] modifiers = modifier.Effect.StatModifiers;
            for (int m = 0; m < modifiers.Length; m++)
            {
                if (modifiers[m].stat != type)
                {
                    continue;
                }
                for (int s = 0; s < modifier.Stacks; s++)
                {
                    result *= modifiers[m].multiplier;
                }
            }
        }
        return result;
    }

    /// <summary>是否有失控条目活跃（CC 层解除的判据；被霸体拦掉的条目不算）</summary>
    public bool HasControlActive
    {
        get
        {
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].ControlActive)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>驱散：移除类别与 filter 有交集（位与非零）的全部条目，返回移除条数。
    /// 例：Dispel(Debuff) = 净化、Dispel(Poison) = 驱毒、Dispel(All) = 全清</summary>
    public int Dispel(EnumModifierCategory filter)
    {
        int removed = 0;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            if ((active[i].Effect.Category & filter) != 0)
            {
                active.RemoveAt(i);
                removed++;
            }
        }
        SyncProjection();
        return removed;
    }

    /// <summary>指定效果是否活跃（SO 引用比较；命中入口判"已在挂"用）</summary>
    public bool IsHolding(ModifierEffect modifier)
    {
        for (int i = 0; i < active.Count; i++)
        {
            if (ReferenceEquals(active[i].Effect, modifier))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>调试输出（仅 OnGUI 面板；拼串有分配，不上玩法路径）</summary>
    public string Describe()
    {
        if (active.Count == 0)
        {
            return "无";
        }

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < active.Count; i++)
        {
            ActiveModifier modifier = active[i];
            if (sb.Length > 0)
            {
                sb.Append('｜');
            }
            sb.Append(modifier.Effect.Name);
            if (modifier.Stacks > 1)
            {
                sb.Append('×').Append(modifier.Stacks);
            }
            sb.Append(modifier.Effect.Duration > 0f
                ? " " + modifier.RemainingTime.ToString("0.0") + "s"
                : " 永久");
        }
        return sb.ToString();
    }

    /// <summary>把活跃条目投影到标签与 CC 层状态（Apply/Tick/Dispel 末尾统一走这里）</summary>
    private void SyncProjection()
    {
        // 1) 失控呈现：活跃 controlActive 条目中 controlPriority 最高者（列表序稳定 = 同强度先挂者胜）
        ActiveModifier best = null;
        for (int i = 0; i < active.Count; i++)
        {
            ActiveModifier modifier = active[i];
            if (modifier.ControlActive && (best == null || modifier.Effect.ControlPriority > best.Effect.ControlPriority))
            {
                best = modifier;
            }
        }

        // 2) 标签投影：各活跃条目的 grantedTag 或起来；有失控条目再补 Controlled
        ulong desired = 0ul;
        for (int i = 0; i < active.Count; i++)
        {
            desired |= (ulong)active[i].Effect.GrantedTag;
        }
        if (best != null)
        {
            desired |= (ulong)EnumEntityTag.Controlled;
        }
        ownedTagMask |= desired;   // 只扩张：位一旦归容器管，条目摘除后经 SyncOwned 清零
        entity.Tags.SyncOwned(ownedTagMask, desired);

        // 3) CC 层状态呈现。失控解除（best 变 null）不在此清层——EntityStunState 轮询 HasControlActive
        //    自清；取舍：解除当帧门禁（Capability 查 CC 层活跃）会多拦一帧，观感级差异
        if (best == null)
        {
            return;
        }
        EntityStateMachine machine = entity.Brain.Machine;
        EntityState target = entity.Brain.ResolveControlState(best.Effect.ControlKind);
        EntityState current = machine.GetActive(EnumStateLayer.CrowdControl);
        if (current == target)
        {
            return;
        }
        if (current == null)
        {
            // 失控起手：打断主动动作（攻击 Exit 顺带摘 Swinging 标签）
            machine.ClearState(EnumStateLayer.Action);
        }
        else
        {
            // 呈现切换：更高/低强度条目接管（将来 Frozen↔Stun 升级/降级），旧呈现退场
            machine.ClearState(EnumStateLayer.CrowdControl);
        }
        machine.ChangeState(target);
    }
}
