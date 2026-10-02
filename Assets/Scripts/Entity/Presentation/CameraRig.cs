using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 相机装置（Presentation 层）：跟随实体的表现相机，只读 Entity 状态（不修改数据——
/// 需求钦定：相机行为做到角色系统的 Presentation Layer）。
/// 旧 CameraFollow 平移 + 三项角色系统呈现：
/// - 抬头 90°（俯仰上限可配，默认正对天空）+ 往上看时地面保护收缩（视角跟手不停转）；
/// - 低头相机靠近（新增）：俯仰向下超过阈值，相机沿视线渐缩贴向角色（看脚下不穿模不远景）；
/// - 奔跑加速（新增）：Entity.IsSprinting（Logic 只读）→ FOV 平滑拉大，速度感呈现。
/// 结构：挂在相机物体上、持 Entity 引用（不全局找——多人纪律：每玩家自己的相机，
/// spawn 系统指派）；LateUpdate 独立节拍（表现层不参与仿真顺序）。
/// 视角基准注入：把自身 Transform 填给宿主 Entity 的 PlayerInputSource.viewTransform
/// （移动方向的投影基准；只填空缺，不覆盖手连）。
/// 相机输入自持（本玩家的显示设备输入，本地直读合理）。
/// </summary>
public sealed class CameraRig : MonoBehaviour
{
    [Header("目标")]
    [Tooltip("要跟随的实体（通常是玩家 Entity）；空 = 本组件不工作")]
    public Entity followEntity;

    [Header("第三人称")]
    [Tooltip("第三人称注视点在实体（胶囊中心）上方的高度，单位：米")]
    public float thirdPersonPivotHeight = 1.5f;
    [Tooltip("第三人称相机与实体的距离，单位：米")]
    public float thirdPersonCameraDistance = 8f;
    [Tooltip("左右肩（过肩视角）的侧向偏移距离，单位：米")]
    public float shoulderOffsetDistance = 1.5f;
    [Tooltip("左右肩切换的平滑速度，越大切换越快")]
    public float shoulderSwitchSmoothSpeed = 10f;
    [Tooltip("当前肩侧：1 = 右肩，-1 = 左肩（运行时会按输入自动改）")]
    public int currentShoulderSide = 1;

    [Header("第一人称")]
    [Tooltip("第一人称眼睛在胶囊中心上方的高度（胶囊高 2、脚到中心约 1.0，此值 0.6 即正常眼高）")]
    public float firstPersonEyeHeight = 0.6f;
    [Tooltip("第一/第三人称切换的平滑速度，越大过渡越快")]
    public float viewModeSwitchSmoothSpeed = 8f;

    [Header("低头靠近（角色系统呈现）")]
    [Tooltip("低头相机靠近的开始角度（度，俯仰向下为正）：低头超过它，相机开始沿视线收缩")]
    public float lookDownPullStartAngle = 35f;
    [Tooltip("低头到正下方（90°）时相机距离收缩到的最小比例（0.25 = 收到基础距离的四分之一）")]
    [Range(0.05f, 1f)]
    public float lookDownPullMinFactor = 0.25f;
    [Tooltip("低头收缩的平滑速度")]
    public float lookDownPullSmoothSpeed = 6f;

    [Header("奔跑加速（角色系统呈现）")]
    [Tooltip("奔跑时 FOV 增量（度）：冲刺中视野拉大，速度感")]
    public float sprintFovBoost = 12f;
    [Tooltip("FOV 过渡平滑速度")]
    public float sprintFovSmoothSpeed = 5f;

    [Header("旋转")]
    [Tooltip("输入动作资源，拖入 Assets/Input/PlayerControls.inputactions")]
    public InputActionAsset inputActionAsset;
    [Tooltip("手柄右摇杆旋转视角的速度，单位：度/秒（推满摇杆时每秒转的角度）")]
    public float gamepadLookRotationSpeed = 150f;
    [Tooltip("鼠标灵敏度：鼠标每移动 1 像素旋转的角度，单位：度/像素")]
    public float mouseLookSensitivity = 0.3f;
    [Tooltip("往上看的最大角度（度），90 = 正对天空")]
    public float lookUpAngleLimit = 90f;
    [Tooltip("往下看的最大角度（度），90 = 正对脚下")]
    public float lookDownAngleLimit = 90f;

    [Header("地面保护")]
    [Tooltip("往上看时，相机与实体所站立平面保持的最小高度，防止相机穿到地面以下")]
    public float minimumCameraHeightAboveGround = 0.2f;
    [Tooltip("地面保护收缩相机时，相机与实体保持的最小距离，防止贴脸穿模")]
    public float minimumCameraDistance = 2f;
    [Tooltip("从实体向下搜索站立平面的最大距离，没找到则不限制")]
    public float groundSearchDistance = 10f;
    [Tooltip("哪些层参与站立平面检测，默认所有层")]
    public LayerMask groundMask = ~0;

