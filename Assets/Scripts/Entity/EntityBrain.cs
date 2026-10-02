using Assets.Scripts.Entity.Data.Skill;
using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// 角色大脑（Brain 层）：全实体唯一 Update 的编排者——"怎么做"的每帧答案。
/// 旧 NpcController.Update 管线平移（语义逐条保真），门禁从散落的 CC 层检查统一进 Capability：
/// 1 帧首重置电平指令（输入源沉默 = 站桩，附身切换帧安全）；
/// 2 输入源采集（玩家/AI 汇流同一 CommandBuffer；"指令翻译成什么"也在这里——瞄准+攻击=射击）；
/// 3 修饰列表步进（投影先于状态机 = 失控当帧压制；帧末施加的效果次帧压制）+ 技能冷却步进；
/// 4 死亡门（清边沿指令 return；不用 enabled=false——那会杀掉网络回调，多人纪律）；
/// 5 地面检测 → 6 主动作消费（起手当帧进前摇）→ 7 状态机两遍 Tick（水平移动/连段推进）
/// → 8 跳跃消费 → 9 重力 → 10 转向 → 11 调试采样。
/// 输入源绑定：控制状态的唯一真相 = InputSource（is PlayerInputSource 即玩家驱动）；
/// Entity.startPlayerControlled 只决定开局绑定，双源同挂物体、运行时切换只换绑定（F10 附身演示）。
/// deltaTime 全链传参（换 NetworkTime/固定 tick 只改本类取时一处——网络时间纪律）。
/// </summary>
[RequireComponent(typeof(EntityMotor))]
[RequireComponent(typeof(CharacterVitals))]
[RequireComponent(typeof(CharacterSlotContainer))]
[RequireComponent(typeof(PlayerInputSource))]
[RequireComponent(typeof(AITreeInputSource))]
public sealed class EntityBrain : MonoBehaviour
{
    // ---- 装配不变量 ----
    // 下面这批（Commands/StateMachine/Modifiers/ArmorSets/Capability 与七个状态实例）
    // 由 Bootstrap 一次性构造，**Bootstrap 之后永远非空**——故按非空不变量声明，
    // `= null!` 是对编译器的断言（零运行时开销，不是赋 null）。
    // 这样 状态转换/状态机调用点 不必满屏 `!`：真正的装配门只有一个（Update 开头查 Entity）。
    // 可空的只有两个：Entity（Bootstrap 参数）与 InputSource（允许绑定失败 = 站桩）。

    /// <summary>宿主实体（Bootstrap 注入；装配前为 null）</summary>
    public Entity? Entity { get; private set; }

    /// <summary>当前绑定的输入源（null = 无输入，站桩；绑定/切换见 BindInputSource）</summary>
    public IInputSource? InputSource { get; private set; }

    /// <summary>指令缓冲（输入源写、管线消费）</summary>
    public CommandBuffer Commands { get; private set; } = null!;

    /// <summary>分层状态机（Bootstrap 构造）</summary>
    public EntityStateMachine StateMachine { get; private set; } = null!;

    /// <summary>修饰列表（Bootstrap 构造）</summary>
    public ModifierList Modifiers { get; private set; } = null!;

    /// <summary>护甲套装引擎（Bootstrap 构造）：装备/卸下护甲后由 SlotContainer 调 Sync 重算档位；
    /// 变更驱动，不在每帧管线里</summary>
    public ArmorSetBonusList ArmorSets { get; private set; } = null!;

    /// <summary>能力仲裁（"能不能"统一查询口）</summary>
    public EntityCapabilities Capability { get; private set; } = null!;

    // 状态实例（Bootstrap 构造一次，转换时互相引用；零 GC）
    public EntityIdleState IdleState { get; private set; } = null!;
    public EntityWalkState WalkState { get; private set; } = null!;
    public EntitySprintState SprintState { get; private set; } = null!;
    public EntityGroundedState GroundedState { get; private set; } = null!;
    public EntityAirState AirState { get; private set; } = null!;
    public EntityAttackState AttackState { get; private set; } = null!;
    public EntityStunState StunState { get; private set; } = null!;

