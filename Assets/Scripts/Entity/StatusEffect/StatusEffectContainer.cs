using System.Collections.Generic;
using System.Text;

/// <summary>
/// 状态效果容器（黑板持有，纯 C#，玩法路径零分配）：Buff/Debuff 条目的生命周期 + 投影。
/// 职责链：Apply（叠加策略/霸体仲裁）→ Tick（时长/周期/到期移除）→ SyncProjection：
/// - 数值成分 → GetStatMultiplier 乘法链（消费读点分散在各系统，见 StatType）；
/// - 控制成分 → 多挂载单表达：活跃失控条目取 controlPriority 最高者（同强度先挂者保持），
///   经控制器的 ResolveControlState 映射成 CrowdControl 层状态推入——压制其余层（冻结而非清除）；
/// - 标签成分 → SyncOwned 投影进黑板标签（条目摘 = 位清，不碰状态直写位）。
/// 霸体仲裁在施加时刻定死（controlActive = hasControl 且当时非霸体）：免疫≠解控——
/// 施加后才获得的霸体不清在挂的失控，失去霸体也不补挂（要补 = 重新施加一次）。
/// </summary>
public sealed class StatusEffectContainer
{
    /// <summary>一条活跃中的效果实例（SO 数据 + 运行时状态）</summary>
    private sealed class ActiveEffect
    {
        public StatusEffectData data;
        public bool controlActive;        // 施加时刻定死：data.hasControl 且当时未处于霸体
        public float remainingTime;       // data.duration <= 0 时不倒计时（永久，仅驱散可清）
        public int stacks = 1;
        public float periodicAccumulator; // 周期累加器（跳伤用）
    }

    private readonly List<ActiveEffect> active = new List<ActiveEffect>();   // 挂载序 = 稳定序（同强度先挂者胜）
    private ulong ownedTagMask;   // 容器域标签位（只扩张不收缩——位一旦归容器管，条目摘除后由 SyncOwned 清零）

    /// <summary>施加效果（命中入口/调试键调用）。
    /// 返回 = 调用后该效果是否活跃在容器中（已在挂、按叠加策略处理，也算 true）。
    /// 注意霸体只拦控制成分：条目照挂、数值/周期照常——判"这发有没有把失控钉住"请查 HasControlActive。
    /// null data 早退 false（调用侧的调试槽忘拖资产已先行警告，这里静默防炸）</summary>
    public bool Apply(NpcController controller, StatusEffectData effect)
    {
        if (effect == null)
        {
            return false;
        }

        // 同 SO 引用 = 同一条效果（数据驱动的同一性：同一份资产不管从哪里施加都归到一条）
        for (int i = 0; i < active.Count; i++)
        {
            ActiveEffect existing = active[i];
            if (!ReferenceEquals(existing.data, effect))
            {
                continue;
            }

            switch (effect.stackPolicy)
            {
                case StackPolicy.Refresh:
                    existing.remainingTime = effect.duration;
                    break;
                case StackPolicy.Stack:
                    if (existing.stacks < effect.maxStacks)
                    {
                        existing.stacks++;
                    }
                    existing.remainingTime = effect.duration;   // 满层后再施加 = 回落为刷新时长
                    break;
                case StackPolicy.Ignore:
                    break;   // 已挂的那条继续跑，本次无操作
            }
            SyncProjection(controller);
            return true;
        }

        active.Add(new ActiveEffect
        {
            data = effect,
            controlActive = effect.hasControl && !controller.HasSuperArmor,   // 施加时刻定死
            remainingTime = effect.duration,
            stacks = 1,
            periodicAccumulator = 0f,
        });
        SyncProjection(controller);
        return true;
    }

    /// <summary>每帧步进：时长倒计时、周期跳伤、到期移除，随后重投影。
    /// 由 NpcController 管线在状态机之前调用——控制投影当帧压制；帧末施加的效果次帧压制</summary>
    public void Tick(NpcController controller, float deltaTime)
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            ActiveEffect effect = active[i];
            if (effect.data.duration > 0f)
            {
                effect.remainingTime -= deltaTime;
                if (effect.remainingTime <= 0f)
                {
                    active.RemoveAt(i);   // 倒序遍历中移除，保序
                    continue;
                }
            }

