using UnityEngine;

/// <summary>
/// 实体控制器基类：CharacterController 实体（玩家/敌人/召唤物）共用的序列化运动参数与运动能力。
/// 派生类规则：
/// - 只 override CreateBlackboard() / InitEntity()，不要声明 Awake（会静默隐藏基类模板且无编译警告）；
/// - Update / OnGUI 等魔术方法只能写在叶子控制器里（Unity 只调最派生声明，基类写了会被静默隐藏）；
/// - 状态机与每帧管线（感知 → 状态机 → 运动）由各叶子控制器自己组织。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public abstract class EntityController : MonoBehaviour
{
    [Header("移动")]
    [Tooltip("移动速度，单位：米/秒")]
    public float walkSpeed = 5f;
    [Tooltip("转向移动方向的平滑速度，数值越大转身越快")]
    public float turnSpeed = 10f;
    [Tooltip("重力加速度，负数表示方向向下；绝对值越大上升下落越快（-20 约为真实重力的两倍，手感更干脆）")]
    public float gravity = -20f;

    [Header("滞空手感")]
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

    // ---- 运行时（模板 Awake 初始化，派生类经 CreateBlackboard/InitEntity 参与）----
    protected CharacterController CharacterController { get; private set; }
    protected EntityBlackboard Board { get; private set; }

    /// <summary>状态要读的移动速度（活的 Inspector 值，Play 模式调参即时生效）</summary>
    public float WalkSpeed => walkSpeed;

    // 模板 Awake：先取组件 → 建黑板 → 派生初始化，保证 Board 在任何使用前就绪。
    // 派生类不要声明 Awake，只 override CreateBlackboard / InitEntity
    private void Awake()
    {
        CharacterController = GetComponent<CharacterController>();
        Board = CreateBlackboard();
        InitEntity();
    }

    /// <summary>构造本实体的黑板并返回（此时 CharacterController 已就绪，可注入引用）</summary>
    protected abstract EntityBlackboard CreateBlackboard();

    /// <summary>实体初始化：建状态机与状态、取其他组件引用（每实体一次）</summary>
    protected virtual void InitEntity() { }

    // ---- 各状态共用的运动能力（读写黑板）----

    /// <summary>水平移动：按传入速度沿黑板方向 Move。方向为零也调用（保留贴地/去穿插行为）。
    /// public：状态类需要跨类调用</summary>
    public void ApplyHorizontalMovement(float speed)
    {
        CharacterController.Move(Time.deltaTime * speed * Board.MoveDirection);
    }

    /// <summary>从角色中心向下射线，判断是否站在地面/平台上</summary>
    protected void UpdateGroundCheck()
    {
        Board.IsGrounded = Physics.Raycast(transform.position, Vector3.down,
            CharacterController.height / 2f + groundCheckDistance, groundMask,
            QueryTriggerInteraction.Ignore);

        if (Board.IsGrounded && Board.VerticalVelocity < 0f)
        {
            Board.VerticalVelocity = -2f;   // 贴地，防止下落速度无限累积
        }
    }

    /// <summary>
    /// 重力（竖直方向是 Vector3.up）。
    /// 空中根据竖直速度调节重力倍率：
    /// - 接近顶点（速度接近 0）：重力减弱，速度缓慢穿过零点，形成顶部滞空感
    /// - 下落阶段：重力加大，下落干脆、不漂浮
    /// </summary>
    protected void ApplyGravityAndVerticalMove()
    {
        float currentGravityMultiplier = 1f;
        if (!Board.IsGrounded)
        {
            if (Mathf.Abs(Board.VerticalVelocity) < apexHangVerticalSpeedThreshold)
            {
                currentGravityMultiplier = apexHangGravityMultiplier;
            }
            else if (Board.VerticalVelocity < 0f)
            {
                currentGravityMultiplier = fallingGravityMultiplier;
            }
        }
        Board.VerticalVelocity += gravity * currentGravityMultiplier * Time.deltaTime;
        CharacterController.Move(Board.VerticalVelocity * Time.deltaTime * Vector3.up);
    }

    /// <summary>有移动方向时平滑转向</summary>
    protected void ApplyRotation()
    {
        if (Board.MoveDirection.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(Board.MoveDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
        }
    }
}
