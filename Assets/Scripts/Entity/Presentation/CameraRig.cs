using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 相机装置（Presentation 层）：跟随实体的表现相机，只读 Entity 状态（不修改数据——
/// 需求钦定：相机行为做到角色系统的 Presentation Layer）。
/// 旧 CameraFollow 平移 + 三项角色系统呈现：
/// - 抬头 90°（俯仰上限可配，默认正对天空）+ 往上看时地面保护收缩（视角跟手不停转）；
/// - 低头相机靠近（新增）：俯仰向下超过阈值，相机沿视线渐缩贴向角色（看脚下不穿模不远景）；
/// - 奔跑加速（新增）：Entity.IsSprinting（Logic 只读）→ FOV 平滑拉大，速度感呈现。
/// 结构：挂在相机物体上、持 Entity 引用——相机属于实体（玩家/敌人各有一台，不全局找——
/// 多人纪律：每玩家自己的相机，spawn 系统指派）；LateUpdate 独立节拍（表现层不参与仿真顺序）。
/// 附身切换不动相机：只读感知 Brain.InputSource is PlayerInputSource（控制状态唯一真相），
/// 自己的实体被玩家驱动才亮（Camera/AudioListener 同开同关）——控制权转移后旧相机自动熄灭、
/// 接管者的相机自动亮起，切换的是"谁的相机在看"。
/// 视角基准注入：把自身 Transform 填给宿主 Entity 的 PlayerInputSource.viewTransform
/// （移动方向的投影基准；只填空缺，不覆盖手连）。
/// 相机输入自持（本玩家的显示设备输入，本地直读合理）。
/// </summary>
[RequireComponent(typeof(Camera))]
public sealed class CameraRig : MonoBehaviour
{
    [Header("目标")]
    [Tooltip("要跟随的实体（本相机的归属者）；空 = 本组件不工作")]
    public Entity? FollowEntity;

    [Header("第三人称")]
    [Tooltip("第三人称注视点在实体（胶囊中心）上方的高度，单位：米")]
    public float ThirdPersonPivotHeight = 1.5f;
    [Tooltip("第三人称相机与实体的距离，单位：米")]
    public float ThirdPersonCameraDistance = 8f;
    [Tooltip("左右肩（过肩视角）的侧向偏移距离，单位：米")]
    public float ShoulderOffsetDistance = 1.5f;
    [Tooltip("左右肩切换的平滑速度，越大切换越快")]
    public float ShoulderSwitchSmoothSpeed = 10f;
    [Tooltip("当前肩侧：1 = 右肩，-1 = 左肩（运行时会按输入自动改）")]
    public int CurrentShoulderSide = 1;

    [Header("第一人称")]
    [Tooltip("第一人称眼睛在胶囊中心上方的高度（胶囊高 2、脚到中心约 1.0，此值 0.6 即正常眼高）")]
    public float FirstPersonEyeHeight = 0.6f;
    [Tooltip("第一/第三人称切换的平滑速度，越大过渡越快")]
    public float ViewModeSwitchSmoothSpeed = 8f;

    [Header("低头靠近（角色系统呈现）")]
    [Tooltip("低头相机靠近的开始角度（度，俯仰向下为正）：低头超过它，相机开始沿视线收缩")]
    public float LookDownPullStartAngle = 35f;
    [Tooltip("低头到正下方（90°）时相机距离收缩到的最小比例（0.25 = 收到基础距离的四分之一）")]
    [Range(0.05f, 1f)]
    public float LookDownPullMinFactor = 0.25f;
    [Tooltip("低头收缩的平滑速度")]
    public float LookDownPullSmoothSpeed = 6f;

    [Header("奔跑加速（角色系统呈现）")]
    [Tooltip("奔跑时 FOV 增量（度）：冲刺中视野拉大，速度感")]
    public float SprintFovBoost = 12f;
    [Tooltip("FOV 过渡平滑速度")]
    public float SprintFovSmoothSpeed = 5f;

    [Header("旋转")]
    [Tooltip("输入动作资源，拖入 Assets/Input/PlayerControls.inputactions")]
    public InputActionAsset? InputActionAsset;
    [Tooltip("手柄右摇杆旋转视角的速度，单位：度/秒（推满摇杆时每秒转的角度）")]
    public float GamepadLookRotationSpeed = 150f;
    [Tooltip("鼠标灵敏度：鼠标每移动 1 像素旋转的角度，单位：度/像素")]
    public float MouseLookSensitivity = 0.3f;
    [Tooltip("往上看的最大角度（度），90 = 正对天空")]
    public float LookUpAngleLimit = 90f;
    [Tooltip("往下看的最大角度（度），90 = 正对脚下")]
    public float LookDownAngleLimit = 90f;

