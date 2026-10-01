using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 玩家控制（新版 Input System 图形化流程）。
/// 需要在同一物体上挂 PlayerInput 组件，并做如下设置：
///   Actions     = PlayerControls.inputactions
///   Default Map = Player
///   Behavior    = Send Messages
/// 架构（实体三层）：
/// - Core：通用状态机 + 黑板（Assets/Scripts/Core/）；
/// - Entity：CharacterController 运动能力与共享参数（EntityController，本类基类）；
/// - Player：本类采集输入写黑板（OnMove/OnJump 消息、Sprint 轮询）、组织每帧管线，
///   Idle/Walk/Sprint/Air 状态只读黑板做转换判定和选速度（Assets/Scripts/Player/）。
/// </summary>
/// <summary>加速触发方式，Inspector 里可切换</summary>
public enum EnumSprintInputMode
{
    Tap = 0,        // 点按：按一下切换加速开关，再按一次取消
    Hold,       // 长按：按住期间一直加速，松开恢复
    TapAndHold  // 点按与长按同时生效
}

[RequireComponent(typeof(PlayerInput))]   // CharacterController 的 RequireComponent 在 EntityController 上
public class PlayerController : EntityController
{
    [Header("跳跃")]
    [Tooltip("跳跃能达到的最大高度，单位：米")]
    public float jumpHeight = 1.2f;

    [Header("加速")]
    [Tooltip("加速倍率：加速时的速度 = walkSpeed × 此值，调走路速度后加速自动跟着变")]
    public float sprintMultiplier = 1.6f;
    [Tooltip("加速触发方式：点按=按一下切换开关（默认）；长按=按住加速、松开恢复；点按与长按=两种同时生效")]
    public EnumSprintInputMode sprintMode = EnumSprintInputMode.Tap;
    [Tooltip("「点按与长按」模式下区分两种按法的分界秒数：按下后在此时长内松开算点按，超过算长按")]
    public float sprintTapTime = 0.3f;

    [Header("调试")]
    [Tooltip("是否在屏幕左上角显示实时速度等调试信息")]
    public bool showDebugInfo = true;

    // ---- 状态机与黑板（InitEntity 中构建）----
    private PlayerBlackboard blackboard;
    private StateMachine<PlayerBlackboard> stateMachine;

    public StateMachine<PlayerBlackboard> StateMachine => stateMachine;   // 调试面板读取当前状态用

    // 状态实例：InitEntity 构造一次，转换时互相引用（不 new，零 GC）
    public PlayerIdleState IdleState { get; private set; }
    public PlayerWalkState WalkState { get; private set; }
    public PlayerSprintState SprintState { get; private set; }
    public PlayerAirState AirState { get; private set; }

    // 玩家特有的配置（WalkSpeed 等通用配置在 EntityController 上）
    public float SprintMultiplier => sprintMultiplier;

    private PlayerInput playerInput;        // 用于取 Sprint 动作实例
    private InputAction sprintAction;       // Sprint 动作，Update 里轮询它的按下状态

    private Vector3 lastFramePosition;    // 上一帧位置，用位置差算真实速度
    private Vector3 debugVelocity;        // 本帧实测速度（含碰撞滑动、重力）
    private GUIStyle debugStyle;          // OnGUI 用，延迟创建

    protected override EntityBlackboard CreateBlackboard()
    {
        blackboard = new PlayerBlackboard
        {
            Controller = this,
        };
        return blackboard;
    }

    protected override void InitEntity()
    {
        playerInput = GetComponent<PlayerInput>();
        sprintAction = playerInput.actions.FindAction("Sprint");

        stateMachine = new StateMachine<PlayerBlackboard>();
        IdleState = new PlayerIdleState(blackboard, stateMachine);
        WalkState = new PlayerWalkState(blackboard, stateMachine);
        SprintState = new PlayerSprintState(blackboard, stateMachine);
        AirState = new PlayerAirState(blackboard, stateMachine);
        stateMachine.Initialize(IdleState);   // 初始状态：待机

        lastFramePosition = transform.position;
    }

    // ---- PlayerInput（Send Messages 模式）回调，方法名 = 资源里的动作名 + "On" 前缀 ----

    private void OnMove(InputValue inputValue)
    {
        blackboard.MoveInput = inputValue.Get<Vector2>();
    }

    private void OnJump(InputValue inputValue)
    {
        if (inputValue.isPressed)
        {
            blackboard.JumpQueued = true;
        }
    }

