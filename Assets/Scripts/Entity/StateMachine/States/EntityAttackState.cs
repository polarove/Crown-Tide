using UnityEngine;
using Assets.Scripts.Entity.Data.Skill;

/// <summary>
/// 攻击（Action 层，通用连段执行器）：读出招表（WeaponComboGraph.comboEntries）推进
/// 前摇 → 命中帧 → 后摇 计时，后摇取消窗口内收到攻击请求则续到 nextEntry 段。
/// 旧 NpcAttackState 的三段计时骨架泛化——节奏差异（武器/急速）全在数据：
/// 段时长 = 表值 ÷（武器攻速 × AttackSpeed 乘法链，钳下限 0.05 防除零）。
/// 两种起手（Brain.TryConsumeAction 调用）：
/// - BeginCombo()：普攻，从连段第 0 段开始，按 nextEntry 链续段；
/// - BeginSingle(entry)：单发段（技能攻击/蓄力），打完即收招，不续段。
/// 出招表缺失时兜底内置默认节奏（0.15/0.1/0.3 单段）——攻击永远有节奏可用。
/// LocksMovement 声明锁移动（Locomotion 层的 ApplyLocomotion 消费：站桩贴地）。
/// 挥剑标记：Enter 挂 / Exit 摘 Swinging 标签——写方只命名不赋义，读方（武器 SO）
/// 是解释器（挥剑期间受伤乘数，见 WeaponSO.swingDamageTakenMultiplier）。
/// 失控打断：ModifierList 推 CC 层时会 ClearState(Action)（走 Exit 摘标记）——攻击不会在眩晕后"续播"。
/// 配置球形近战判定，仅命中窗口采样；同一段按实体去重，续段重新开始。动画后置。
/// </summary>
public sealed class EntityAttackState : EntityState
{
    /// <summary>内置默认段（出招表缺失/空表时的兜底节奏）</summary>
    private static readonly ComboEntry FistEntry = new()
    {
        Name = "双拳",
        Windup = 0.15f,
        Hit = 0.1f,
        Recovery = 0.3f,
        NextEntry = -1,
        CancelWindow = 0f,
    };

    private WeaponComboGraph? ComboGraph;   // 起手时刻的出招表快照（避免攻击中途换装串段）；表缺失 = 空
    private ComboEntry[]? Entries;          // 连段数组快照（ComboGraph.ComboEntries）；空表 = 走兜底单段
    private ComboEntry Single = FistEntry;    // 未起手时也可安全读取调试阶段。
    private bool IsSingle = true;              // true = 单发段模式（不续段）
    private int ComboIndex;             // 当前段下标（连段模式有效）
    private float Elapsed;              // 段内累积时间（+= deltaTime，不用 Time.time 差值——网络时间纪律）
    private readonly MeleeAttackHits Hits;
    private EnumSkillType? SkillKind;
    private PossessionProfileSO? PossessionProfile;
    private float PossessionAmount;

    public EntityAttackState(Entity entity, EntityStateMachine machine) : base(entity, machine)
    {
        LocksMovement = true;   // 构造期设定：攻击期间锁移动（能力声明，Locomotion 层仲裁）
        Hits = new MeleeAttackHits(entity);
    }

    public override EnumStateLayer Layer => EnumStateLayer.Action;

    public override string StateName => "Attack";

    /// <summary>当前段（起手快照；单发模式/兜底返回快照值）。
    /// 连段模式下 Entries 必有值（BeginCombo 里空表已转单发模式），故用 ! 说明该不变量</summary>
    public ComboEntry CurrentEntry => IsSingle ? Single : Entries![ComboIndex];

    /// <summary>只读阶段：0前摇、1命中、2后摇，供表现层呈现起手预警。</summary>
    public int PhaseIndex => Elapsed < SafeTime(CurrentEntry.Windup) * DurationScale ? 0
        : Elapsed < (SafeTime(CurrentEntry.Windup) + SafeTime(CurrentEntry.Hit)) * DurationScale ? 1 : 2;

    /// <summary>普攻起手：从连段第 0 段开始（Brain 在 Action 层空闲时调，随后 ChangeState 到本状态）。
    /// 表缺失/空表 → 兜底默认段（以单发模式跑）</summary>
    public void BeginCombo()
    {
        SkillKind = null;
        PossessionProfile = null;
        ComboGraph = Entity.Slots.CurrentComboGraph;
        Entries = ComboGraph != null && ComboGraph.ComboEntries != null
            ? (ComboEntry[])ComboGraph.ComboEntries.Clone() : null;
        if (Entries == null || Entries.Length == 0)
        {
            IsSingle = true;
            Single = FistEntry;
            return;
        }
        IsSingle = false;
        ComboIndex = 0;
    }