    [Header("地面保护")]
    [Tooltip("往上看时，相机与实体所站立平面保持的最小高度，防止相机穿到地面以下")]
    public float MinimumCameraHeightAboveGround = 0.2f;
    [Tooltip("地面保护收缩相机时，相机与实体保持的最小距离，防止贴脸穿模")]
    public float MinimumCameraDistance = 2f;
    [Tooltip("从实体向下搜索站立平面的最大距离，没找到则不限制")]
    public float GroundSearchDistance = 10f;
    [Tooltip("哪些层参与站立平面检测，默认所有层")]
    public LayerMask GroundMask = ~0;

    [Tooltip("游玩时锁定并隐藏鼠标指针，防止指针碰到屏幕边缘后转不动")]
    public bool LockAndHideCursor = true;

    // Awake 里 FindAction 注入；空 = 资产没配齐（OnEnable 会 NRE，属配置错误早暴露）
    private InputAction GamepadLookAction = null!;          // 右摇杆（速率语义）
    private InputAction MouseLookAction = null!;            // 鼠标移动（位移语义）
    private InputAction SwitchShoulderAction = null!;
    private InputAction ToggleViewModeAction = null!;
    private InputActionAsset LocalInputActions = null!;

    private float YawRotationAngle = 0f;            // 水平旋转角（绕 Y 轴），不受限制
    private float PitchRotationAngle = 20f;         // 俯仰角，正值向下看，负值向上看
    private float ShoulderSideBlendFactor = 1f;     // 左右肩的平滑插值因子
    private float LookDownPullFactor = 1f;          // 低头收缩因子（1 = 不收缩）
    private float BaseFieldOfView;                  // 初始 FOV（奔跑加速的基准）
    private bool IsFirstPersonMode;
    private float FirstPersonBlendFactor;           // 0 = 第三人称，1 = 第一人称
    private Renderer[] EntityMeshRenderers = null!;   // 第一人称显隐（首次切换时 ??= 取，表现层自持对象，只读数据不改）
    private Camera CameraComponent = null!;         // Awake 缓存（RequireComponent 保证存在；亮灭开关）
    private AudioListener? ListenerComponent;       // Awake 缓存（可空：相机物体不一定挂；与 Camera 同开同关）

    private void Awake()
    {
        // 相机是表现层独立装置（不在 Entity 组件族内），自取输入动作不参与实体的单 Awake 规则
        if (InputActionAsset == null)
        {
            // 配置事故早暴露：没拖输入资源则整条相机旋转/切换链路都不可用，禁用自身并点明原因
            Debug.LogError($"{name}：CameraRig.InputActionAsset 未配置，相机旋转与视角切换不可用（已禁用本组件）", this);
            enabled = false;
            return;
        }

        // PlayerInput 附身解绑时会关闭自己的动作表。相机只暂停渲染，不会再次 OnEnable，
        // 因此必须持有独立动作实例，不能让角色输入开关影响视角输入的生命周期。
        LocalInputActions = Instantiate(InputActionAsset);
        LocalInputActions.bindingMask = null;
        GamepadLookAction = LocalInputActions.FindAction("Look", throwIfNotFound: true);
        MouseLookAction = LocalInputActions.FindAction("MouseLook", throwIfNotFound: true);
        SwitchShoulderAction = LocalInputActions.FindAction("SwitchShoulder", throwIfNotFound: true);
        ToggleViewModeAction = LocalInputActions.FindAction("ToggleView", throwIfNotFound: true);
        SwitchShoulderAction.performed += OnSwitchShoulder;
        CameraComponent = GetComponent<Camera>();   // RequireComponent 保证非空
        BaseFieldOfView = CameraComponent.fieldOfView;
        ListenerComponent = GetComponent<AudioListener>();
    }

    private void OnDestroy()
    {
        if (SwitchShoulderAction != null)
        {
            SwitchShoulderAction.performed -= OnSwitchShoulder;
        }
        if (LocalInputActions != null)
        {
            LocalInputActions.Disable();
            Destroy(LocalInputActions);
        }
    }