    // Update 帧内顺序（与拆分前逐帧一致，保证手感不变，勿调整）：
    // 采集加速键 → 地面检测(基类) → 移动方向 → 长按判定 → 状态机（水平移动）
    // → 跳跃 → 重力(基类) → 转向(基类) → 调试采样
    void Update()
    {
        UpdateSprintInput();
        UpdateGroundCheck();
        UpdateMoveDirection();
        UpdateSprintHoldPromotion();

        stateMachine.Tick();

        TryConsumeJump();
        ApplyGravityAndVerticalMove();
        ApplyRotation();

        // 调试：用本帧位置差反推真实移动速度（比状态选的速度更可信，能反映碰撞和重力）
        debugVelocity = (transform.position - lastFramePosition) / Time.deltaTime;
        lastFramePosition = transform.position;
    }

    // ---- 每帧数据采集（写黑板）----

    // 加速键的触发方式由 sprintMode 决定（默认点按）：
    // - 点按：按下再松开即切换加速开关
    // - 长按：按住期间一直加速，松开恢复
    // - 点按与长按：按下后在 sprintTapTime 内松开算点按（切换开关），超过算长按（按住加速）
    // 逻辑放在代码里，输入资源只是普通按键绑定，之后做键位重绑时行为自动跟着新按键走。
    // 不用 OnSprint 消息回调而是直接轮询动作值：绕开 Send Messages 的回调时序问题
    private void UpdateSprintInput()
    {
        bool pressed = sprintAction != null && sprintAction.ReadValue<float>() > 0.5f;
        if (pressed == blackboard.SprintPressing)
        {
            return;   // 按下状态没变化
        }

        if (pressed)
        {
            blackboard.SprintPressing = true;
            blackboard.SprintPressTime = Time.time;
        }
        else
        {
            blackboard.SprintPressing = false;
            if (sprintMode != EnumSprintInputMode.Hold)
            {
                // 点按模式：任何一次按下再松开都算点按；
                // 点按与长按模式：分界时长内松开、且没进入过长按状态，才算点按
                bool isTap = sprintMode == EnumSprintInputMode.Tap ||
                             (!blackboard.SprintHoldActive && Time.time - blackboard.SprintPressTime < sprintTapTime);
                if (isTap)
                {
                    blackboard.SprintToggled = !blackboard.SprintToggled;
                }
            }
            blackboard.SprintHoldActive = false;
        }
    }

    // 把输入转换成相对摄像机的前后左右方向
    private void UpdateMoveDirection()
    {
        Vector3 worldMoveDirection;
        if (Camera.main != null)
        {
            Vector3 cameraForward = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
            Vector3 cameraRight = Vector3.ProjectOnPlane(Camera.main.transform.right, Vector3.up).normalized;
            worldMoveDirection = cameraForward * blackboard.MoveInput.y + cameraRight * blackboard.MoveInput.x;
        }
        else
        {
            worldMoveDirection = new Vector3(blackboard.MoveInput.x, 0f, blackboard.MoveInput.y);
        }

        blackboard.MoveDirection = Vector3.ClampMagnitude(worldMoveDirection, 1f);
    }

    // 长按判定：
    // - 长按模式：按下立即加速
    // - 点按与长按模式：按住超过分界时长才加速，给点按留出判定窗口
    private void UpdateSprintHoldPromotion()
    {
        if (blackboard.SprintPressing && !blackboard.SprintHoldActive &&
            (sprintMode == EnumSprintInputMode.Hold ||
             (sprintMode == EnumSprintInputMode.TapAndHold &&
              Time.time - blackboard.SprintPressTime >= sprintTapTime)))
        {
            blackboard.SprintHoldActive = true;
        }
    }

    /// <summary>跳跃：本帧按下过且在地面就施加冲量；随后无条件清空请求（无缓冲，同旧实现）</summary>
    private void TryConsumeJump()
    {
        if (blackboard.IsGrounded && blackboard.JumpQueued)
        {
            blackboard.VerticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        blackboard.JumpQueued = false;
    }

    // ---- 调试面板 ----

    // 屏幕左上角的调试信息（OnGUI 每帧会被调用多次，只做纯显示）
    void OnGUI()
    {
        if (!showDebugInfo)
        {
            return;
        }

        if (debugStyle == null)
        {
            debugStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 16,
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(10, 10, 8, 8),
            };
        }

        float horizontalSpeed = new Vector3(debugVelocity.x, 0f, debugVelocity.z).magnitude;
        string sprintState = blackboard.SprintToggled ? "加速（点按）"
                            : blackboard.SprintHoldActive ? "加速（长按）"
                            : "走路";
        string machineState = stateMachine?.CurrentState?.StateName ?? "-";
        string actionStatus = sprintAction != null ? "已找到" : "未找到（检查资源）";
        string scheme = playerInput != null ? playerInput.currentControlScheme : "-";
        GUI.Label(new Rect(10f, 10f, 300f, 140f),
            $"水平速度 {horizontalSpeed:F2} m/s\n竖直速度 {debugVelocity.y:F2} m/s\n状态 {sprintState}" +
            $"\n状态机 {machineState}\n方案 {scheme}｜Sprint动作 {actionStatus}",
            debugStyle);
    }
}
