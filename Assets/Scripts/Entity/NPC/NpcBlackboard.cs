using UnityEngine;

/// <summary>
/// 角色黑板（玩家与 NPC 共用的唯一黑板）：实体运动数据 + 指令区 + 感知区 + 战斗运行时。
/// 指令区是"输入源"的输出接口——玩家叶子（PlayerController）把输入设备翻译成指令、
/// NPC 叶子（如 EnemyController）把决策树意图翻译成指令，写入的都是这里；
/// 状态机只消费指令，不关心指令来自谁（指令层：玩家与 AI 在此汇流）。
/// 战斗运行时（Effects/Tags/CurrentHealth）随实体生灭，来源是命中/效果系统，
/// 与指令区（输入源写、管线消费）的读写方不同，勿混用。
/// 配置参数不进黑板——状态经控制器只读属性读"活的"Inspector 值（含 MaxHealth），
/// Play 模式改参即时生效。
/// </summary>
public class NpcBlackboard : EntityBlackboard
{
    // ---- 指令区（输入源写入；电平型由控制器帧首重置，边沿型由管线消费清空）----
    public bool SprintActive;            // 电平型：移动速度意图（玩家=加速开关/长按，NPC=决策决定）
    public bool JumpQueued;              // 边沿型：跳跃请求，TryConsumeJump 每帧无条件清空（无缓冲）
    public int QueuedSkillSlot = -1;     // 边沿型：想用的技能槽位（槽 0 = 普攻），TryConsumeSkill 清回 -1（无缓冲）
    // MoveDirection（继承自 EntityBlackboard）也是指令：最终形态的世界方向
    //（玩家=摄像机相对换算，NPC=朝目标）——方向即指令，HasMoveInput 据此判定

    // ---- 感知区 ----
    public Transform Target;             // 当前目标（如玩家），null = 无目标。由感知/指派逻辑写入

    // ---- 战斗运行时（命中/效果系统写入，状态与管线消费）----
    public readonly StatusEffectContainer Effects = new StatusEffectContainer();   // Buff/Debuff 条目容器（含 CC 投影）
    public readonly TagSet Tags = new TagSet();                                    // 命名标签（状态直写 + 容器投影，见 TagSet）
    public float CurrentHealth;          // 当前生命（InitEntity 初始化为 MaxHealth；上限走控制器 MaxHealth 活属性）

    // ---- 服务引用 ----
    public NpcController Controller;

    // ---- 派生只读 ----
    // 语义注意：判定基于换算后的世界方向（旧玩家版基于原始杆量 MoveInput）。已知取舍：
    // - 相机俯仰到 ±90° 时前向投影归零，纯推前旧=Walk 零速原地踏步、新=Idle（位移一致，仅状态标签不同）
    // - 手柄回中残值由输入资源的 deadzone processor 处理，这里的阈值（幅值约 0.001）仅兜底
    public bool HasMoveInput => MoveDirection.sqrMagnitude > 0.000001f;
}
