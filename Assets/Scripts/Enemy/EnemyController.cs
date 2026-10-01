using UnityEngine;

/// <summary>
/// 敌人控制器：接入实体框架的最小示例（召唤物可照此模式编写）。
/// 感知目前 = Inspector 拖一个 target（之后替换成正式感知系统）；
/// 状态机只有 Idle / Chase 两个占位状态，复杂行为后续加状态即可。
/// </summary>
public class EnemyController : EntityController
{
    [Header("感知")]
    [Tooltip("测试用目标（通常拖玩家进来）；之后由正式感知系统赋值")]
    public Transform target;

    private EnemyBlackboard enemyBlackboard;
    private StateMachine<EnemyBlackboard> stateMachine;

    // 状态实例：InitEntity 构造一次
    public EnemyIdleState IdleState { get; private set; }
    public EnemyChaseState ChaseState { get; private set; }

    protected override EntityBlackboard CreateBlackboard()
    {
        enemyBlackboard = new EnemyBlackboard
        {
            Controller = this,
        };
        return enemyBlackboard;
    }

    protected override void InitEntity()
    {
        stateMachine = new StateMachine<EnemyBlackboard>();
        IdleState = new EnemyIdleState(enemyBlackboard, stateMachine);
        ChaseState = new EnemyChaseState(enemyBlackboard, stateMachine);
        stateMachine.Initialize(IdleState);
    }

    // 每帧管线：同步感知 → 地面检测 → 状态机（水平移动）→ 重力 → 转向
    void Update()
    {
        enemyBlackboard.Target = target;   // 感知 TODO
        UpdateGroundCheck();
        stateMachine.Tick();
        ApplyGravityAndVerticalMove();
        ApplyRotation();
    }
}
