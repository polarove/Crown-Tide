using UnityEngine;

/// <summary>
/// NPC 控制器基类：在实体运动能力之上加"决策树 + 状态机"的 NPC 管线骨架。
/// 叶子控制器的职责：
/// - override CreateBlackboard 返回自己的黑板；
/// - override InitNpc 建状态与决策树，并调用 NpcMachine.Initialize(初始状态)；
/// - override RunDecision 评估决策树、把意图写进黑板；
/// - 自己声明 Update（基类不写魔术方法，写了会被叶子静默隐藏），推荐管线：
///   同步感知 → UpdateDecision() → UpdateGroundCheck() → NpcMachine.Tick()
///   → ApplyGravityAndVerticalMove() → ApplyRotation()。
/// 决策按 decisionInterval 低频评估：省性能，也避免条件抖动导致行为闪烁。
/// </summary>
public abstract class NpcController<TBoard> : EntityController where TBoard : NpcBlackboard
{
    [Header("决策")]
    [Tooltip("决策树评估间隔（秒）：越小反应越快、开销越大；0 = 每帧评估")]
    public float decisionInterval = 0.2f;

    private float nextDecisionTime;

    /// <summary>类型化黑板（即基类 Board；由 CreateBlackboard 构造，恒为 TBoard）</summary>
    protected TBoard NpcBoard => (TBoard)Board;

    /// <summary>NPC 状态机（InitNpc 里建状态后调用 NpcMachine.Initialize）</summary>
    protected StateMachine<TBoard> NpcMachine { get; private set; }

    protected sealed override void InitEntity()
    {
        NpcMachine = new StateMachine<TBoard>();
        InitNpc();
    }

    /// <summary>NPC 初始化：建状态、决策树，并 NpcMachine.Initialize(初始状态)</summary>
    protected abstract void InitNpc();

    /// <summary>评估决策树，把意图写进黑板（按 decisionInterval 定时调用）</summary>
    protected abstract void RunDecision();

    /// <summary>决策定时器：到期返回 true 并立即执行 RunDecision；叶子 Update 每帧调用</summary>
    protected bool UpdateDecision()
    {
        if (Time.time < nextDecisionTime)
        {
            return false;
        }
        nextDecisionTime = decisionInterval <= 0f ? Time.time : Time.time + decisionInterval;
        RunDecision();
        return true;
    }
}
