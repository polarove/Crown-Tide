using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 玩家控制器：角色层的"玩家输入源"叶子——与 NPC 同构（同一状态机/黑板/运动管线，基类 NpcController），
/// 唯一差异是 UpdateCommands 读输入设备写黑板指令区，而 NPC 叶子（如 EnemyController）
/// 跑决策树写同一块指令区。后续"玩家操作 NPC"= 给实体换输入源（附身路径见 EntityArchitecture.md）。
/// 需要在同一物体上挂 PlayerInput 组件，并做如下设置：
///   Actions     = PlayerControls.inputactions
///   Default Map = Player
///   Behavior    = Send Messages
/// 注意：Update 管线唯一声明在 NpcController——本类不得声明 Update（会遮蔽整条管线）。
/// 加速点按/长按判定（moveInput 与 sprint 四字段）是本输入源的内部状态，不进黑板。
/// 状态机分四层（同层互斥、异层叠加，详见 StateLayer）：Locomotion=水平移动、Aerial=竖直姿态、
/// Action=主动动作、CrowdControl=失控（眩晕，压制其余层）。
/// </summary>
/// <summary>加速触发方式，Inspector 里可切换</summary>
public enum EnumSprintInputMode
{
    Tap = 0,        // 点按：按一下切换加速开关，再按一次取消
    Hold,       // 长按：按住期间一直加速，松开恢复
    TapAndHold  // 点按与长按同时生效
}

[RequireComponent(typeof(PlayerInput))]   // CharacterController/CharacterEquipment 的 RequireComponent 在 EntityController 上
public class PlayerController : NpcController
{
    [Header("加速输入")]
    [Tooltip("加速触发方式：点按=按一下切换开关（默认）；长按=按住加速、松开恢复；点按与长按=两种同时生效")]
    public EnumSprintInputMode sprintMode = EnumSprintInputMode.Tap;
    [Tooltip("「点按与长按」模式下区分两种按法的分界秒数：按下后在此时长内松开算点按，超过算长按")]
    public float sprintTapTime = 0.3f;

    [Header("调试")]
    [Tooltip("是否在屏幕左上角显示实时速度等调试信息")]
    public bool showDebugInfo = true;

    // ---- 输入源内部状态（原黑板字段移入：点按/长按判定只有玩家输入才需要，NPC 不用知道）----
    private Vector2 moveInput;           // OnMove 持续更新（屏幕相对杆量，换算成世界方向后即失效）
    private bool sprintPressing;         // 加速键当前是否被按着
    private float sprintPressTime;       // 本次按下开始的时刻，用于区分点按/长按
    private bool sprintToggled;          // 点按切换出的加速开关，再点按一次取消
    private bool sprintHoldActive;       // 长按期间为 true，松开即恢复

    private PlayerInput playerInput;        // 用于取 Sprint 动作实例
    private InputAction sprintAction;       // Sprint 动作，UpdateCommands 里轮询它的按下状态

    private GUIStyle debugStyle;          // OnGUI 用，延迟创建

    protected override void InitEntity()
    {
        base.InitEntity();   // 建状态机与七状态（与 NPC 同构）
        playerInput = GetComponent<PlayerInput>();
        sprintAction = playerInput.actions.FindAction("Sprint");
    }

    // ---- PlayerInput（Send Messages 模式）回调，方法名 = 资源里的动作名 + "On" 前缀 ----

    private void OnMove(InputValue inputValue)
    {
        moveInput = inputValue.Get<Vector2>();
    }

    private void OnJump(InputValue inputValue)
    {
        if (inputValue.isPressed)
        {
            NpcBoard.JumpQueued = true;
        }
    }

    private void OnAttack(InputValue inputValue)
    {
        // 技能指令：M1 只有槽 0（普攻）；QueuedSkillSlot 由基类 TryConsumeSkill 每帧清空（无缓冲）
        if (inputValue.isPressed)
        {
            NpcBoard.QueuedSkillSlot = 0;
        }
    }

    /// <summary>输入源：采集加速键 → 杆量换算成摄像机相对方向 → 长按判定 → 汇总 SprintActive 指令</summary>
    protected override void UpdateCommands()
    {
        UpdateSprintInput();
        UpdateMoveDirection();
        UpdateSprintHoldPromotion();

        // SprintActive 是电平型指令（基类帧首已重置）：此处写入本帧输入源的最终判定
        NpcBoard.SprintActive = sprintToggled || sprintHoldActive;
    }

    // ---- 每帧数据采集（写黑板指令区）----

