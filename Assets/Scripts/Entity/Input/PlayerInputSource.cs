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
/// 调试链（F3~F11）随输入源迁移：效果施加/驱散/附身/技能请求，仅设备直读，不走输入资源。
/// </summary>
[RequireComponent(typeof(PlayerInput))]
public sealed class PlayerInputSource : MonoBehaviour, IInputSource
{
    [Header("视角基准")]
    [Tooltip("移动方向的投影基准（本玩家的相机 Transform；空 = CameraRig 自动注入，也可手连）")]
    public Transform? ViewTransform;

    [Tooltip("PlayerInput 未配 Actions 时的兜底资产（附身到未预配的实体用）；空 = 不兜底")]
    public InputActionAsset? FallbackActions;

    [Header("加速输入")]
    [Tooltip("加速触发方式：点按=按一下切换开关（默认）；长按=按住加速、松开恢复；点按与长按=两种同时生效")]
    public EnumSprintInputMode SprintMode = EnumSprintInputMode.Tap;
    [Tooltip("「点按与长按」模式下区分两种按法的分界秒数：按下后在此时长内松开算点按，超过算长按")]
    public float SprintTapTime = 0.3f;

    [Header("调试效果（拖演示 SO：右键 Create → Crown Tide → 修饰效果）")]
    [Tooltip("F3：对自身施加（眩晕演示——失控/打断/自动解除全链路）")]
    public ModifierEffect? DebugStunModifier;
    [Tooltip("F4：对自身施加（急速演示——移速乘数）")]
    public ModifierEffect? DebugHasteModifier;
    [Tooltip("F5：对自身施加（创伤演示——周期跳伤/死亡占位）")]
    public ModifierEffect? DebugWoundModifier;

