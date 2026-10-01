using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 跟随相机：
/// - 第三人称：右摇杆 / 鼠标移动旋转视角；十字键左/右直接选择左/右肩，键盘 F1 切换；
/// - 第一人称：按下右摇杆（R3）/ 键盘 F2 切换，相机平滑过渡到角色头部位置，并隐藏角色模型；
/// - 俯仰限制在 ±90°；往上看时相机会自动收缩到玩家附近，不会穿过所站立的平面；
/// - 游玩时锁定并隐藏鼠标指针（可关闭）。
/// 挂在 Main Camera 上，直接从 PlayerControls 资源读取动作。
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Header("目标")]
    [Tooltip("要跟随的玩家，通常是 Player 胶囊")]
    public Transform followTarget;

    [Header("第三人称")]
    [Tooltip("第三人称注视点在玩家（胶囊中心）上方的高度，单位：米")]
    public float thirdPersonPivotHeight = 1.5f;
    [Tooltip("第三人称相机与玩家的距离，单位：米")]
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
    [Tooltip("往上看时，相机与玩家所站立平面保持的最小高度，防止相机穿到地面以下")]
    public float minimumCameraHeightAboveGround = 0.2f;
    [Tooltip("地面保护收缩相机时，相机与玩家保持的最小距离，防止贴脸穿模")]
    public float minimumCameraDistance = 2f;
    [Tooltip("从玩家向下搜索站立平面的最大距离，没找到则不限制")]
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
    private float pitchRotationAngle = 20f;         // 俯仰角（绕 X 轴），正值向下看，负值向上看

    private float shoulderSideBlendFactor = 1f;     // 左右肩的平滑插值因子，向 currentShoulderSide 靠拢
    private bool isFirstPersonMode;
    private float firstPersonBlendFactor;           // 0 = 第三人称，1 = 第一人称
    private Renderer[] playerMeshRenderers;

    void Awake()
    {
        gamepadLookAction = inputActionAsset.FindAction("Look", throwIfNotFound: true);
        mouseLookAction = inputActionAsset.FindAction("MouseLook", throwIfNotFound: true);
        switchShoulderAction = inputActionAsset.FindAction("SwitchShoulder", throwIfNotFound: true);
        toggleViewModeAction = inputActionAsset.FindAction("ToggleView", throwIfNotFound: true);
        switchShoulderAction.performed += OnSwitchShoulder;
    }

    void OnDestroy()
    {
        switchShoulderAction.performed -= OnSwitchShoulder;
    }

    void OnEnable()
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

    void OnDisable()
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

    void LateUpdate()
    {
        if (followTarget == null)
            return;

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

            // 第一人称时隐藏角色模型，防止挡住视线
            playerMeshRenderers ??= followTarget.GetComponentsInChildren<Renderer>();
            foreach (var meshRenderer in playerMeshRenderers)
            {
                meshRenderer.enabled = !isFirstPersonMode;
            }
        }

        // 各项平滑过渡
        shoulderSideBlendFactor = Mathf.Lerp(shoulderSideBlendFactor, currentShoulderSide, shoulderSwitchSmoothSpeed * Time.deltaTime);
        firstPersonBlendFactor = Mathf.Lerp(firstPersonBlendFactor, isFirstPersonMode ? 1f : 0f, viewModeSwitchSmoothSpeed * Time.deltaTime);

        // 过渡到第一人称：注视点高度降到眼睛，距离和肩偏移收缩为 0
        float currentPivotHeight = Mathf.Lerp(thirdPersonPivotHeight, firstPersonEyeHeight, firstPersonBlendFactor);
        float currentCameraDistance = thirdPersonCameraDistance * (1f - firstPersonBlendFactor);
        float currentShoulderLateralOffset = shoulderOffsetDistance * shoulderSideBlendFactor * (1f - firstPersonBlendFactor);

        // 右摇杆：速率语义，按住幅度 × 速度 × 时间
        Vector2 gamepadLookInput = gamepadLookAction.ReadValue<Vector2>();
        yawRotationAngle += gamepadLookInput.x * gamepadLookRotationSpeed * Time.deltaTime;
        pitchRotationAngle -= gamepadLookInput.y * gamepadLookRotationSpeed * Time.deltaTime;

        // 鼠标：位移语义，本帧像素位移 × 灵敏度（不乘 deltaTime，与帧率无关）
        Vector2 mouseLookInput = mouseLookAction.ReadValue<Vector2>();
        yawRotationAngle += mouseLookInput.x * mouseLookSensitivity;
        pitchRotationAngle -= mouseLookInput.y * mouseLookSensitivity;

        // 俯仰角：只按用户设置限制（默认 ±90°），地面保护不卡视角
        pitchRotationAngle = Mathf.Clamp(pitchRotationAngle, -lookUpAngleLimit, lookDownAngleLimit);

        // 地面保护：往上看时相机若会低于玩家所站的平面，沿视线把相机收缩到玩家附近，
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
    }
}