    /// <summary>失控呈现注册表：Modifier SO 的 controlKind → CC 层状态。
    /// 新失控（冰冻/石化）= 枚举成员 + 新状态类 + 这里注册一行，全"加"零"改"</summary>
    private readonly Dictionary<EnumControlKind, EntityState> controlStates = new();

    /// <summary>失控条目 → CC 层呈现状态：查注册表，未注册回落 Stun（数据配错不断链）</summary>
    public EntityState ResolveControlState(EnumControlKind kind)
    {
        return controlStates.TryGetValue(kind, out EntityState state) ? state : StunState;
    }

    // 两输入源组件（双源同挂是附身演示的前提；缺失对应源时该侧绑定失败并警告）
    public PlayerInputSource? PlayerSource;
    public AITreeInputSource? AiSource;
    private bool WarnedMissingSource;

    // 调试采样：本帧实测速度（位置差反推，比状态选的速度更可信，能反映碰撞和重力）
    private Vector3 LastFramePosition;

    // 首帧附身唯一性自检标记（一次性）
    private bool CheckedPossessionUniqueness;

    // 当前生效的附身会话载体（buff 侧是唯一真相；这里只记"上一帧挂的是哪一份"，
    // 用于识别"被摘掉了"= 会话结束，从而换回输入源）
    private PossessionEffect? ActivePossession;

    /// <summary>首帧自检：提示"多个玩家驱动实体共享本地输入设备"的单机现象。
    /// 多人语义（需求钦定）：玩家驱动 = Brain.InputSource 是 PlayerInputSource——多人下
    /// N 个玩家驱动实体是常态（各自 PlayerInput 配对各自设备 / 走网络中继）；单机调试下
    /// 多个实体被玩家驱动却没有分设备配对，键盘会同时驱动它们（典型成因：复制实体/
    /// 手搭场景多勾了 startPlayerControlled）。
    /// 首帧跑保证所有实体已 Awake（顺序未定义，Bootstrap 里扫会漏）；只提示不裁决</summary>
    private void WarnIfMultiplePlayerControlled(Entity host)
    {
        if (InputSource is not PlayerInputSource)
        {
            return;
        }
        foreach (Entity other in FindObjectsByType<Entity>())
        {
            if (other != host && other.Brain.InputSource is PlayerInputSource)
            {
                Debug.LogWarning($"{name} 与 {other.name} 都由玩家驱动：多人下这是常态" +
                    "（各自设备配对/网络中继）；但单机调试会共享键盘一起动——若非有意分屏，" +
                    "请检查是否多勾了 startPlayerControlled（控制权转移用 F10）");
            }
        }
    }

    /// <summary>本帧实测速度（调试面板读）</summary>
    public Vector3 DebugVelocity { get; private set; }

    /// <summary>
    /// 装配（Entity.Awake 调用一次，全实体唯一初始化入口——单 Awake 规则）：
    /// 注入组件 → 建状态机与七状态 → 能力/修饰/指令缓冲 → 护甲套装引擎 → 初始化 Vitals → 绑定输入源
    /// → 重算护甲套装档位（覆盖 Inspector 预配的初始装备）。
    /// </summary>
    public void Bootstrap(Entity entity)
    {
        Entity = entity;

        Entity.Motor.Initialize();
        Entity.Vitals.Initialize(entity.Config);

        Commands = new CommandBuffer();
        Modifiers = new ModifierList(entity);
        Capability = new EntityCapabilities(entity);
        // 护甲套装引擎：容器持有引用（装备写入点 ⇒ 套装重算成对），Brain 也持有便于读点
        ArmorSets = new ArmorSetBonusList(entity);
        Entity.Slots.ArmorSetBonuses = ArmorSets;

        StateMachine = new EntityStateMachine();
        // Locomotion 层（水平移动，空中照常执行）
        IdleState = new EntityIdleState(entity, StateMachine);
        WalkState = new EntityWalkState(entity, StateMachine);
        SprintState = new EntitySprintState(entity, StateMachine);
        // Aerial 层（竖直姿态：起跳/离地/落地判定）
        GroundedState = new EntityGroundedState(entity, StateMachine);
        AirState = new EntityAirState(entity, StateMachine);
        // Action 层（主动动作；平时不激活，TryConsumeAction 触发时才进状态）
        AttackState = new EntityAttackState(entity, StateMachine);
        // CrowdControl 层（失控；平时不激活，ModifierList 投影时才进状态）
        StunState = new EntityStunState(entity, StateMachine);
        StateMachine.Initialize(IdleState, GroundedState);   // 各层初始状态

        // 失控呈现注册（EnumControlKind.Stun → 眩晕态；将来冰冻在此加一行）
        controlStates[EnumControlKind.Stun] = StunState;

        PlayerSource = GetComponent<PlayerInputSource>();
        AiSource = GetComponent<AITreeInputSource>();
        // 开局绑定：startPlayerControlled 只在这里读一次（运行时控制状态一律看 InputSource）
        BindInputSource(entity.StartPlayerControlled ? PlayerSource : AiSource);

        // 覆盖 Inspector 预配的初始装备：开局穿在身上的套装档位立即生效（变更驱动，不占每帧管线）
        ArmorSets.Sync();

        LastFramePosition = transform.position;
    }