            // 周期跳伤：interval <= 0 视为无周期（防配置事故把 while 变死循环）
            if (effect.data.hasPeriodic && effect.data.tickInterval > 0f)
            {
                effect.periodicAccumulator += deltaTime;
                while (effect.periodicAccumulator >= effect.data.tickInterval)
                {
                    effect.periodicAccumulator -= effect.data.tickInterval;
                    controller.TakeDamage(effect.data.damagePerTick * effect.stacks);
                }
            }
        }
        SyncProjection(controller);
    }

    /// <summary>统计乘数：全部活跃条目连乘；Stack 条目按层数自乘（指数叠加）。无修饰 = 1</summary>
    public float GetStatMultiplier(StatType type)
    {
        float result = 1f;
        for (int i = 0; i < active.Count; i++)
        {
            ActiveEffect effect = active[i];
            StatModifierEntry[] modifiers = effect.data.statModifiers;
            for (int m = 0; m < modifiers.Length; m++)
            {
                if (modifiers[m].stat != type)
                {
                    continue;
                }
                for (int s = 0; s < effect.stacks; s++)
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
                if (active[i].controlActive)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>驱散：移除类别与 filter 有交集（位与非零）的全部条目，返回移除条数。
    /// 例：Dispel(Debuff) = 净化、Dispel(Poison) = 驱毒、Dispel(All) = 全清</summary>
    public int Dispel(NpcController controller, StatusEffectCategory filter)
    {
        int removed = 0;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            if ((active[i].data.category & filter) != 0)
            {
                active.RemoveAt(i);
                removed++;
            }
        }
        SyncProjection(controller);
        return removed;
    }

    /// <summary>指定效果是否活跃（SO 引用比较；命中入口判"已在挂"用）</summary>
    public bool IsHolding(StatusEffectData effect)
    {
        for (int i = 0; i < active.Count; i++)
        {
            if (ReferenceEquals(active[i].data, effect))
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
            ActiveEffect effect = active[i];
            if (sb.Length > 0)
            {
                sb.Append("｜");
            }
            sb.Append(effect.data.displayName);
            if (effect.stacks > 1)
            {
                sb.Append('×').Append(effect.stacks);
            }
            sb.Append(effect.data.duration > 0f
                ? " " + effect.remainingTime.ToString("0.0") + "s"
                : " 永久");
        }
        return sb.ToString();
    }

    /// <summary>把活跃条目投影到标签与 CC 层状态（Apply/Tick/Dispel 末尾统一走这里）</summary>
    private void SyncProjection(NpcController controller)
    {
        // 1) 失控呈现：活跃 controlActive 条目中 controlPriority 最高者（列表序稳定 = 同强度先挂者胜）
        ActiveEffect best = null;
        for (int i = 0; i < active.Count; i++)
        {
            ActiveEffect effect = active[i];
            if (effect.controlActive && (best == null || effect.data.controlPriority > best.data.controlPriority))
            {
                best = effect;
            }
        }

        // 2) 标签投影：各活跃条目的 grantedTag 或起来；有失控条目再补 Controlled
        ulong desired = 0ul;
        for (int i = 0; i < active.Count; i++)
        {
            desired |= (ulong)active[i].data.grantedTag;
        }
        if (best != null)
        {
            desired |= (ulong)EntityTag.Controlled;
        }
        ownedTagMask |= desired;   // 只扩张：位一旦归容器管，条目摘除后经 SyncOwned 清零
        controller.Tags.SyncOwned(ownedTagMask, desired);

        // 3) CC 层状态呈现。失控解除（best 变 null）不在此清层——NpcStunState 轮询 HasControlActive
        //    自清；取舍：解除当帧门禁（TryConsumeJump/TryConsumeSkill 读 CC 层活跃）会多拦一帧，观感级差异
        if (best == null)
        {
            return;
        }
        State<NpcBlackboard> target = controller.ResolveControlState(best.data);
        State<NpcBlackboard> current = controller.StateMachine.GetActive(StateLayer.CrowdControl);
        if (current == target)
        {
            return;
        }
        if (current == null)
        {
            // 失控起手：打断主动动作（攻击 Exit 顺带摘 Swinging——兑现 NpcAttackState 头注释的承诺）
            controller.StateMachine.ClearState(StateLayer.Action);
        }
        else
        {
            // 呈现切换：更高/低强度条目接管（将来 Frozen↔Stun 升级/降级），旧呈现退场
            controller.StateMachine.ClearState(StateLayer.CrowdControl);
        }
        controller.StateMachine.ChangeState(target);
    }
}