    // 宿主与输入源内部引用：由 Bootstrap 绑定/懒初始化保证存在（懒取在首次 GatherCommands），
    // 故按"非空不变量"声明——`= null!` 是对编译器的断言，零运行时开销（不是赋 null）
    private Entity Host = null!;                 // 宿主（GatherCommands 缓存一次）
    private PlayerInput PlayerInput = null!;
    private InputAction SprintAction = null!;
    private InputAction AimAction = null!;
    private bool Bound;                  // Brain 绑定标志（未绑定 = 沉默，回调/Gather 双守门）
    private bool WarnedMissingModifiers; // 调试槽忘拖资产的一次性警告标记（防刷屏）

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
            PlayerInput.actions = InputSystem.actions;
        }
        if (PlayerInput.actions != null)
        {
            SprintAction = PlayerInput.actions.FindAction("Sprint");
            AimAction = PlayerInput.actions.FindAction("Aim");
        }
    }

    /// <summary>Brain 绑定/解绑（BindInputSource 调用）：激活 = 开 PlayerInput；解绑 = 全关</summary>
    public void SetActive(bool active)
    {
        EnsurePlayerInput();
        if (active && PlayerInput.actions == null && FallbackActions != null)
        {
            PlayerInput.actions = FallbackActions;   // 附身兜底：目标实体没预配 Actions 时补上
            SprintAction = FallbackActions.FindAction("Sprint");
            AimAction = FallbackActions.FindAction("Aim");
        }
        Bound = active;
        PlayerInput.enabled = active;
    }

    /// <summary>视角基准注入（CameraRig 在字段为空时调用；多人 = spawn 系统指派，本轮自动连）</summary>
    public void SetViewTransform(Transform view)
    {
        ViewTransform ??= view;
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
        if (!Bound)
        {
            return;
        }
        moveInput = inputValue.Get<Vector2>();
    }

    private void OnJump(InputValue inputValue)
    {
        if (!Bound || !inputValue.isPressed)
        {
            return;
        }
        EnsureEntity().Commands.JumpQueued = true;
    }

    private void OnAttack(InputValue inputValue)
    {
        if (!Bound || !inputValue.isPressed)
        {
            return;
        }
        // 攻击请求不在这里翻译（瞄准中是不是射击由 Brain 按 Data 决定）——只置边沿
        EnsureEntity().Commands.AttackQueued = true;
    }

    /// <summary>每帧采集：加速键 → 杆量投影成视角相对方向 → 长按判定 → 瞄准电平 → 汇总写缓冲</summary>
    public void GatherCommands(CommandBuffer commands)
    {
        if (!Bound)
        {
            return;
        }
        Entity host = EnsureEntity();
        if (host == null)
        {
            return;
        }

        UpdateSprintInput(Time.deltaTime);
        UpdateMoveDirection(commands);
        UpdateSprintHoldPromotion(Time.deltaTime);
        UpdateDebugInput(host);

        // 电平型指令：本帧输入源的最终判定（帧首已重置）
        commands.SprintActive = SprintToggled || SprintHoldActive;
        commands.AimActive = AimAction != null && AimAction.ReadValue<float>() > 0.5f;
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

    /// <summary>调试输入：F3/F4/F5 对自身施加调试效果、F6 全驱散、F7 对场景内其他实体施加眩晕、
    /// F9 请求冠冕技能（信心钟摆涨向）、F10 附身切换（IsPlayerControlled 翻转）、
    /// F11 请求潮汐技能（信心钟摆降向）。
    /// F1/F2 已被视角切换占用；正式效果来自战斗系统的命中入口；设备直读不走输入资源</summary>
    private void UpdateDebugInput(Entity host)
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.f3Key.wasPressedThisFrame)
        {
            ApplyDebugModifier(host, DebugStunModifier, "F3 眩晕");
        }
        if (keyboard.f4Key.wasPressedThisFrame)
        {
            ApplyDebugModifier(host, DebugHasteModifier, "F4 急速");
        }
        if (keyboard.f5Key.wasPressedThisFrame)
        {
            ApplyDebugModifier(host, DebugWoundModifier, "F5 创伤");
        }
        if (keyboard.f6Key.wasPressedThisFrame)
        {
            host.Brain.Modifiers.Dispel(EnumModifierCategory.All);
        }
        if (keyboard.f7Key.wasPressedThisFrame && DebugStunModifier != null)
        {
            // 敌人侧回归：场景内其他实体走同一套列表/投影（FindObjectsByType 只在调试分支跑，不上玩法路径）
            foreach (Entity other in FindObjectsByType<Entity>())
            {
                if (other != host)
                {
                    other.Brain.Modifiers.Apply(DebugStunModifier);
                }
            }
        }
        if (keyboard.f9Key.wasPressedThisFrame)
        {
            // 技能链路冒烟：请求冠冕技能（Brain 消费：CanAct + 双闸门校验 + 写回冷却/信心增量）
            host.Commands.SkillSlotQueued = (int)EnumSkillType.Crown;
        }
        if (keyboard.f11Key.wasPressedThisFrame)
        {
            // 信心钟摆降向：请求潮汐技能（faithDelta<0 降信心；贴边 -67 后 CanApply 锁向拒绝）
            host.Commands.SkillSlotQueued = (int)EnumSkillType.Tide;
        }
        if (keyboard.f10Key.wasPressedThisFrame)
        {
            // 附身切换演示：本实体换脑（玩家→AI / AI→玩家），验证 IsPlayerControlled 的唯一职责
            host.IsPlayerControlled = !host.IsPlayerControlled;
            Debug.Log($"{host.name}：IsPlayerControlled → {host.IsPlayerControlled}");
        }
    }

    /// <summary>调试施加：空槽一次性警告后跳过（列表对 null 也早退，这里负责把缺配置讲清楚）</summary>
    private void ApplyDebugModifier(Entity host, ModifierEffect? modifier, string keyName)
    {
        if (modifier == null)
        {
            if (!WarnedMissingModifiers)
            {
                WarnedMissingModifiers = true;
                Debug.LogWarning($"{keyName}：调试槽未拖 ModifierEffect 资产（此警告只提示一次）");
            }
            return;
        }
        host.Brain.Modifiers.Apply(modifier);
    }
}
