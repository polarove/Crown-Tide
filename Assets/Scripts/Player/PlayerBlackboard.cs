using UnityEngine;

/// <summary>
/// 玩家黑板：实体黑板 + 玩家特有的输入与加速键数据（黑板模式）。
/// 纯数据类；配置参数不进黑板——状态经控制器只读属性读“活的” Inspector 值，
/// 保证 Play 模式下改参数即时生效。
/// </summary>
public class PlayerBlackboard : EntityBlackboard
{
    // ---- 输入数据（PlayerController 写入）----
    public Vector2 MoveInput;           // OnMove 持续更新
    public bool JumpQueued;             // OnJump 置位，TryConsumeJump 每帧无条件清空（无缓冲，同旧实现）

    // ---- 加速键状态（UpdateSprintInput 维护，语义与拆分前逐行一致）----
    public bool SprintPressing;         // 加速键当前是否被按着
    public float SprintPressTime;       // 本次按下开始的时刻，用于区分点按/长按
    public bool SprintToggled;          // 点按切换出的加速开关，再点按一次取消
    public bool SprintHoldActive;       // 长按期间为 true，松开即恢复

    // ---- 服务引用 ----
    public PlayerController Controller;

    // ---- 派生只读 ----
    public bool SprintActive => SprintToggled || SprintHoldActive;
    public bool HasMoveInput => MoveInput != Vector2.zero;
}