    /// <summary>绑定输入源（控制权唯一写口）：传实例本身——通常传本实体的双源组件之一，
    /// null = 回落绑 AITreeInputSource（"还给 AI"就是传 null）。
    /// 将来新源（网络中继等）实现 IInputSource 后直接传进来，双源组件自动双双停用。
    /// 调用后"是否玩家控制"以 InputSource is PlayerInputSource 为准；
    /// 启用被绑定源、停用其余（SendMessage 不看组件 enabled，源内部还有 bound 守门双保险）。
    /// 回落目标 AI 源未挂时警告一次并保持无输入（站桩）</summary>
    public void BindInputSource(IInputSource? inputSource)
    {
        var target = inputSource ?? AiSource;
        if (target == null)
        {
            if (!WarnedMissingSource)
            {
                WarnedMissingSource = true;
                Debug.LogWarning($"{name}：null 绑定的回落目标 AITreeInputSource 组件缺失，实体无输入（站桩）。此警告只提示一次");
            }
            return;
        }

        // 先落字段再通知：控制状态唯一真相 = InputSource（读方如 CameraRig 据此判"谁在驱动"）
        InputSource = target;

        // 启用被绑定源、停用其余（按接口行动，不判断具体类型——将来网络中继等第三方源自动适配）。
        // 注意：这里必须判断刚绑定的 target，不能判断字段旧值（首绑时字段为 null → 两个源都收不到通知，
        // 结果是玩家源未激活 → 相机不亮、无输入 = "No Camera Rendering"）
        if (ReferenceEquals(target, PlayerSource))
        {
            PlayerSource!.Activate();
            AiSource?.Deactivate();
            return;
        }
        if (ReferenceEquals(target, AiSource))
        {
            AiSource!.Activate();
            PlayerSource?.Deactivate();
            return;
        }

        // 第三方源（网络输入中继等）：双源组件都让位
        PlayerSource?.Deactivate();
        AiSource?.Deactivate();
    }

    // ---- 附身会话（buff 驱动，无专门管理器）----
    //
    // 需求钦定：附身会话**用 buff 表达**——挂上「被附身」buff = 会话开始，摘掉 = 会话结束。
    // 于是：倒计时由 ModifierList 的 Duration 免费提供；判定条件也是 buff 在场
    //（有 Possessed = 身体已被占用；有 SoulOut = 我已在附身中），不依赖任何场景级单例，
    // 多人下各客户端用同一套规则就能初步裁决"能不能附身"。
    // 本类只做两件事：发起时的闸门、以及每帧识别"载体的挂/摘"来换（换回）输入源。

    /// <summary>会话进行中（载体 buff 在挂）。读方：HUD、二次发起闸门</summary>
    public bool IsPossessing => ActivePossession != null;

    /// <summary>本会话载体的剩余秒数（&lt;= 0 = 没有会话；永久载体返回 -1）</summary>
    public float PossessionRemaining =>
        ActivePossession != null ? Modifiers.GetRemaining(ActivePossession) : 0f;

    /// <summary>灵魂出窍中（身上挂着 SoulOut 角色的附身效果 = 我的操作权正在别处）。HUD 与将来 UI 用</summary>
    public bool HasSoulOut =>
        Modifiers.GetHeld<IPossessionEffect>()?.PossessionRole == EnumPossessionRole.SoulOut;

