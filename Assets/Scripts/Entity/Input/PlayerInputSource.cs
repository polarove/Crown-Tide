using Assets.Scripts.Entity.Data.Skill;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>加速触发方式，Inspector 里可切换（旧 PlayerController 平移）</summary>
public enum EnumSprintInputMode
{
    Tap = 0,        // 点按：按一下切换加速开关，再按一次取消
    Hold,           // 长按：按住期间一直加速，松开恢复
    TapAndHold      // 点按与长按同时生效
}

/// <summary>
/// 玩家输入源（Input 层）：读输入设备，翻译成指令写 CommandBuffer——回答"Entity 要做什么"。
/// 与 AITreeInputSource 写的是同一个缓冲（指令层汇流），Brain/状态机不关心指令来自谁。
/// 需要同物体挂 PlayerInput 组件（Actions = PlayerControls.inputactions，Default Map = Player，
/// Behavior = Send Messages）。SendMessage 不看组件 enabled——SetActive(false) 时回调经 Bound
/// 标志早退（双保险：PlayerInput.enabled 也关，未激活的实体不被同一键盘驱动）。
/// 移动方向投影读 ViewTransform（视角基准，CameraRig 注入/Inspector 手连）而非 Camera.main——
/// 多人纪律：每玩家自己的相机，不全局找。
/// 加速点按/长压判定（四字段）是本输入源的内部状态，不进指令缓冲。
/// 武器技能走正式 WeaponSkill 动作（默认 V / LB，可改绑）；Debug 独立。
/// </summary>
[RequireComponent(typeof(PlayerInput))]
public sealed partial class PlayerInputSource : MonoBehaviour, IInputSource
{
    [Header("视角基准")]
    [Tooltip("移动投影和攻击水平朝向的视角基准（本玩家相机；空 = CameraRig 自动注入，也可手连）")]
    public Transform? ViewTransform;

    [Tooltip("PlayerInput 未配 Actions 时的兜底资产；空 = 不兜底")]
    public InputActionAsset? FallbackActions;

    [Header("加速输入")]
    [Tooltip("加速触发方式：点按=按一下切换开关（默认）；长按=按住加速、松开恢复；点按与长按=两种同时生效")]
    public EnumSprintInputMode SprintMode = EnumSprintInputMode.Tap;
    [Tooltip("「点按与长按」模式下区分两种按法的分界秒数：按下后在此时长内松开算点按，超过算长按")]
    public float SprintTapTime = 0.3f;

    // 宿主与输入源内部引用：由 Bootstrap 绑定/懒初始化保证存在（懒取在首次 GatherCommands），
    // 故按"非空不变量"声明——`= null!` 是对编译器的断言，零运行时开销（不是赋 null）
    private Entity Host = null!;                 // 宿主（GatherCommands 缓存一次）
    private PlayerInput PlayerInput = null!;
    private InputAction SprintAction = null!;
    private InputAction? MoveAction;
    private InputActionAsset? OwnedActions;
    private bool Bound;                  // Brain 绑定标志（未绑定 = 沉默，回调/Gather 双守门）
    private bool WarnedMissingPlayerInput;   // 缺 PlayerInput 组件的一次性警告标记（防刷屏）
    private InputDevice[]? PreviousDevices;
    private string? PreviousControlScheme;

    // ---- 输入源内部状态（点按/长按判定只有玩家输入才需要，AI 不用知道）----
    private Vector2 moveInput;           // OnMove 持续更新（屏幕相对杆量，投影成世界方向后即失效）
    private bool SprintPressing;         // 加速键当前是否被按着
    private float SprintPressTimer;      // 本次按下开始的计时（累积器，网络时间纪律）
    private bool SprintToggled;          // 点按切换出的加速开关，再点按一次取消
    private bool SprintHoldActive;       // 长按期间为 true，松开即恢复

    /// <summary>懒初始化（零 Awake 规则：本组件被 Entity.Bootstrap → BindInputSource → SetActive
    /// 首次触碰，届时才取 PlayerInput——避开同物体多组件 Awake 顺序未定义的竞态；只跑一次）</summary>
    private void EnsurePlayerInput()
    {
        if (PlayerInput != null)
        {
            return;
        }
        PlayerInput = GetComponent<PlayerInput>();
        // 缺省对齐工程的 Input System Actions 资产（InputSystem.actions）：Play 模式/测试夹具
        // 动态挂上的 PlayerInput 不会自动继承 PlayerInput 的项目级缺省，Action 引用会全空
        if (PlayerInput.actions == null)
        {
            InputActionAsset? template = InputSystem.actions != null ? InputSystem.actions : FallbackActions;
            if (template != null) PlayerInput.actions = template;
        }
        if (PlayerInput.actions != null)
        {
            OwnedActions = Instantiate(PlayerInput.actions);
            PlayerInput.actions = OwnedActions;
            if (PlayerInput.actions != OwnedActions)
            {
                Destroy(OwnedActions);
                OwnedActions = PlayerInput.actions;
            }
            GameSettingsController.RegisterInput(OwnedActions);
            MoveAction = PlayerInput.actions.FindAction("Move");
            SprintAction = PlayerInput.actions.FindAction("Sprint");
        }
    }

