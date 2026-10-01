using UnityEngine;

/// <summary>
/// 敌人控制器：NPC 三件套（决策树+状态机+黑板）的最小示例（召唤物可照此模式编写）。
/// 感知目前 = Inspector 拖一个 target（之后替换成正式感知系统）；
/// 决策树只有两条规则：有目标→追击、兜底→待机；状态机执行 Idle / Chase。
/// </summary>
public class EnemyController : NpcController<EnemyBlackboard>
{
    [Header("感知")]
    [Tooltip("测试用目标（通常拖玩家进来）；之后由正式感知系统赋值")]
    public Transform target;

    private Decision<EnemyBlackboard, EnemyBehavior> decisionTree;

    // 状态实例：InitNpc 构造一次
    public EnemyIdleState IdleState { get; private set; }
    public EnemyChaseState ChaseState { get; private set; }

    protected override EntityBlackboard CreateBlackboard()
    {
        return new EnemyBlackboard
        {
            Controller = this,
        };
    }

    protected override void InitNpc()
    {
        IdleState = new EnemyIdleState(NpcBoard, NpcMachine);
        ChaseState = new EnemyChaseState(NpcBoard, NpcMachine);
        NpcMachine.Initialize(IdleState);

        // 决策树：高优先级在前，最后一条恒真兜底（Selector = 优先级选择）
        decisionTree = new DecisionSelector<EnemyBlackboard, EnemyBehavior>(
            new DecisionLeaf<EnemyBlackboard, EnemyBehavior>(b => b.Target != null, EnemyBehavior.Chase),
            new DecisionLeaf<EnemyBlackboard, EnemyBehavior>(b => true, EnemyBehavior.Idle));
    }

    protected override void RunDecision()
    {
        // null = 本次无结论，维持原意图（防抖）
        NpcBoard.DesiredBehavior = decisionTree.Decide(NpcBoard) ?? NpcBoard.DesiredBehavior;
    }

    // 每帧管线：同步感知 → 决策（按间隔）→ 地面检测 → 状态机（水平移动）→ 重力 → 转向
    void Update()
    {
        NpcBoard.Target = target;   // 感知 TODO
        UpdateDecision();
        UpdateGroundCheck();
        NpcMachine.Tick();
        ApplyGravityAndVerticalMove();
        ApplyRotation();
    }
}
