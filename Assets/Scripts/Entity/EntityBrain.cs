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
/// 输入源绑定：IsPlayerControlled 的唯一职责（true=PlayerInputSource，false=AITreeInputSource）；
/// 双源同挂物体、切换只换绑定（附身演示）。
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
    private PlayerInputSource? PlayerSource;
    private AITreeInputSource? AiSource;
    private bool WarnedMissingSource;

    // 调试采样：本帧实测速度（位置差反推，比状态选的速度更可信，能反映碰撞和重力）
    private Vector3 LastFramePosition;

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
        BindInputSource(entity.IsPlayerControlled);

        // 覆盖 Inspector 预配的初始装备：开局穿在身上的套装档位立即生效（变更驱动，不占每帧管线）
        ArmorSets.Sync();

        LastFramePosition = transform.position;
    }

    /// <summary>绑定输入源（IsPlayerControlled 的唯一职责，运行时切换 = 附身）：
    /// 启用目标源、停用另一个（SendMessage 不看组件 enabled，源内部还有 bound 守门双保险）。
    /// 对应源未挂（如纯敌人没挂 PlayerInputSource）时警告一次并保持无输入（站桩）</summary>
    public void BindInputSource(bool playerControlled)
    {
        IInputSource? target = playerControlled ? PlayerSource : AiSource;
        if (target == null)
        {
            if (!WarnedMissingSource)
            {
                WarnedMissingSource = true;
                Debug.LogWarning($"{name}：缺少{(playerControlled ? "PlayerInputSource" : "AITreeInputSource")}组件，实体无输入（站桩）。此警告只提示一次");
            }
        }
        // 两侧都通知：被绑定的激活、另一个停用（null 安全——缺挂的源本来就没人写指令）
        if (PlayerSource != null)
        {
            PlayerSource.SetActive(playerControlled);
        }
        if (AiSource != null)
        {
            AiSource.SetActive(!playerControlled);
        }
        InputSource = target;
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

        // 1) 帧首重置电平型指令：输入源沉默 = 站桩。
        //    边沿型指令不在此列——由消息回调/决策在帧间置位，帧首清会丢输入
        commands.ResetLevels();

        // 2) 输入源采集（玩家/AI 汇流；无输入源 = 全零站桩）
        InputSource?.GatherCommands(commands);

        // 3) 修饰列表（时长/周期跳伤/CC 投影）+ 技能冷却步进
        modifiers.Tick(deltaTime);
        host.Slots.Skills.TickCooldown(deltaTime);

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
    /// 击退/受击硬直等由施加方另行 Apply Modifier，本入口只管数值</summary>
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
    }
}
