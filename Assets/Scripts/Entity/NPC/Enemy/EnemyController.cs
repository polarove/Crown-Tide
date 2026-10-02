using UnityEngine;

/// <summary>敌人行为意图（决策树产出，输入源内部翻译成指令，不进黑板）</summary>
public enum EnemyBehavior
{
    Idle,       // 待机
    Chase       // 追击
}

/// <summary>
/// 敌人控制器：角色层的"决策树输入源"叶子（NPC 三件套：决策树+状态机+黑板的最小示例，
/// 召唤物可照此模式编写）。
/// 感知目前 = Inspector 拖一个 target（之后替换成正式感知系统）；
/// 决策树只有两条规则：有目标→追击、兜底→待机。
/// 意图翻译每帧执行（UpdateCommands）：低频决策产出 behavior，这里把它连续翻译成黑板指令
/// （MoveDirection 朝目标——与玩家推杆同构），移动/停止由共享的 Walk/Idle 状态执行——
/// 决策树代替玩家思考，指令层玩家与 AI 汇流。
/// 注意：Update 管线唯一声明在 NpcController——本类不得声明 Update（会遮蔽整条管线）。
/// </summary>
public class EnemyController : NpcController
{
    [Header("感知")]
    [Tooltip("测试用目标（通常拖玩家进来）；之后由正式感知系统赋值")]
    public Transform target;

    [Header("决策")]
    [Tooltip("决策树评估间隔（秒）：越小反应越快、开销越大；0 = 每帧评估")]
    public float decisionInterval = 0.2f;

    private Decision<NpcBlackboard, EnemyBehavior> decisionTree;
    private EnemyBehavior behavior = EnemyBehavior.Idle;   // 当前意图（决策低频刷新）
    private float nextDecisionTime;

    protected override void InitEntity()
    {
        base.InitEntity();   // 建状态机与七状态（与其他角色同构）

        // 决策树：高优先级在前，最后一条恒真兜底（Selector = 优先级选择）
        decisionTree = new DecisionSelector<NpcBlackboard, EnemyBehavior>(
            new DecisionLeaf<NpcBlackboard, EnemyBehavior>(b => b.Target != null, EnemyBehavior.Chase),
            new DecisionLeaf<NpcBlackboard, EnemyBehavior>(b => true, EnemyBehavior.Idle));
    }

    protected override void UpdateCommands()
    {
        NpcBoard.Target = target;   // 感知同步（TODO：正式感知系统）
        TryRunDecision();

        // 意图翻译（每帧）：决策低频产出意图，指令连续刷新——玩家推杆与 AI 追击在此汇成同一种指令。
        // 已知取舍：Target 在决策间隔内闪断会 Walk↔Idle 抖动（Enter/Exit 为空，现在无成本；接动画时加迟滞）
        if (behavior == EnemyBehavior.Chase && NpcBoard.Target != null)
        {
            // 朝目标的方向（投影到水平面），限幅 1；方向同时供 ApplyRotation 转身
            Vector3 toTarget = NpcBoard.Target.position - transform.position;
            toTarget.y = 0f;
            NpcBoard.MoveDirection = Vector3.ClampMagnitude(toTarget, 1f);   // 演示用直线追击，无绕障/寻路
        }
        // 无 else：基类帧首已把 MoveDirection 重置为零——输入源沉默 = 站桩
    }

    /// <summary>决策定时器（决策机械是 AI 输入源的内部事务，收编在本叶子）：
    /// 到期评估决策树；null = 本次无结论，维持原意图（防抖）</summary>
    private void TryRunDecision()
    {
        if (Time.time < nextDecisionTime)
        {
            return;
        }
        nextDecisionTime = decisionInterval <= 0f ? Time.time : Time.time + decisionInterval;
        behavior = decisionTree.Decide(NpcBoard) ?? behavior;
    }
}
