using UnityEngine;

/// <summary>
/// 实体黑板基类：所有 CharacterController 实体（玩家/敌人/召唤物）共享的运动数据。
/// 注意：这里不放 Controller 引用——由具体黑板（NpcBlackboard）持有类型化的控制器引用，
/// 避免基类/派生类同名字段互相遮蔽（两个不同字段、基类那个永远为 null 的陷阱）。
/// </summary>
public abstract class EntityBlackboard : Blackboard
{
    // ---- 世界 / 运动数据（EntityController 每帧维护）----
    public bool IsGrounded;             // 本帧地面射线结果
    public float VerticalVelocity;      // 竖直速度，跳跃冲量与重力共用、跨状态保留
    public Vector3 MoveDirection;       // 已限幅的本帧移动方向，也是移动指令（玩家=摄像机相对换算，NPC=朝目标；控制器帧首重置）
}
