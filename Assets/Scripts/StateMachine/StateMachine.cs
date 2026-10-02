using System;
using UnityEngine;

/// <summary>
/// 通用分层状态机：每层（StateLayer）一个活跃状态，同层互斥、异层叠加。
/// 两段式 Tick——每帧先做全部层的转换判定、再执行全部层的动作，
/// 转换帧由「新状态」执行本帧动作（与"切换当帧即换速度"的手感一致），每层不做链式转换。
/// 压制规则（StateLayerRules）：失控层活跃时其余层冻结（不转换、不执行）但状态保留，
/// 解除后自动恢复；压制判定在轮到每层时惰性求值，保证解除当帧即恢复。
/// 未激活的层（active 为 null，如敌人的 Aerial 层）直接跳过。
/// 纯 C# 类（非 MonoBehaviour）：由各实体控制器在 Awake 构造一次、Update 里 Tick。
/// </summary>
public sealed class StateMachine<TBoard> where TBoard : Blackboard
{
    private static readonly int LayerCount = Enum.GetValues(typeof(StateLayer)).Length;

    private readonly State<TBoard>[] active = new State<TBoard>[LayerCount];

    /// <summary>取某层当前活跃状态；层未激活返回 null</summary>
    public State<TBoard> GetActive(StateLayer layer)
    {
        return active[(int)layer];
    }

    /// <summary>该状态实例当前是否在其所属层上活跃</summary>
    public bool IsActive(State<TBoard> state)
    {
        return state != null && active[(int)state.Layer] == state;
    }

    /// <summary>
    /// 初始化并进入各层初始状态（控制器 Awake 中调用一次）。
    /// 每层至多一个初始状态；同层重复传入时保留先者并警告。
    /// </summary>
    public void Initialize(params State<TBoard>[] initialStates)
    {
        foreach (State<TBoard> state in initialStates)
        {
            int layerIndex = (int)state.Layer;
            if (active[layerIndex] != null)
            {
                Debug.LogWarning($"状态机初始化：{state.Layer} 层已有初始状态 {active[layerIndex].StateName}，忽略 {state.StateName}");
                continue;
            }
            active[layerIndex] = state;
            state.Enter();
        }
    }

    /// <summary>每帧调用：先判定全部层的转换，再执行全部层的动作（被压制的层两步都跳过）</summary>
    public void Tick()
    {
        for (int i = 0; i < LayerCount; i++)
        {
            if (active[i] != null && !IsSuppressed(i))
            {
                active[i].HandleTransitions();
            }
        }
        for (int i = 0; i < LayerCount; i++)
        {
            if (active[i] != null && !IsSuppressed(i))
            {
                active[i].Tick();
            }
        }
    }

    /// <summary>
    /// 切换状态：作用于 nextState 所属的层（同层互斥替换，不影响其他层）。
    /// 空或该层同一实例直接忽略；否则旧状态 Exit → 换 → 新状态 Enter。
    /// </summary>
    public void ChangeState(State<TBoard> nextState)
    {
        if (nextState == null)
        {
            return;
        }

        int layerIndex = (int)nextState.Layer;
        if (active[layerIndex] == nextState)
        {
            return;
        }

        active[layerIndex]?.Exit();
        active[layerIndex] = nextState;
        nextState.Enter();
    }

    /// <summary>清空某层：当前状态 Exit 后置空（层回到未激活）。眩晕结束等场景使用</summary>
    public void ClearState(StateLayer layer)
    {
        int layerIndex = (int)layer;
        active[layerIndex]?.Exit();
        active[layerIndex] = null;
    }

    /// <summary>是否有任一活跃状态声明了霸体（免疫 CrowdControl 的进入，不解除已生效的）。
    /// 由战斗系统的命中/施加 CC 入口做仲裁；装备/Buff 型常驻霸体由装备系统
    /// 在控制器侧与本查询做或运算</summary>
    public bool HasSuperArmor()
    {
        for (int i = 0; i < LayerCount; i++)
        {
            State<TBoard> state = active[i];
            if (state != null && state.GrantsSuperArmor)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>层是否被压制：存在活跃的压制者层（如 CrowdControl）。轮到该层时惰性求值</summary>
    private bool IsSuppressed(int layerIndex)
    {
        for (int j = 0; j < LayerCount; j++)
        {
            if (active[j] != null && StateLayerRules.Suppresses((StateLayer)j, (StateLayer)layerIndex))
            {
                return true;
            }
        }
        return false;
    }
}