    /// <summary>Brain 绑定/解绑（BindInputSource 调用）：激活 = 开 PlayerInput；解绑 = 全关。
    /// PlayerInput 缺失（RequireComponent 被绕过/测试夹具手工搭）时警告一次并保持沉默，不 NRE</summary>
    public void Activate()
    {
        EnsurePlayerInput();
        if (PlayerInput == null)
        {
            WarnIfMissingPlayerInput();
            return;
        }
        if (PlayerInput.actions == null && FallbackActions != null)
        {
            OwnedActions = Instantiate(FallbackActions);
            PlayerInput.actions = OwnedActions;
            GameSettingsController.RegisterInput(OwnedActions);
            MoveAction = OwnedActions.FindAction("Move");
            SprintAction = OwnedActions.FindAction("Sprint");
        }
        Bound = true;
        PlayerInput.enabled = true;
        // 重新启用会重新自动配对；再次激活必须恢复该输入源离开前的设备。
        if (PlayerInput.isActiveAndEnabled && PlayerInput.user.valid
            && PreviousDevices != null && PreviousDevices.Length > 0 && PreviousControlScheme != null
            && System.Array.TrueForAll(PreviousDevices, device => device.added))
        {
            PlayerInput.SwitchCurrentControlScheme(PreviousControlScheme, PreviousDevices);
        }
    }

    public void Deactivate()
    {
        Bound = false;
        moveInput = Vector2.zero;
        SprintPressing = SprintToggled = SprintHoldActive = false;
        SprintPressTimer = 0f;
        // 与 Activate 对称地补懒初始化：反激活可能发生在首次激活之前
        // （Bootstrap 绑 AI 源时就会 Deactivate 本组件），不补则 PlayerInput 仍是 null → NRE
        EnsurePlayerInput();
        if (PlayerInput == null)
        {
            WarnIfMissingPlayerInput();
            return;
        }
        if (PlayerInput.enabled && PlayerInput.devices.Count > 0)
        {
            PreviousDevices = PlayerInput.devices.ToArray();
            PreviousControlScheme = PlayerInput.currentControlScheme;
        }
        PlayerInput.enabled = false;
    }

    /// <summary>逻辑交接请求：继承操作者的设备归属，视角基准仍用目标自己的相机。</summary>
    public void InheritDevices(PlayerInputSource source)
    {
        source.EnsurePlayerInput();
        if (source.PlayerInput != null && source.PlayerInput.enabled && source.PlayerInput.devices.Count > 0)
        {
            PreviousDevices = source.PlayerInput.devices.ToArray();
            PreviousControlScheme = source.PlayerInput.currentControlScheme;
        }
        else
        {
            PreviousDevices = source.PreviousDevices;
            PreviousControlScheme = source.PreviousControlScheme;
        }
    }

    /// <summary>PlayerInput 缺失的一次性警告（防刷屏；懒初始化只看一次，这里只负责讲清原因）</summary>
    private void WarnIfMissingPlayerInput()
    {
        if (WarnedMissingPlayerInput)
        {
            return;
        }
        WarnedMissingPlayerInput = true;
        Debug.LogWarning($"{name}：缺 PlayerInput 组件，玩家输入源无法工作（站桩）。此警告只提示一次", this);
    }

    /// <summary>视角基准注入（CameraRig 在字段为空时调用；多人 = spawn 系统指派，本轮自动连）。
    /// 只填空缺、不覆盖手连</summary>
    public void SetViewTransform(Transform view)
    {
        ViewTransform = ViewTransform != null ? ViewTransform : view;
    }

    /// <summary>宿主注入（Entity.Bootstrap 后由调试链/GatherCommands 使用；Brain 侧不调本组件，
    /// 宿主在首次 GatherCommands 懒取）</summary>
    private Entity EnsureEntity()
    {
        if (Host == null)
        {
            Host = GetComponentInParent<Entity>();
        }
        return Host;
    }

    // ---- PlayerInput（Send Messages 模式）回调，方法名 = 资源里的动作名 + "On" 前缀 ----

    private void OnMove(InputValue inputValue)
    {
        if (!Bound || GameSettingsController.IsGameplayInputBlocked)
        {
            return;
        }
        moveInput = inputValue.Get<Vector2>();
    }

    private void OnJump(InputValue inputValue)
    {
        if (!Bound || GameSettingsController.IsGameplayInputBlocked || !inputValue.isPressed)
        {
            return;
        }
        EnsureEntity().Commands.JumpQueued = true;
    }

    private void OnAttack(InputValue inputValue)
    {
        if (!Bound || GameSettingsController.IsGameplayInputBlocked || !inputValue.isPressed)
        {
            return;
        }
        // 仅提交攻击请求，资格与连段由逻辑处理
        EnsureEntity().Commands.AttackQueued = true;
    }

