using UnityEngine;

/// <summary>
/// NPC 控制器：统一角色层（玩家与 NPC 在此同构）。
/// "NPC" 在本项目 = 角色壳：分层状态机（Locomotion/Aerial/Action/CrowdControl，见 StateLayer）
/// + 黑板指令区 + 每帧运动管线（中段挂效果容器：Buff/Debuff 的 CC 投影/数值乘数/周期跳伤，
/// 统一伤害入口 TakeDamage——命名状态三种机制见 EntityArchitecture.md）。谁在写指令区（输入源）是叶子的唯一差异：
/// - PlayerController（叶子）：读输入设备，翻译成指令写黑板；
/// - EnemyController（叶子）：跑决策树，把意图翻译成指令写同一块黑板。
/// 后续"玩家操作 NPC"= 给实体换输入源叶子（附身操作序列见 EntityArchitecture.md）。
/// 叶子规则：只 override InitEntity（先调 base 建状态机）与 UpdateCommands / UpdateDebugInput；
/// 不要声明 Update——管线唯一声明在此（protected virtual，叶子误写会收到编译器隐藏警告而非静默失效）。
/// </summary>
public abstract class NpcController : EntityController
{
    [Header("跳跃")]
    [Tooltip("跳跃能达到的最大高度，单位：米")]
    public float jumpHeight = 1.2f;

    [Header("加速")]
    [Tooltip("加速倍率：加速时的速度 = walkSpeed × 此值，调走路速度后加速自动跟着变")]
    public float sprintMultiplier = 1.6f;

    // ---- 状态机与黑板（InitEntity 中构建）----
    private NpcBlackboard blackboard;
    private StateMachine<NpcBlackboard> stateMachine;

    public StateMachine<NpcBlackboard> StateMachine => stateMachine;   // 调试面板读取当前状态用

    /// <summary>类型化黑板（即基类 Board；叶子经此访问指令区，不再自持黑板字段）</summary>
    protected NpcBlackboard NpcBoard => blackboard;

    // 状态实例：InitEntity 构造一次，转换时互相引用（不 new，零 GC）。玩家与 NPC 共用同一套
    public NpcIdleState IdleState { get; private set; }
    public NpcWalkState WalkState { get; private set; }
    public NpcSprintState SprintState { get; private set; }
    public NpcAirState AirState { get; private set; }
    public NpcGroundedState GroundedState { get; private set; }   // Aerial 层着地态
    public NpcStunState StunState { get; private set; }           // CrowdControl 层眩晕态
    public NpcAttackState AttackState { get; private set; }       // Action 层攻击态

    // 状态要读的参数（WalkSpeed 等通用配置在 EntityController；攻击节奏与体质在 CharacterEquipment）
    public float SprintMultiplier => sprintMultiplier;

    // ---- 战斗查询口（命中/效果/状态系统读这里，不直接碰黑板容器）----

    /// <summary>效果容器（Apply 施加 / Dispel 驱散 / HasControlActive 失控判定）</summary>
    public StatusEffectContainer Effects => blackboard.Effects;

    /// <summary>命名标签集合（Has 查询；写入走状态直写或容器投影）</summary>
    public TagSet Tags => blackboard.Tags;

    /// <summary>最大生命（活属性：装备组合查询，Play 模式改角色定义即时生效；不进黑板）</summary>
    public float MaxHealth => Equipment.MaxHealth;

    /// <summary>统计乘数直通（效果容器乘法链；各乘数的消费读点清单见 StatType 头注释）</summary>
    public float GetStatMultiplier(StatType type) => blackboard.Effects.GetStatMultiplier(type);

    /// <summary>失控条目 → CC 层呈现状态的映射：默认一律眩晕态；将来冰冻/石化由具体角色
    /// 覆写此方法返回自己的状态（容器按最高优先级条目取呈现，多挂载单表达）</summary>
    public virtual State<NpcBlackboard> ResolveControlState(StatusEffectData effect) => StunState;

    /// <summary>霸体：任一活跃状态声明 GrantsSuperArmor 即算，命中入口据此仲裁是否施加 CC</summary>
    public override bool HasSuperArmor => stateMachine != null && stateMachine.HasSuperArmor();

    // 调试采样：本帧实测速度（位置差反推，比状态选的速度更可信，能反映碰撞和重力）
    private Vector3 lastFramePosition;
    protected Vector3 DebugVelocity { get; private set; }   // 玩家叶子的调试面板读

    private bool dead;   // 死亡标志（TakeDamage 幂等门；复活功能后置，置位后本实体不再接受伤害/指令）

    protected sealed override EntityBlackboard CreateBlackboard()
    {
        blackboard = new NpcBlackboard
        {
            Controller = this,
        };
        return blackboard;
    }

    protected override void InitEntity()
    {
        stateMachine = new StateMachine<NpcBlackboard>();
        // Locomotion 层（水平移动，空中照常执行）
        IdleState = new NpcIdleState(blackboard, stateMachine);
        WalkState = new NpcWalkState(blackboard, stateMachine);
        SprintState = new NpcSprintState(blackboard, stateMachine);
        // Aerial 层（竖直姿态：起跳/离地/落地判定）
        GroundedState = new NpcGroundedState(blackboard, stateMachine);
        AirState = new NpcAirState(blackboard, stateMachine);
        // Action 层（主动动作；平时不激活，TryConsumeSkill 触发时才进状态）
        AttackState = new NpcAttackState(blackboard, stateMachine);
        // CrowdControl 层（失控；平时不激活，触发时才进状态）
        StunState = new NpcStunState(blackboard, stateMachine);
        stateMachine.Initialize(IdleState, GroundedState);   // 各层初始状态

        // 生命初始化放在最后：MaxHealth 读装备组合（Awake 已取到组件，此时就绪）
        blackboard.CurrentHealth = MaxHealth;

        lastFramePosition = transform.position;
    }