    private void OnEnable()
    {
        if (LocalInputActions == null) return;
        GamepadLookAction.Enable();
        MouseLookAction.Enable();
        SwitchShoulderAction.Enable();
        ToggleViewModeAction.Enable();

        if (LockAndHideCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void OnDisable()
    {
        if (LocalInputActions == null) return;
        GamepadLookAction.Disable();
        MouseLookAction.Disable();
        SwitchShoulderAction.Disable();
        ToggleViewModeAction.Disable();

        if (LockAndHideCursor)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    // 十字键左 = 左肩，十字键右 = 右肩，其他绑定（键盘 F1）= 左右切换
    private void OnSwitchShoulder(InputAction.CallbackContext context)
    {
        if (FollowEntity == null || !FollowEntity.Brain.HasPlayerView || !CameraComponent.enabled)
        {
            return;
        }
        string controlPath = context.control.path;
        if (controlPath.EndsWith("/dpad/left"))
        {
            CurrentShoulderSide = -1;
        }
        else if (controlPath.EndsWith("/dpad/right"))
        {
            CurrentShoulderSide = 1;
        }
        else
        {
            CurrentShoulderSide = -CurrentShoulderSide;
        }
    }

    /// <summary>相机激活同步（附身感知的执行端）：Camera/AudioListener 同开同关——
    /// 同一时刻只有玩家驱动实体的相机在渲染（多台同亮会叠画面 + 双 AudioListener 警告）。
    /// 本组件自身永不禁用：熄灭的相机还要每帧感知"控制权回来了没"</summary>
    private void SyncCameraActive(bool active)
    {
        if (CameraComponent.enabled != active)
        {
            CameraComponent.enabled = active;
        }
        if (ListenerComponent != null && ListenerComponent.enabled != active)
        {
            ListenerComponent.enabled = active;
        }
    }

    /// <summary>还原第一人称隐藏的实体网格（相机熄灭时调用：别的相机看过来不该是隐形人；
    /// 本相机再亮时按视角模式重新隐藏）——表现层自持状态，不动实体数据</summary>
    private void RestoreEntityMeshRenderers()
    {
        if (EntityMeshRenderers == null)
        {
            return;
        }
        for (int i = 0; i < EntityMeshRenderers.Length; i++)
        {
            EntityMeshRenderers[i].enabled = true;
        }
        EntityMeshRenderers = null!;
    }

    private void LateUpdate()
    {
        if (FollowEntity == null)
        {
            return;
        }

        // 视角基准注入（一次性；只填空缺不覆盖手连——多人下 spawn 系统指派各自的相机）
        PlayerInputSource inputSource = FollowEntity.GetComponent<PlayerInputSource>();
        if (inputSource != null)
        {
            inputSource.SetViewTransform(transform);
        }

        // 相机激活感知（只读纪律，零事件耦合）：相机属于实体——仅当自己的实体正被玩家驱动
        // （Brain.InputSource is PlayerInputSource，控制状态唯一真相）时才亮。
        // V / LB 控制权转移后旧相机自动熄灭、接管者的相机自动亮起——切换的是"谁的相机在看"，
        // 相机本身不动、不换跟随目标。多人接缝：分屏下多个玩家相机并亮即是分屏，
        // 将来按"驱动本实体的是本玩家吗"细化（各自设备配对/网络中继）
        // 原角色死亡收尾保留视角，但 Brain 不再绑定玩家操作。
        bool playerDriven = FollowEntity.Brain.HasPlayerView;
        SyncCameraActive(playerDriven);
        if (!playerDriven)
        {
            RestoreEntityMeshRenderers();
            return;   // 熄灭的相机：不锁鼠标、不读视角输入（再亮时视角不跳）、不更新跟随
        }
        Transform followTarget = FollowEntity.transform;

        // 仅读取所属实体已配对的设备，保留各玩家设备隔离；死亡视角保留最后的设备范围。
        PlayerInput? playerInput = FollowEntity.GetComponent<PlayerInput>();
        if (playerInput != null && playerInput.devices.Count > 0)
        {
            LocalInputActions.devices = playerInput.devices;
        }

        // 编辑器里按 Esc 会解锁指针；窗口重新获得焦点时恢复锁定
        if (LockAndHideCursor && Cursor.lockState != CursorLockMode.Locked && Application.isFocused)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // 按下右摇杆（R3）/ 键盘 F2：切换第一/第三人称
        if (ToggleViewModeAction.WasPressedThisFrame())
        {
            IsFirstPersonMode = !IsFirstPersonMode;

            // 第一人称时隐藏角色模型（表现层自持的显隐，防挡视线）
            EntityMeshRenderers ??= followTarget.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < EntityMeshRenderers.Length; i++)
            {
                EntityMeshRenderers[i].enabled = !IsFirstPersonMode;
            }
        }

        // 各项平滑过渡
        float deltaTime = Time.deltaTime;
        ShoulderSideBlendFactor = Mathf.Lerp(ShoulderSideBlendFactor, CurrentShoulderSide, ShoulderSwitchSmoothSpeed * deltaTime);
        FirstPersonBlendFactor = Mathf.Lerp(FirstPersonBlendFactor, IsFirstPersonMode ? 1f : 0f, ViewModeSwitchSmoothSpeed * deltaTime);

        // 过渡到第一人称：注视点高度降到眼睛，距离和肩偏移收缩为 0
        float currentPivotHeight = Mathf.Lerp(ThirdPersonPivotHeight, FirstPersonEyeHeight, FirstPersonBlendFactor);
        float currentCameraDistance = ThirdPersonCameraDistance * (1f - FirstPersonBlendFactor);
        float currentShoulderLateralOffset = ShoulderOffsetDistance * ShoulderSideBlendFactor * (1f - FirstPersonBlendFactor);

        // 右摇杆：速率语义，按住幅度 × 速度 × 时间
        Vector2 gamepadLookInput = GamepadLookAction.ReadValue<Vector2>();
        YawRotationAngle += gamepadLookInput.x * GamepadLookRotationSpeed * deltaTime;
        PitchRotationAngle -= gamepadLookInput.y * GamepadLookRotationSpeed * deltaTime;

        // 鼠标：位移语义，本帧像素位移 × 灵敏度（不乘 deltaTime，与帧率无关）
        Vector2 mouseLookInput = MouseLookAction.ReadValue<Vector2>();
        YawRotationAngle += mouseLookInput.x * MouseLookSensitivity;
        PitchRotationAngle -= mouseLookInput.y * MouseLookSensitivity;

        // 俯仰角：只按用户设置限制（默认 ±90°），地面保护/低头收缩不卡视角
        PitchRotationAngle = Mathf.Clamp(PitchRotationAngle, -LookUpAngleLimit, LookDownAngleLimit);

        // 低头相机靠近（角色系统呈现）：低头超过起始角，收缩因子从 1 渐降到最小比例
        // （俯仰向下为正；第一人称时距离已是 0，不参与）
        float pitchDown = Mathf.Max(0f, PitchRotationAngle);
        float targetPullFactor = pitchDown <= LookDownPullStartAngle
            ? 1f
            : Mathf.Lerp(1f, LookDownPullMinFactor,
                Mathf.InverseLerp(LookDownPullStartAngle, LookDownAngleLimit, pitchDown));
        LookDownPullFactor = Mathf.Lerp(LookDownPullFactor, targetPullFactor, LookDownPullSmoothSpeed * deltaTime);
        currentCameraDistance *= LookDownPullFactor;

        // 地面保护：往上看时相机若会低于实体所站的平面，沿视线把相机收缩到实体附近，
        // 视角方向继续跟手，而不是让旋转停住
        float finalCameraDistance = currentCameraDistance;
        float pitchSine = Mathf.Sin(PitchRotationAngle * Mathf.Deg2Rad);
        if (currentCameraDistance > 0.01f && pitchSine < 0f
            && Physics.Raycast(followTarget.position, Vector3.down, out RaycastHit groundHit,
                GroundSearchDistance, GroundMask, QueryTriggerInteraction.Ignore))
        {
            // 相机高度 = 支点高度 + 距离 × sin(俯仰角)，令其 >= 地面高度 + 保护距离：
            // 最大允许距离 = (支点高度 - 地面高度 - 保护距离) / |sin(俯仰角)|
            float availableHeightAboveGround =
                followTarget.position.y + currentPivotHeight - groundHit.point.y - MinimumCameraHeightAboveGround;
            float maximumAllowedCameraDistance = availableHeightAboveGround / -pitchSine;
            finalCameraDistance = Mathf.Min(currentCameraDistance,
                Mathf.Max(maximumAllowedCameraDistance, MinimumCameraDistance));
        }

        Vector3 pivotWorldPosition = followTarget.position + Vector3.up * currentPivotHeight;
        Quaternion cameraRotation = Quaternion.Euler(PitchRotationAngle, YawRotationAngle, 0f);

        // 两种视角统一成一个公式：FirstPersonBlendFactor = 1 时相机正好在眼睛位置朝前看
        Vector3 desiredCameraPosition = pivotWorldPosition
            + cameraRotation * new Vector3(currentShoulderLateralOffset, 0f, -finalCameraDistance);
        transform.SetPositionAndRotation(desiredCameraPosition, cameraRotation);

        // 奔跑加速（角色系统呈现）：读 Logic 的冲刺态，FOV 平滑拉大（速度感）。
        // 第一人称也生效（视野加速感与视角无关）
        if (TryGetComponent<Camera>(out var cameraComponent))
        {
            float targetFov = BaseFieldOfView + (FollowEntity.IsSprinting ? SprintFovBoost : 0f);
            cameraComponent.fieldOfView = Mathf.Lerp(
                cameraComponent.fieldOfView, targetFov, SprintFovSmoothSpeed * deltaTime);
        }
    }
}