    [Tooltip("游玩时锁定并隐藏鼠标指针，防止指针碰到屏幕边缘后转不动")]
    public bool lockAndHideCursor = true;

    private InputAction gamepadLookAction;          // 右摇杆（速率语义）
    private InputAction mouseLookAction;            // 鼠标移动（位移语义）
    private InputAction switchShoulderAction;
    private InputAction toggleViewModeAction;

    private float yawRotationAngle = 0f;            // 水平旋转角（绕 Y 轴），不受限制
    private float pitchRotationAngle = 20f;         // 俯仰角，正值向下看，负值向上看
    private float shoulderSideBlendFactor = 1f;     // 左右肩的平滑插值因子
    private float lookDownPullFactor = 1f;          // 低头收缩因子（1 = 不收缩）
    private float baseFieldOfView;                  // 初始 FOV（奔跑加速的基准）
    private bool isFirstPersonMode;
    private float firstPersonBlendFactor;           // 0 = 第三人称，1 = 第一人称
    private Renderer[] entityMeshRenderers;         // 第一人称显隐（表现层自持对象，只读数据不改）

    private void Awake()
    {
        // 相机是表现层独立装置（不在 Entity 组件族内），自取输入动作不参与实体的单 Awake 规则
        gamepadLookAction = inputActionAsset.FindAction("Look", throwIfNotFound: true);
        mouseLookAction = inputActionAsset.FindAction("MouseLook", throwIfNotFound: true);
        switchShoulderAction = inputActionAsset.FindAction("SwitchShoulder", throwIfNotFound: true);
        toggleViewModeAction = inputActionAsset.FindAction("ToggleView", throwIfNotFound: true);
        switchShoulderAction.performed += OnSwitchShoulder;
        Camera cameraComponent = GetComponent<Camera>();
        baseFieldOfView = cameraComponent != null ? cameraComponent.fieldOfView : 60f;
    }

    private void OnDestroy()
    {
        if (switchShoulderAction != null)
        {
            switchShoulderAction.performed -= OnSwitchShoulder;
        }
    }

