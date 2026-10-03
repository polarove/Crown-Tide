using UnityEngine;

/// <summary>
/// 指令缓冲（Input 层 → Logic 层的唯一桥）：全部指令的扁平载体。
/// 指令语义分两类（旧黑板指令区的语义平移，需求钦定）：
/// - 电平型（MoveDirection/LookDirection/SprintActive/AimActive）：表达"现在正在"。
///   Brain 帧首 ResetLevels——输入源沉默 = 站桩（附身切换帧、输入源早退路径都安全）；
/// - 边沿型（JumpQueued/AttackQueued/SkillSlotQueued）：表达"请求一次"。
///   输入源在消息回调/决策处置位，管线消费点无条件清空（无缓冲，清在帧首会丢输入）。
/// 指令不区分来源——玩家按键与 AI 决策写的是同一个缓冲（指令层汇流）；
/// "指令该翻译成什么"（如瞄准中攻击=射击）是 Logic 消费点的事，不归输入源。
/// 网络镜像就绪：全部原始类型扁平字段，零对象引用——将来 NGO INetworkSerializable
/// 直接镜像成 struct 原样拷贝，它就是"客户端输入 → 服务端仿真"的线上格式。
/// </summary>
public sealed class CommandBuffer
{
    // ---- 电平型（Brain 帧首重置；输入源每帧重写）----
    public Vector3 MoveDirection;      // 世界方向（限幅 1）：玩家=视角相对换算，AI=朝目标
    public Vector3 LookDirection;      // 水平攻击意图：玩家=自己的视角，AI=自己的目标；与移动独立
    public bool SprintActive;          // 加速意图
    public bool AimActive;             // 瞄准意图（射击连招的电平半边，翻译在 Brain.TryConsumeAction）

    // ---- 边沿型（置位后由管线消费清空；无缓冲）----
    public bool JumpQueued;            // 跳跃请求
    public bool PossessionQueued;      // 固有附身主动技能请求，不占技能槽
    public bool AttackQueued;          // 攻击请求（普攻/瞄准射击/连段续击，由消费点按 Data 翻译）
    public int SkillSlotQueued = 0;    // 想用的技能（EnumSkillKind 值 ±1；0 = 无请求哨兵，0 不是合法技能位）

    /// <summary>帧首重置电平型指令（Brain 调用；边沿型不在此列，帧首清会丢输入）</summary>
    public void ResetLevels()
    {
        MoveDirection = Vector3.zero;
        LookDirection = Vector3.zero;
        SprintActive = false;
        AimActive = false;
    }

    /// <summary>清空边沿型指令（死亡门等"实体不再接受指令"的路径调用，防复活瞬间残留请求）</summary>
    public void ClearEdges()
    {
        PossessionQueued = false;
        JumpQueued = false;
        AttackQueued = false;
        SkillSlotQueued = 0;
    }

    /// <summary>是否有移动指令（判定基于限幅后的世界方向；手柄回中残值由输入资源 deadzone 处理，
    /// 这里的阈值（幅值约 0.001）仅兜底。语义注意：相机俯仰到 ±90° 时前向投影归零，
    /// 推杆会判"无移动"——位移一致，仅状态标签归 Idle）</summary>
    public bool HasMoveInput => MoveDirection.sqrMagnitude > 0.000001f;
}