    private void OnCrownSkill(InputValue inputValue)
    {
        if (Bound && !GameSettingsController.IsGameplayInputBlocked && inputValue.isPressed)
            EnsureEntity().Commands.SkillSlotQueued = (int)EnumSkillType.Crown;
    }

    private void OnTideSkill(InputValue inputValue)
    {
        if (Bound && !GameSettingsController.IsGameplayInputBlocked && inputValue.isPressed)
            EnsureEntity().Commands.SkillSlotQueued = (int)EnumSkillType.Tide;
    }

    private void OnWeaponSkill(InputValue inputValue)
    {
        if (Bound && !GameSettingsController.IsGameplayInputBlocked && inputValue.isPressed)
            EnsureEntity().Commands.SkillSlotQueued = (int)EnumSkillType.Weapon;
    }

    /// <summary>每帧采集：加速键 → 杆量投影成视角相对方向 → 长按判定 → 汇总写缓冲</summary>
    public void GatherCommands(CommandBuffer commands)
    {
        if (!Bound || GameSettingsController.IsGameplayInputBlocked)
        {
            return;
        }
        Entity host = EnsureEntity();
        if (host == null)
        {
            return;
        }

        if (MoveAction != null) moveInput = MoveAction.ReadValue<Vector2>();
        UpdateSprintInput(Time.deltaTime);
        UpdateMoveDirection(commands);
        Vector3 look = ViewTransform != null ? ViewTransform.forward : host.transform.forward;
        look.y = 0f;
        // 正对天空／脚下时保留视角的水平朝向，避免零向量退回后退方向。
        if (look.sqrMagnitude < 0.000001f && ViewTransform != null)
            look = Vector3.Cross(ViewTransform.right, Vector3.up);
        commands.LookDirection = look.normalized;
        UpdateSprintHoldPromotion(Time.deltaTime);

        // 电平型指令：本帧输入源的最终判定（帧首已重置）
        commands.SprintActive = SprintToggled || SprintHoldActive;
    }

    public void ResetPausedInput()
    {
        moveInput = Vector2.zero;
        SprintPressing = SprintToggled = SprintHoldActive = false;
        SprintPressTimer = 0f;
    }

    private void OnDestroy()
    {
        if (OwnedActions == null) return;
        GameSettingsController.UnregisterInput(OwnedActions);
        Destroy(OwnedActions);
    }

    // ---- 每帧数据采集 ----

    // 加速键的触发方式由 SprintMode 决定（逻辑在代码里，输入资源只是普通按键绑定，
    // 之后做键位重绑时行为自动跟着新按键走。不-OnSprint-回调而轮询动作值：绕开 Send Messages 时序问题）
    private void UpdateSprintInput(float deltaTime)
    {
        bool pressed = SprintAction != null && SprintAction.ReadValue<float>() > 0.5f;
        if (pressed == SprintPressing)
        {
            if (SprintPressing)
            {
                SprintPressTimer += deltaTime;   // 按住期间累积（判定长按用）
            }
            return;   // 按下状态没变化
        }

        if (pressed)
        {
            SprintPressing = true;
            SprintPressTimer = 0f;
        }
        else
        {
            SprintPressing = false;
            if (SprintMode != EnumSprintInputMode.Hold)
            {
                // 点按模式：任何一次按下再松开都算点按；
                // 点按与长按模式：分界时长内松开、且没进入过长按状态，才算点按
                bool isTap = SprintMode == EnumSprintInputMode.Tap ||
                             (!SprintHoldActive && SprintPressTimer < SprintTapTime);
                if (isTap)
                {
                    SprintToggled = !SprintToggled;
                }
            }
            SprintHoldActive = false;
        }
    }

    // 把输入投影成视角相对方向（AI 输入源跳过这步：决策树直接给世界方向）
    private void UpdateMoveDirection(CommandBuffer commands)
    {
        Vector3 worldMoveDirection;
        if (ViewTransform != null)
        {
            Vector3 viewForward = Vector3.ProjectOnPlane(ViewTransform.forward, Vector3.up).normalized;
            Vector3 viewRight = Vector3.ProjectOnPlane(ViewTransform.right, Vector3.up).normalized;
            worldMoveDirection = viewForward * moveInput.y + viewRight * moveInput.x;
        }
        else
        {
            // 无视角基准（相机未注入）：退化为世界轴向（z 前正），仅保不炸不飘
            worldMoveDirection = new Vector3(moveInput.x, 0f, moveInput.y);
        }

        commands.MoveDirection = Vector3.ClampMagnitude(worldMoveDirection, 1f);
    }

    // 长按判定：
    // - 长按模式：按下立即加速；
    // - 点按与长按模式：按住超过分界时长才加速，给点按留出判定窗口
    private void UpdateSprintHoldPromotion(float deltaTime)
    {
        if (SprintPressing && !SprintHoldActive &&
            (SprintMode == EnumSprintInputMode.Hold ||
             (SprintMode == EnumSprintInputMode.TapAndHold && SprintPressTimer >= SprintTapTime)))
        {
            SprintHoldActive = true;
        }
    }



}