    private void OnEnable()
    {
        gamepadLookAction.Enable();
        mouseLookAction.Enable();
        switchShoulderAction.Enable();
        toggleViewModeAction.Enable();

        if (lockAndHideCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void OnDisable()
    {
        gamepadLookAction.Disable();
        mouseLookAction.Disable();
        switchShoulderAction.Disable();
        toggleViewModeAction.Disable();

        if (lockAndHideCursor)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    // 十字键左 = 左肩，十字键右 = 右肩，其他绑定（键盘 F1）= 左右切换
    private void OnSwitchShoulder(InputAction.CallbackContext context)
    {
        string controlPath = context.control.path;
        if (controlPath == "<Gamepad>/dpad/left")
        {
            currentShoulderSide = -1;
        }
        else if (controlPath == "<Gamepad>/dpad/right")
        {
            currentShoulderSide = 1;
        }
        else
        {
            currentShoulderSide = -currentShoulderSide;
        }
    }

    private void LateUpdate()
    {
        if (followEntity == null)
        {
            return;
        }
        Transform followTarget = followEntity.transform;

        // 视角基准注入（一次性；只填空缺不覆盖手连——多人下 spawn 系统指派各自的相机）
        PlayerInputSource inputSource = followEntity.GetComponent<PlayerInputSource>();
        if (inputSource != null)
        {
            inputSource.SetViewTransform(transform);
        }

        // 编辑器里按 Esc 会解锁指针；窗口重新获得焦点时恢复锁定
        if (lockAndHideCursor && Cursor.lockState != CursorLockMode.Locked && Application.isFocused)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // 按下右摇杆（R3）/ 键盘 F2：切换第一/第三人称
        if (toggleViewModeAction.WasPressedThisFrame())
        {
            isFirstPersonMode = !isFirstPersonMode;

            // 第一人称时隐藏角色模型（表现层自持的显隐，防挡视线）
            entityMeshRenderers ??= followTarget.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < entityMeshRenderers.Length; i++)
            {
                entityMeshRenderers[i].enabled = !isFirstPersonMode;
            }
        }

        // 各项平滑过渡
        float deltaTime = Time.deltaTime;
        shoulderSideBlendFactor = Mathf.Lerp(shoulderSideBlendFactor, currentShoulderSide, shoulderSwitchSmoothSpeed * deltaTime);
        firstPersonBlendFactor = Mathf.Lerp(firstPersonBlendFactor, isFirstPersonMode ? 1f : 0f, viewModeSwitchSmoothSpeed * deltaTime);

        // 过渡到第一人称：注视点高度降到眼睛，距离和肩偏移收缩为 0
        float currentPivotHeight = Mathf.Lerp(thirdPersonPivotHeight, firstPersonEyeHeight, firstPersonBlendFactor);
        float currentCameraDistance = thirdPersonCameraDistance * (1f - firstPersonBlendFactor);
        float currentShoulderLateralOffset = shoulderOffsetDistance * shoulderSideBlendFactor * (1f - firstPersonBlendFactor);

        // 右摇杆：速率语义，按住幅度 × 速度 × 时间
        Vector2 gamepadLookInput = gamepadLookAction.ReadValue<Vector2>();
        yawRotationAngle += gamepadLookInput.x * gamepadLookRotationSpeed * deltaTime;
        pitchRotationAngle -= gamepadLookInput.y * gamepadLookRotationSpeed * deltaTime;

        // 鼠标：位移语义，本帧像素位移 × 灵敏度（不乘 deltaTime，与帧率无关）
        Vector2 mouseLookInput = mouseLookAction.ReadValue<Vector2>();
        yawRotationAngle += mouseLookInput.x * mouseLookSensitivity;
        pitchRotationAngle -= mouseLookInput.y * mouseLookSensitivity;

        // 俯仰角：只按用户设置限制（默认 ±90°），地面保护/低头收缩不卡视角
        pitchRotationAngle = Mathf.Clamp(pitchRotationAngle, -lookUpAngleLimit, lookDownAngleLimit);

        // 低头相机靠近（角色系统呈现）：低头超过起始角，收缩因子从 1 渐降到最小比例
        // （俯仰向下为正；第一人称时距离已是 0，不参与）
        float pitchDown = Mathf.Max(0f, pitchRotationAngle);
        float targetPullFactor = pitchDown <= lookDownPullStartAngle
            ? 1f
            : Mathf.Lerp(1f, lookDownPullMinFactor,
                Mathf.InverseLerp(lookDownPullStartAngle, lookDownAngleLimit, pitchDown));
        lookDownPullFactor = Mathf.Lerp(lookDownPullFactor, targetPullFactor, lookDownPullSmoothSpeed * deltaTime);
        currentCameraDistance *= lookDownPullFactor;

        // 地面保护：往上看时相机若会低于实体所站的平面，沿视线把相机收缩到实体附近，
        // 视角方向继续跟手，而不是让旋转停住
        float finalCameraDistance = currentCameraDistance;
        float pitchSine = Mathf.Sin(pitchRotationAngle * Mathf.Deg2Rad);
        if (currentCameraDistance > 0.01f && pitchSine < 0f
            && Physics.Raycast(followTarget.position, Vector3.down, out RaycastHit groundHit,
                groundSearchDistance, groundMask, QueryTriggerInteraction.Ignore))
        {
            // 相机高度 = 支点高度 + 距离 × sin(俯仰角)，令其 >= 地面高度 + 保护距离：
            // 最大允许距离 = (支点高度 - 地面高度 - 保护距离) / |sin(俯仰角)|
            float availableHeightAboveGround =
                followTarget.position.y + currentPivotHeight - groundHit.point.y - minimumCameraHeightAboveGround;
            float maximumAllowedCameraDistance = availableHeightAboveGround / -pitchSine;
            finalCameraDistance = Mathf.Min(currentCameraDistance,
                Mathf.Max(maximumAllowedCameraDistance, minimumCameraDistance));
        }

        Vector3 pivotWorldPosition = followTarget.position + Vector3.up * currentPivotHeight;
        Quaternion cameraRotation = Quaternion.Euler(pitchRotationAngle, yawRotationAngle, 0f);

        // 两种视角统一成一个公式：firstPersonBlendFactor = 1 时相机正好在眼睛位置朝前看
        Vector3 desiredCameraPosition = pivotWorldPosition
            + cameraRotation * new Vector3(currentShoulderLateralOffset, 0f, -finalCameraDistance);
        transform.SetPositionAndRotation(desiredCameraPosition, cameraRotation);

        // 奔跑加速（角色系统呈现）：读 Logic 的冲刺态，FOV 平滑拉大（速度感）。
        // 第一人称也生效（视野加速感与视角无关）
        Camera cameraComponent = GetComponent<Camera>();
        if (cameraComponent != null)
        {
            float targetFov = baseFieldOfView + (followEntity.IsSprinting ? sprintFovBoost : 0f);
            cameraComponent.fieldOfView = Mathf.Lerp(
                cameraComponent.fieldOfView, targetFov, sprintFovSmoothSpeed * deltaTime);
        }
    }
}
