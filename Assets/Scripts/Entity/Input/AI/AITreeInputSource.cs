using UnityEngine;

/// <summary>AI 行为意图（决策树产出，输入源内部翻译成指令，不进缓冲）</summary>
public enum EnumAIIntent
{
    Idle = 0,       // 待机
    Chase = 1,      // 追击
}

/// <summary>
/// 决策树输入源（Input 层）：跑决策树产出意图，每帧把意图翻译成指令写 CommandBuffer——
/// 与 PlayerInputSource 写同一个缓冲（指令层汇流，谁在写指令是唯一差异）。
/// 决策机械（定时评估 + 意图枚举）是本输入源的内部事务：低频决策防抖动，意图翻译每帧连续
/// （决策间隔内目标闪断维持原方向——已知取舍：Target 闪断会 Walk↔Idle 抖动，接动画时加迟滞）。
/// 感知目前 = Inspector 拖一个 target（占位，之后替换成正式感知系统）。
/// 决策定时用累积器（decisionTimer -= dt，不用 Time.time 差值——网络时间纪律）。
/// 决策树在构造上与 Entity 解耦（Decision<TContext, TIntent> 读任意上下文）——
/// 强度差异来自决策树内容 + 槽位 + config，不来自类继承（需求钦定）。
/// </summary>
public sealed class AITreeInputSource : MonoBehaviour, IInputSource
{
    [Header("感知")]
    [Tooltip("测试用目标（通常拖玩家实体进来）；之后由正式感知系统赋值。空 = 无目标 → 待机")]
    public Transform? Target;

    [Header("决策")]
    [Tooltip("决策树评估间隔（秒）：越小反应越快、开销越大；0 = 每帧评估")]
    public float DecisionInterval = 0.2f;

    /// <summary>宿主实体（懒取一次）</summary>
    private Entity Entity = null!;

    /// <summary>决策树（本输入源的"脑子"：默认两条规则——有目标追击、兜底待机；
    /// 换 AI = 换树，不改框架）</summary>
    private DecisionSelector<Entity, EnumAIIntent> DecisionTree = null!;

    private EnumAIIntent Intent = EnumAIIntent.Idle;   // 当前意图（决策低频刷新）
    private float DecisionTimer;                        // 决策定时累积器
    private bool Bound;                                 // Brain 绑定标志

    /// <summary>Brain 绑定/解绑（BindInputSource 调用）。解绑 = GatherCommands 早退（沉默站桩）</summary>
    public void Activate()
    {
        Bound = true;
    }

    public void Deactivate()
    {
        Bound = false;
    }

    /// <summary>每帧采集：决策定时 → 意图翻译成指令（追击 = 朝目标方向，与玩家推杆同构）</summary>
    public void GatherCommands(CommandBuffer commands)
    {
        if (!Bound)
        {
            return;
        }
        if (Entity == null)
        {
            Entity = GetComponentInParent<Entity>();
            if (Entity == null)
            {
                return;
            }
            BuildDecisionTree();
        }

        // 防身：被附身驱动的实体（身上挂着附身载体 buff）不该再被 AI 写入指令。
        // 正常路径下会话期间本源的 Bound 已为 false（换绑时被 Deactivate），这里是双保险——
        // 万一绑定状态被外部绕过，也不会出现"AI 与玩家同时开这具身体"
        if (Entity.Brain.InputSource is PlayerInputSource
            && Entity.Brain.Modifiers.GetHeld<IPossessionEffect>()?.PossessionRole == EnumPossessionRole.Possessed)
        {
            return;
        }

        TryRunDecision(Time.deltaTime);

        // 意图翻译（每帧）：决策低频产出意图，指令连续刷新——玩家推杆与 AI 追击在此汇成同一种指令。
        // 无 else：帧首已把 MoveDirection 重置为零——输入源沉默 = 站桩
        if (Intent == EnumAIIntent.Chase && Target != null)
        {
            // 朝目标的方向（投影到水平面），限幅 1；方向同时供 RotateTowards 转身。
            // 演示用直线追击，无绕障/寻路
            Vector3 toTarget = Target.position - transform.position;
            toTarget.y = 0f;
            commands.MoveDirection = Vector3.ClampMagnitude(toTarget, 1f);
        }
    }

    /// <summary>决策树组装（首帧懒建；换脑子 = 改这里或做成 Inspector 配置）：
    /// 高优先级在前，最后一条恒真兜底（Selector = 优先级选择）</summary>
    private void BuildDecisionTree()
    {
        DecisionTree = new DecisionSelector<Entity, EnumAIIntent>(
            new DecisionLeaf<Entity, EnumAIIntent>(ctx => Target != null, EnumAIIntent.Chase),
            new DecisionLeaf<Entity, EnumAIIntent>(ctx => true, EnumAIIntent.Idle));
    }

    /// <summary>决策定时器：到期评估决策树；null = 本次无结论，维持原意图（防抖）。
    /// 累积器计时（网络时间纪律），间隔 <= 0 = 每帧评估</summary>
    private void TryRunDecision(float deltaTime)
    {
        if (DecisionInterval > 0f)
        {
            DecisionTimer -= deltaTime;
            if (DecisionTimer > 0f)
            {
                return;
            }
            DecisionTimer = DecisionInterval;
        }
        EnumAIIntent? decided = DecisionTree.Decide(Entity);
        if (decided.HasValue)
        {
            Intent = decided.Value;
        }
    }
}