    /// <summary>输入源挂点：把本帧指令写进黑板指令区（玩家叶子=设备翻译，NPC 叶子=决策树翻译）</summary>
    protected virtual void UpdateCommands() { }

    /// <summary>调试输入挂点（管线末尾）：F3 眩晕等调试触发</summary>
    protected virtual void UpdateDebugInput() { }

    // 帧内顺序（勿调整）：
    // 帧首重置电平型指令 → 输入源写指令 → 效果容器（时长/周期跳伤/CC 投影；先于状态机 =
    // 投影当帧压制，帧末施加的效果次帧压制） → 死亡门 → 地面检测(基类) → 技能消费
    // (进攻击等 Action 状态) → 状态机（水平移动） → 跳跃 → 重力(基类) → 转向(基类) → 调试采样 → 调试输入
    protected virtual void Update()
    {
        // 帧首重置电平型指令：输入源沉默 = 站桩（附身切换帧、叶子未来的早退路径都安全）。
        // 边沿型指令（JumpQueued/QueuedSkillSlot）不在此列——由消息回调/决策在帧间置位，清了会丢输入
        blackboard.SprintActive = false;
        blackboard.MoveDirection = Vector3.zero;

        UpdateCommands();
        blackboard.Effects.Tick(this, Time.deltaTime);

        if (blackboard.CurrentHealth <= 0f)
        {
            return;   // 周期跳伤致死：Die 已关 enabled，此为最后一帧——余下管线（移动/重力）不再跑，尸体站桩
        }

        UpdateGroundCheck();

        TryConsumeSkill();
        stateMachine.Tick();

        TryConsumeJump();
        ApplyGravityAndVerticalMove();
        ApplyRotation();

        // 调试采样：用本帧位置差反推真实移动速度（对敌人也跑，一次减法可忽略）
        DebugVelocity = (transform.position - lastFramePosition) / Time.deltaTime;
        lastFramePosition = transform.position;

        UpdateDebugInput();
    }

    /// <summary>跳跃：本帧按下过、在地面、且未失控就施加冲量；随后无条件清空请求（无缓冲）。
    /// 眩晕门禁：冲量类效果（跳跃/将来的击退/闪避）都要过这一道，否则会绕过状态机的层压制</summary>
    private void TryConsumeJump()
    {
        bool stunned = stateMachine.GetActive(StateLayer.CrowdControl) != null;
        if (!stunned && blackboard.IsGrounded && blackboard.JumpQueued)
        {
            // 跳跃力乘数（JumpPower 乘法链）在高度平方根前缩放：×2 乘数 ≈ 跳 1.41 倍高
            float jumpPower = GetStatMultiplier(StatType.JumpPower);
            blackboard.VerticalVelocity = Mathf.Sqrt(jumpHeight * jumpPower * -2f * gravity);
        }
        blackboard.JumpQueued = false;
    }

    /// <summary>技能指令消费：有排队槽位且未失控就起手攻击（槽 0 = 普攻）；
    /// Action 层已活跃时忽略本次（攻击期间不重复起手，连招续段是 M3 的事）；
    /// 随后无条件清空（无缓冲，同 TryConsumeJump 模式）。
    /// 放在状态机 Tick 之前：起手当帧 AttackState 即进入前摇。
    /// 指令不区分来源——玩家按键与 AI 决策写的是同一个 QueuedSkillSlot（指令层）</summary>
    private void TryConsumeSkill()
    {
        if (blackboard.QueuedSkillSlot < 0)
        {
            return;
        }

        bool stunned = stateMachine.GetActive(StateLayer.CrowdControl) != null;
        if (!stunned && stateMachine.GetActive(StateLayer.Action) == null)
        {
            stateMachine.ChangeState(AttackState);
        }
        blackboard.QueuedSkillSlot = -1;
    }

    // ---- 生命与伤害（统一入口：命中/周期跳伤/环境都走这里，别处不得直改 CurrentHealth）----

    /// <summary>受到伤害（统一入口）：先过入口修正（DamageTaken 乘法链 × 挥剑减伤），扣血钳到 [0, MaxHealth]。
    /// 死亡走占位 Die（冻结实体）；击退/受击硬直等由施加方另行 Apply 效果，本入口只管数值</summary>
    public void TakeDamage(float amount)
    {
        if (dead || amount <= 0f)
        {
            return;
        }

        float final = amount
            * GetStatMultiplier(StatType.DamageTaken)
            * Equipment.SwingDamageTakenMultiplier(Tags.Has((ulong)EntityTag.Swinging));
        blackboard.CurrentHealth = Mathf.Clamp(blackboard.CurrentHealth - final, 0f, MaxHealth);
        if (blackboard.CurrentHealth <= 0f)
        {
            Die();
        }
    }

    /// <summary>死亡（占位处理）：清指令 + 停 Update。正式死亡演出/尸体物理/掉落/复活后置（M5）</summary>
    private void Die()
    {
        dead = true;   // 幂等门：后续伤害直接早退，不会双跑死亡
        // PlayerInput 的 SendMessage 不看目标组件 enabled——死后按键消息仍会写黑板，
        // 清空指令区防"复活瞬间残留的跳/攻击请求"被触发
        blackboard.JumpQueued = false;
        blackboard.QueuedSkillSlot = -1;
        blackboard.SprintActive = false;
        blackboard.MoveDirection = Vector3.zero;
        enabled = false;   // 停整条管线（效果容器/状态机/重力冻结），尸体停在原地
        Debug.Log($"{name} 死亡（占位处理）");
    }
}