    // 加速键的触发方式由 sprintMode 决定（默认点按）：
    // - 点按：按下再松开即切换加速开关
    // - 长按：按住期间一直加速，松开恢复
    // - 点按与长按：按下后在 sprintTapTime 内松开算点按（切换开关），超过算长按（按住加速）
    // 逻辑放在代码里，输入资源只是普通按键绑定，之后做键位重绑时行为自动跟着新按键走。
    // 不用 OnSprint 消息回调而是直接轮询动作值：绕开 Send Messages 的回调时序问题
    private void UpdateSprintInput()
    {
        bool pressed = sprintAction != null && sprintAction.ReadValue<float>() > 0.5f;
        if (pressed == sprintPressing)
        {
            return;   // 按下状态没变化
        }

        if (pressed)
        {
            sprintPressing = true;
            sprintPressTime = Time.time;
        }
        else
        {
            sprintPressing = false;
            if (sprintMode != EnumSprintInputMode.Hold)
            {
                // 点按模式：任何一次按下再松开都算点按；
                // 点按与长按模式：分界时长内松开、且没进入过长按状态，才算点按
                bool isTap = sprintMode == EnumSprintInputMode.Tap ||
                             (!sprintHoldActive && Time.time - sprintPressTime < sprintTapTime);
                if (isTap)
                {
                    sprintToggled = !sprintToggled;
                }
            }
            sprintHoldActive = false;
        }
    }

    // 把输入转换成相对摄像机的前后左右方向（AI 输入源跳过这步：决策树直接给世界方向）
    private void UpdateMoveDirection()
    {
        Vector3 worldMoveDirection;
        if (Camera.main != null)
        {
            Vector3 cameraForward = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
            Vector3 cameraRight = Vector3.ProjectOnPlane(Camera.main.transform.right, Vector3.up).normalized;
            worldMoveDirection = cameraForward * moveInput.y + cameraRight * moveInput.x;
        }
        else
        {
            worldMoveDirection = new Vector3(moveInput.x, 0f, moveInput.y);
        }

        NpcBoard.MoveDirection = Vector3.ClampMagnitude(worldMoveDirection, 1f);
    }

    // 长按判定：
    // - 长按模式：按下立即加速
    // - 点按与长按模式：按住超过分界时长才加速，给点按留出判定窗口
    private void UpdateSprintHoldPromotion()
    {
        if (sprintPressing && !sprintHoldActive &&
            (sprintMode == EnumSprintInputMode.Hold ||
             (sprintMode == EnumSprintInputMode.TapAndHold &&
              Time.time - sprintPressTime >= sprintTapTime)))
        {
            sprintHoldActive = true;
        }
    }

    /// <summary>调试输入：F3 触发眩晕（F1/F2 已被视角切换占用；正式眩晕来自战斗系统的击打效果）。
    /// 设备直读不走输入资源；连按不叠加：同层同实例的 ChangeState 会被状态机忽略</summary>
    protected override void UpdateDebugInput()
    {
        if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
        {
            StateMachine.ChangeState(StunState);
        }
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

        float horizontalSpeed = new Vector3(DebugVelocity.x, 0f, DebugVelocity.z).magnitude;
        string sprintState = sprintToggled ? "加速（点按）"
                            : sprintHoldActive ? "加速（长按）"
                            : "走路";
        // 各活跃层状态名拼接（如"Sprint｜Air｜Attack"），Action/CC 层未激活时不显示
        string machineState =
            $"{StateMachine?.GetActive(StateLayer.Locomotion)?.StateName ?? "-"}" +
            $"｜{StateMachine?.GetActive(StateLayer.Aerial)?.StateName ?? "-"}";
        State<NpcBlackboard> actionState = StateMachine?.GetActive(StateLayer.Action);
        if (actionState != null)
        {
            machineState += $"｜{actionState.StateName}";
        }
        State<NpcBlackboard> ccState = StateMachine?.GetActive(StateLayer.CrowdControl);
        if (ccState != null)
        {
            machineState += $"｜{ccState.StateName}";
        }
        string attackLine = actionState is NpcAttackState attack ? $"\n攻击 {attack.Phase}" : "";
        string actionStatus = sprintAction != null ? "已找到" : "未找到（检查资源）";
        string scheme = playerInput != null ? playerInput.currentControlScheme : "-";
        GUI.Label(new Rect(10f, 10f, 300f, 160f),
            $"水平速度 {horizontalSpeed:F2} m/s\n竖直速度 {DebugVelocity.y:F2} m/s\n状态 {sprintState}" +
            $"\n状态机 {machineState}{attackLine}\n方案 {scheme}｜Sprint动作 {actionStatus}",
            debugStyle);
    }
}