    /// <summary>
    /// 发起附身（附身效果 buff 是会话载体；F10 调试链与将来的瞄准选目标都走这里）。
    /// 闸门（全部读 buff，不看任何场景管理器）：
    /// 「被附身」buff 已在挂 = 这具身体已被占用 → 拒绝；
    /// 发起者已有「灵魂出窍」= 我已在附身中 → 拒绝；
    /// 任一方死亡 → 拒绝；载体效果为空（资产没配）→ 拒绝。
    /// 施加顺序：先给目标挂载体（会话从这一刻起算），再给出窍者挂标记。
    /// </summary>
    public bool TryBeginPossession(Entity target, PossessionEffect possessionEffect, PossessionEffect? soulOutEffect = null)
    {
        Entity? host = Entity;
        if (host == null || target == null || host == target || possessionEffect == null)
        {
            return false;
        }

        bool targetOccupied = target.Brain.Modifiers.GetHeld<IPossessionEffect>() != null;
        bool hostBusy = soulOutEffect != null && host.Brain.Modifiers.IsHolding(soulOutEffect);
        if (targetOccupied || hostBusy || host.IsDead || target.IsDead)
        {
            return false;
        }

        target.Brain.Modifiers.Apply(possessionEffect);                                  // 会话载体：Duration 即时长
        if (soulOutEffect != null)
        {
            host.Brain.Modifiers.Apply(soulOutEffect);                                   // 出窍者侧纯标记
        }

        // 立刻收敛一次：本帧就完成换绑（不等下一帧 Tick），发起方手感无延迟
        // （注意：收敛的是对方的 Brain，所以用对方自己的实例方法 + 对方的 ModifierList）
        target.Brain.ReconcilePossession(target.Brain.Modifiers);
        ReconcilePossession(Modifiers);
        return true;
    }

    /// <summary>
    /// 显式中断入口（将来网络强制换人/死亡观战切人走它；玩家按键不调它——不能中途跑路）。
    /// 实现就是摘掉自己身上的载体 buff：摘 = 结束，与"到期自动摘"同一条路
    /// </summary>
    public void EndPossession()
    {
        if (ActivePossession != null)
        {
            Modifiers.Remove(ActivePossession);
        }
    }

    /// <summary>
    /// 每帧收敛：载体 buff 的挂/摘 ⇄ 输入源绑定。
    /// 挂上 → 本实体由玩家驱动（有玩家源就绑，没有则什么也不做——保持原绑定）；
    /// 摘掉 → 本实体还回 AI 驱动（没有 AI 源则保持原绑定，只清会话标记）。
    /// 之所以能这么短：换绑本来就是本类的邻居能力（BindInputSource），不需要新管理器。
    /// 前提：传入的必须是**本 Brain 宿主**的 ModifierList（发起方用它去收敛对方的 Brain）
    /// </summary>
    private void ReconcilePossession(ModifierList modifiers)
    {
        PossessionEffect? possessed = modifiers.GetHeld<PossessionEffect>();
        if (ReferenceEquals(possessed, ActivePossession))
        {
            return;   // 状态没变（含"都没有"的常态）：零分配零动作
        }

        ActivePossession = possessed;
        if (possessed == null)
        {
            if (AiSource != null)
            {
                BindInputSource(AiSource);   // 会话结束：还回 AI
            }
            return;
        }

        if (PlayerSource != null)
        {
            BindInputSource(PlayerSource);   // 会话开始：接管本地玩家输入
        }
    }

    /// <summary>
    /// 场景里的附身候选：另一个非玩家驱动、存活、且身上没有「被附身」buff 的实体。
    /// 多人纪律：绝不抢已被占用的身体（buff 判定）。FindObjectsByType 只在调试/选目标入口跑，不上玩法路径
    /// </summary>
    public static Entity? FindPossessionCandidate(Entity self)
    {
        foreach (Entity candidate in FindObjectsByType<Entity>())
        {
            if (candidate == self || candidate.IsDead
                || candidate.Brain.InputSource is PlayerInputSource                     // 别的玩家在操作
                || candidate.Brain.Modifiers.GetHeld<IPossessionEffect>() != null)      // 已被附身占用
            {
                continue;
            }
            return candidate;
        }
        return null;
    }

