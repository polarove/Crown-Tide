using UnityEngine;

/// <summary>
/// 运动原语（Physics 层）：CharacterController 的移动/重力/地面检测/转向 + 运动参数。
/// 旧 EntityController 与 NpcController 的运动能力合并平移（去继承：从"基类提供能力"
/// 变为"组件提供服务"），语义逐条保真：
/// - 地面检测贴地钳 -2（防下落速度无限累积）；
/// - 顶点滞空（速度接近 0 时重力减弱）/ 下落加重（落地干脆）；
/// - 零方向也 Move（CharacterController 依赖 Move 做贴地/去穿插）；
/// - 有移动方向时平滑转向。
/// 参数全部活值（Play 模式调参即时生效）；被 Logic 层（状态/Brain 管线）调用，
/// 本类不做任何"要不要动"的决策——那是 Capability 与状态机的事。
/// 零 Awake：由 EntityBrain.Bootstrap 调 Initialize 注入 CharacterController（单 Awake 规则）。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public sealed class EntityMotor : MonoBehaviour
{
    [Header("移动")]
    [Tooltip("移动速度，单位：米/秒（MoveSpeed 乘法链的基础值）")]
    public float walkSpeed = 5f;
    [Tooltip("加速倍率：加速时的速度 = walkSpeed × 此值，调走路速度后加速自动跟着变")]
    public float sprintMultiplier = 1.6f;
    [Tooltip("转向移动方向的平滑速度，数值越大转身越快")]
    public float turnSpeed = 10f;

    [Header("跳跃")]
    [Tooltip("跳跃能达到的最大高度，单位：米（JumpPower 乘法链的基础值）")]
    public float jumpHeight = 1.2f;

    [Header("重力")]
    [Tooltip("重力加速度，负数表示方向向下；绝对值越大上升下落越快（-20 约为真实重力的两倍，手感更干脆）")]
    public float gravity = -20f;
    [Tooltip("接近跳跃顶点时的重力倍率，越小滞空感越明显（0.5 = 顶点附近重力减半）")]
    public float apexHangGravityMultiplier = 0.5f;
    [Tooltip("竖直速度绝对值小于此值（米/秒）时视为接近顶点，进入滞空")]
    public float apexHangVerticalSpeedThreshold = 1.5f;
    [Tooltip("下落阶段的重力倍率，越大下落越快、落地越干脆")]
    public float fallingGravityMultiplier = 1.5f;

    [Header("地面检测")]
    [Tooltip("从角色中心向下射线的额外长度，胶囊半高 + 此值以内碰到地面就算站在地上")]
    public float groundCheckDistance = 0.25f;
    [Tooltip("哪些层参与地面检测，默认所有层")]
    public LayerMask groundMask = ~0;

    private CharacterController controller;

    /// <summary>是否站在地面/平台上（Brain 管线每帧 GroundCheck 刷新）</summary>
    public bool IsGrounded { get; private set; }

    /// <summary>竖直速度（米/秒）：跳跃冲量写入点（Brain.TryConsumeJump），重力步进读写</summary>
    public float VerticalVelocity { get; set; }

    /// <summary>注入 CharacterController（Brain.Bootstrap 调用一次）</summary>
    public void Initialize()
    {
        controller = GetComponent<CharacterController>();
    }

    /// <summary>从角色中心向下射线，判断是否站在地面/平台上（贴地钳 -2 防累积）</summary>
    public void GroundCheck()
    {
        IsGrounded = Physics.Raycast(transform.position, Vector3.down,
            controller.height / 2f + groundCheckDistance, groundMask,
            QueryTriggerInteraction.Ignore);

        if (IsGrounded && VerticalVelocity < 0f)
        {
            VerticalVelocity = -2f;   // 贴地，防止下落速度无限累积
        }
    }

    /// <summary>水平移动：按传入速度沿方向 Move。方向为零也调用（保留贴地/去穿插行为）。
    /// 状态与效果乘数由调用侧（ApplyLocomotion）算好传入，本方法只做移动</summary>
    public void MoveHorizontal(float speed, Vector3 direction)
    {
        controller.Move(Time.deltaTime * speed * direction);
    }

    /// <summary>
    /// 重力步进（竖直方向是 Vector3.up）。空中根据竖直速度调节重力倍率：
    /// - 接近顶点（速度接近 0）：重力减弱，速度缓慢穿过零点，形成顶部滞空感；
    /// - 下落阶段：重力加大，下落干脆、不漂浮。
    /// deltaTime 入参（不用 Time.deltaTime——网络时间纪律：换 NetworkTime 只改 Brain 传参）
    /// </summary>
    public void ApplyGravityAndVerticalMove(float deltaTime)
    {
        float currentGravityMultiplier = 1f;
        if (!IsGrounded)
        {
            if (Mathf.Abs(VerticalVelocity) < apexHangVerticalSpeedThreshold)
            {
                currentGravityMultiplier = apexHangGravityMultiplier;
            }
            else if (VerticalVelocity < 0f)
            {
                currentGravityMultiplier = fallingGravityMultiplier;
            }
        }
        VerticalVelocity += gravity * currentGravityMultiplier * deltaTime;
        controller.Move(VerticalVelocity * deltaTime * Vector3.up);
    }

    /// <summary>有移动方向时平滑转向（方向为零不转，保留原朝向）</summary>
    public void RotateTowards(Vector3 direction, float deltaTime)
    {
        if (direction.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * deltaTime);
        }
    }
}
