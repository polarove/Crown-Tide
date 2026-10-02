using UnityEngine;

/// <summary>
/// NPC 控制器：统一角色层（玩家与 NPC 在此同构）。
/// "NPC" 在本项目 = 角色壳：分层状态机（Locomotion/Aerial/Action/CrowdControl，见 StateLayer）
/// + 黑板指令区 + 每帧运动管线。谁在写指令区（输入源）是叶子的唯一差异：
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

    [Header("战斗")]
    [Tooltip("眩晕持续时间（秒）；正式眩晕将来自战斗系统（M2.5 效果容器）")]
    public float stunDuration = 2f;

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

    // 状态要读的参数（WalkSpeed 等通用配置在 EntityController；攻击节奏在 CharacterEquipment）
    public float SprintMultiplier => sprintMultiplier;
    public float StunDuration => stunDuration;

    /// <summary>霸体：任一活跃状态声明 GrantsSuperArmor 即算，命中入口据此仲裁是否施加 CC</summary>
    public override bool HasSuperArmor => stateMachine != null && stateMachine.HasSuperArmor();

    // 调试采样：本帧实测速度（位置差反推，比状态选的速度更可信，能反映碰撞和重力）
    private Vector3 lastFramePosition;
    protected Vector3 DebugVelocity { get; private set; }   // 玩家叶子的调试面板读

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

        lastFramePosition = transform.position;
    }

    /// <summary>输入源挂点：把本帧指令写进黑板指令区（玩家叶子=设备翻译，NPC 叶子=决策树翻译）</summary>
    protected virtual void UpdateCommands() { }

    /// <summary>调试输入挂点（管线末尾）：F3 眩晕等调试触发</summary>
    protected virtual void UpdateDebugInput() { }

    // 帧内顺序（勿调整）：
    // 帧首重置电平型指令 → 输入源写指令 → 地面检测(基类) → 技能消费(进攻击等 Action 状态)
    // → 状态机（水平移动） → 跳跃 → 重力(基类) → 转向(基类) → 调试采样 → 调试输入
    protected virtual void Update()
    {
        // 帧首重置电平型指令：输入源沉默 = 站桩（附身切换帧、叶子未来的早退路径都安全）。
        // 边沿型指令（JumpQueued/QueuedSkillSlot）不在此列——由消息回调/决策在帧间置位，清了会丢输入
        blackboard.SprintActive = false;
        blackboard.MoveDirection = Vector3.zero;

        UpdateCommands();
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
            blackboard.VerticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
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
}