    /// <summary>单发段起手（技能攻击/将来的蓄力）：打完即收招，不按 nextEntry 续段</summary>
    public void BeginSingle(ComboEntry entry, EnumSkillType? skillKind = null, PossessionProfileSO? possessionProfile = null, float possessionAmount = 0f)
    {
        SkillKind = skillKind;
        PossessionProfile = possessionProfile;
        PossessionAmount = possessionAmount;
        IsSingle = true;
        Single = entry;
    }

    /// <summary>当前阶段名（调试面板显示用）</summary>
    public string Phase
    {
        get
        {
            ComboEntry entry = CurrentEntry;
            float scale = DurationScale;
            if (Elapsed < SafeTime(entry.Windup) * scale)
            {
                return $"{entry.Name} 前摇";
            }
            if (Elapsed < (SafeTime(entry.Windup) + SafeTime(entry.Hit)) * scale)
            {
                return $"{entry.Name} 命中";
            }
            return $"{entry.Name} 后摇";
        }
    }

    /// <summary>攻速缩放：时长 = 表值 ÷（武器攻速 × 效果攻速乘数）。
    /// 双持取主手武器攻速；空手 = 1。乘积钳下限 0.05 防除零（配置事故不卡死编辑器）</summary>
    private float DurationScale
    {
        get
        {
            WeaponSO? mainWeapon = Entity.Slots.Weapons.MainHand;
            float weaponSpeed = mainWeapon != null ? mainWeapon.AttackSpeed : 1f;
            float modifierSpeed = Entity.Brain.Modifiers.GetStatMultiplier(EnumStatType.AttackSpeed);
            float speed = weaponSpeed * modifierSpeed;
            if (float.IsNaN(speed) || float.IsInfinity(speed)) speed = 1f;
            return 1f / Mathf.Max(0.05f, speed);
        }
    }

    public override void Enter()
    {
        Elapsed = 0f;
        Hits.BeginSegment();
        Entity.Tags.Add((ulong)EnumEntityTag.Swinging);   // 挂挥剑标记（读方解释，见头注释）
    }

    public override void Exit()
    {
        Entity.Tags.Remove((ulong)EnumEntityTag.Swinging);   // 摘标记：打完/被打断（失控清层走的就是这里）
        Hits.BeginSegment();
        Entity.Commands.AttackQueued = false;
    }

    public override void HandleTransitions()
    {
        ComboEntry entry = CurrentEntry;
        float total = (SafeTime(entry.Windup) + SafeTime(entry.Hit) + SafeTime(entry.Recovery)) * DurationScale;
        if (Elapsed >= total)
        {
            // 打完当前段且未续段：清空 Action 层（层回到未激活 = 无动作）
            Machine.ClearState(Layer);
        }
    }

    public override void Tick(float deltaTime)
    {
        if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
        float previous = Elapsed;
        Elapsed += deltaTime;
        ComboEntry activeEntry = CurrentEntry;
        float scaleNow = DurationScale;
        float hitStart = SafeTime(activeEntry.Windup) * scaleNow;
        float hitEnd = hitStart + SafeTime(activeEntry.Hit) * scaleNow;
        // 区间相交而非只检查当前帧：低帧率跨过整个命中窗口也会采样一次。
        if (activeEntry.Hit > 0f && previous < hitEnd && Elapsed >= hitStart)
            Hits.Sample(activeEntry, SkillKind, PossessionProfile, PossessionAmount);

        // 续段判定（仅连段模式）：后摇起点起取消窗口内收到攻击请求 → 跳 nextEntry。
        // 攻击中再按攻击键的请求不经 Brain 起手（CanAct 挡 Action 层活跃），
        // AttackQueued 保留到这里消费——消费后无条件清空（无缓冲）
        if (!IsSingle && Entity.Commands.AttackQueued)
        {
            ComboEntry entry = CurrentEntry;
            float scale = DurationScale;
            float recoveryStart = (SafeTime(entry.Windup) + SafeTime(entry.Hit)) * scale;
            float window = entry.CancelWindow > 0f ? SafeTime(entry.CancelWindow) * scale : SafeTime(entry.Recovery) * scale;
            if (Elapsed >= recoveryStart && Elapsed <= recoveryStart + window && entry.NextEntry >= 0
                && entry.NextEntry < Entries!.Length)   // 连段模式下 Entries 必非空（空表已转单发模式）
            {
                ComboIndex = entry.NextEntry;
                Entity.Brain.FaceAttackDirection();
                Elapsed = 0f;   // 续段从新段的前摇开始（Swinging 保持挂载——状态未离开）
                Hits.BeginSegment();
            }
            Entity.Commands.AttackQueued = false;
        }
        // 移动由 Locomotion 层锁住后站桩；转向沿用 Brain 的指令编排。
    }

    private static float SafeTime(float value) => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value) ? value : 0f;
}