    // 帧内顺序（勿调整），语义对照见类头注释
    private void Update()
    {
        float deltaTime = Time.deltaTime;   // 全链传参：换网络时间只改这一行

        // 0) 装配门：Bootstrap 未完成（或被销毁）时不跑管线。
        //    同时把可空属性收窄成局部（null 分析只需一次判断，后续零判空也零 `!`）
        CommandBuffer? commands = Commands;
        EntityStateMachine? machine = StateMachine;
        ModifierList? modifiers = Modifiers;
        Entity? host = Entity;
        if (commands == null || machine == null || modifiers == null || host == null)
        {
            return;
        }

        // 0.5) 首帧自检（一次性）：多个玩家驱动实体 = 装配事故（见方法注释）
        if (!CheckedPossessionUniqueness)
        {
            CheckedPossessionUniqueness = true;
            WarnIfMultiplePlayerControlled(host);
        }

        // 1) 帧首重置电平型指令：输入源沉默 = 站桩。
        //    边沿型指令不在此列——由消息回调/决策在帧间置位，帧首清会丢输入
        commands.ResetLevels();

        // 2) 输入源采集（玩家/AI 汇流；无输入源 = 全零站桩）
        InputSource?.GatherCommands(commands);

        // 3) 修饰列表（时长/周期跳伤/CC 投影）+ 技能冷却步进
        modifiers.Tick(deltaTime);
        host.Slots.Skills.TickCooldown(deltaTime);

        // 3.5) 附身会话：buff 是唯一真相——摘掉「被附身」buff 即会话结束（Duration 到期自动摘）
        ReconcilePossession(modifiers);

        // 4) 死亡门：周期跳伤致死当帧冻结余下管线（尸体站桩）。
        //    清边沿防"复活瞬间残留的跳/攻击请求"；每帧清幂等，成本可忽略
        if (host.Vitals.IsDead)
        {
            commands.ClearEdges();
            return;
        }

        // 5) 地面检测（贴地钳 -2 语义在 Motor）
        host.Motor.GroundCheck();

        // 6) 主动作消费：起手当帧进前摇（放状态机之前，语义保真）
        TryConsumeAction(host, commands, machine, modifiers);

        // 7) 状态机两遍 Tick（全层转换 → 全层动作；CC 压制惰性求值；连段推进在 AttackState）
        machine.TwoPassTick(deltaTime);

        // 8) 跳跃消费（冲量类过 CC 门禁，防绕过层压制）
        TryConsumeJump(host, commands, machine, modifiers);

        // 9) 重力步进（顶点滞空/下落加重）
        host.Motor.ApplyGravityAndVerticalMove(deltaTime);

        // 10) 转向（有移动方向时平滑转向）
        host.Motor.RotateTowards(commands.MoveDirection, deltaTime);

        // 11) 调试采样：位置差反推真实移动速度（一次减法可忽略）
        DebugVelocity = (transform.position - LastFramePosition) / deltaTime;
        LastFramePosition = transform.position;
    }

    /// <summary>主动作消费：攻击边沿（含瞄准射击翻译与连段留置）+ 技能边沿（双闸门）。
    /// 全部指令不区分来源——玩家按键与 AI 决策写的是同一个缓冲。
    /// host/commands/machine/modifiers 由 Update 的局部传入（已非空收窄），方法体内无需判空</summary>
    private void TryConsumeAction(Entity host, CommandBuffer commands, EntityStateMachine machine, ModifierList modifiers)
    {
        // ---- 攻击：Action 层空闲时起手并消费；攻击中留给 AttackState.Tick 的续段判定 ----
        if (commands.AttackQueued && machine.GetActive(EnumStateLayer.Action) == null)
        {
            if (Capability.CanAct())
            {
                WeaponComboGraph? comboGraph = host.Slots.CurrentComboGraph;
                if (commands.AimActive && comboGraph != null && comboGraph.HasShoot)
                {
                    // 连招翻译（需求示例：瞄准+射击）：瞄准电平 + 攻击边沿 → 射击变体（单发段）。
                    // 翻译在消费点不在输入源——AI 输入源自动同享规则，不漂移
                    AttackState.BeginSingle(comboGraph.ShootEntry);
                }
                else
                {
                    AttackState.BeginCombo();
                }
                machine.ChangeState(AttackState);
            }
            commands.AttackQueued = false;   // 起手或门禁拒绝都清（无缓冲；攻击中的续段由状态自己消费）
        }

        // ---- 技能：双闸门（冷却 + 信心方向）都过才结算；执行效果后置（本轮只扣闸门，Debug 可见）----
        if (commands.SkillSlotQueued != 0)
        {
            EnumSkillType kind = (EnumSkillType)commands.SkillSlotQueued;   // ±1 = 冠冕/潮汐，枚举值即方向因子
            SkillResource? faith = host.Vitals.Faith;
            if (Capability.CanAct() && host.Slots.Skills.CanCast(kind, faith))
            {
                host.Slots.Skills.TryGet(kind, out SkillSO? skill, out _);
                host.Slots.Skills.Consume(kind, faith);   // 写回 Data：冷却 + 信心增量（位方向 × 幅度）
                if (skill != null)
                {
                    Debug.Log($"{name} 释放 {skill.Name}（冷却 {skill.Cooldown:0.0}s 已启动、信心 {(int)kind * skill.Faith:+#;-#;0}，执行效果后置）");
                }
            }
            commands.SkillSlotQueued = 0;   // 无缓冲
        }
    }

    /// <summary>跳跃：Capability 门禁（地面 + 未失控）通过就施加冲量；随后无条件清空（无缓冲）。
    /// 冲量在高度平方根前缩放 JumpPower 乘数：×2 乘数 ≈ 跳 1.41 倍高。
    /// host 由 Update 的局部传入（已非空收窄）</summary>
    private void TryConsumeJump(Entity host, CommandBuffer commands, EntityStateMachine machine, ModifierList modifiers)
    {
        if (!commands.JumpQueued)
        {
            return;
        }

        if (Capability.CanJump())
        {
            float jumpPower = modifiers.GetStatMultiplier(EnumStatType.JumpPower);
            host.Motor.VerticalVelocity = Mathf.Sqrt(
                host.Motor.JumpHeight * jumpPower * -2f * host.Motor.Gravity);
        }
        commands.JumpQueued = false;
    }

    /// <summary>受到伤害（统一入口，命中/周期跳伤/环境都走这里，别处不得直改 Vitals）：
    /// 入口修正 = DamageTaken 乘法链 × 挥剑减伤（Swinging 标签的武器解释器），
    /// 终值交给 Vitals.ApplyDamage（唯一扣血写口，Data 层不反向依赖 Logic）。
    /// 附带受击降信心（FaithLossPerHit，见方法尾；击退/受击硬直等仍由施加方另行 Apply Modifier）</summary>
    public void TakeDamage(float rawAmount)
    {
        // 装配门 + 收窄成局部（伤害入口可能被场景里的其他实体在装配中途调用）
        ModifierList? modifiers = Modifiers;
        Entity? host = Entity;
        if (host == null || modifiers == null || host.Vitals.IsDead || rawAmount <= 0f)
        {
            return;
        }

        float swingMultiplier = 1f;
        if (host.Tags.Has((ulong)EnumEntityTag.Swinging))
        {
            var mainWeapon = host.Slots.Weapons.MainHand;
            if (mainWeapon != null && mainWeapon.SwingDamageTakenMultiplier > 0f)
            {
                swingMultiplier = mainWeapon.SwingDamageTakenMultiplier;
            }
        }

        float final = rawAmount
            * modifiers.GetStatMultiplier(EnumStatType.DamageTaken)
            * swingMultiplier;
        host.Vitals.ApplyDamage(final);

        // 受伤动摇信心（核心玩法钩子）：被命中不问来源——被自己附身的怪打、多人被队友误伤同规则，
        // 信心往潮汐方向降（(int)Tide × 幅度 = -幅度，方向因子纪律与技能链同构）。
        // 被动损失不走 CanCast 双闸门：SkillResource 内部钳制，贴边 -67 自然封底
        int faithLoss = host.Config?.FaithLossPerHit ?? 0;
        if (faithLoss > 0)
        {
            host.Vitals.Faith?.Update((int)EnumSkillType.Tide * faithLoss);
        }
    }
}
