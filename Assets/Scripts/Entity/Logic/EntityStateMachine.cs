using System;
using UnityEngine;

/// <summary>
/// 分层状态机（Logic 层，纯 C#，玩法路径零分配）：每层（EnumStateLayer）一个活跃状态，
/// 同层互斥、异层叠加。旧 StateMachine 非泛型化平移（唯一机器类型，Entity 直连），语义逐条保真：
/// - 两遍 Tick：每帧先做全部层的转换判定、再执行全部层的动作——转换帧由"新状态"执行本帧动作
///   （与"切换当帧即换速度"的手感一致），每层不做链式转换；
/// - 压制规则（EnumStateLayerRules）：失控层活跃时其余层冻结（不转换、不执行）但状态保留，
///   解除后自动恢复；压制判定在轮到每层时惰性求值，保证解除当帧即恢复；
/// - 未激活的层（Active 为 null，如不跳跃实体的 Aerial 层）直接跳过。
/// deltaTime 全链入参（不用 Time.deltaTime——网络时间纪律）。
/// 由 EntityBrain.Bootstrap 构造一次、Brain.Update 里 TwoPassTick。
/// </summary>
public sealed class EntityStateMachine
{
    private static readonly int LayerCount = Enum.GetValues(typeof(EnumStateLayer)).Length;

    // 层槽：未激活的层为 null（合法状态），故元素类型可空
    private readonly EntityState?[] Active = new EntityState?[LayerCount];

    /// <summary>取某层当前活跃状态；层未激活返回 null</summary>
    public EntityState? GetActive(EnumStateLayer layer)
    {
        return Active[(int)layer];
    }

    /// <summary>该状态实例当前是否在其所属的层上活跃</summary>
    public bool IsActive(EntityState state)
    {
        return state != null && Active[(int)state.Layer] == state;
    }

    /// <summary>
    /// 初始化并进入各层初始状态（Brain.Bootstrap 调用一次）。
    /// 每层至多一个初始状态；同层重复传入时保留先者并警告。
    /// </summary>
    public void Initialize(params EntityState[] initialStates)
    {
        for (int i = 0; i < initialStates.Length; i++)
        {
            EntityState state = initialStates[i];
            int layerIndex = (int)state.Layer;
            if (Active[layerIndex] is { } occupied)
            {
                Debug.LogWarning($"状态机初始化：{state.Layer} 层已有初始状态 {occupied.StateName}，忽略 {state.StateName}");
                continue;
            }
            Active[layerIndex] = state;
            state.Enter();
        }
    }

    /// <summary>每帧调用：先判定全部层的转换，再执行全部层的动作（被压制的层两步都跳过）</summary>
    public void TwoPassTick(float deltaTime)
    {
        for (int i = 0; i < LayerCount; i++)
        {
            if (Active[i] is { } ticking && !IsSuppressed(i))
            {
                ticking.HandleTransitions();
            }
        }
        for (int i = 0; i < LayerCount; i++)
        {
            if (Active[i] is { } ticking && !IsSuppressed(i))
            {
                ticking.Tick(deltaTime);
            }
        }
    }

    /// <summary>
    /// 切换状态：作用于 nextState 所属的层（同层互斥替换，不影响其他层）。
    /// 空或该层同一实例直接忽略；否则旧状态 Exit → 换 → 新状态 Enter。
    /// </summary>
    public void ChangeState(EntityState nextState)
    {
        if (nextState == null)
        {
            return;
        }

        int layerIndex = (int)nextState.Layer;
        if (Active[layerIndex] == nextState)
        {
            return;
        }

        Active[layerIndex]?.Exit();
        Active[layerIndex] = nextState;
        nextState.Enter();
    }

    /// <summary>清空某层：当前状态 Exit 后置空（层回到未激活）。眩晕结束、攻击收招等场景使用</summary>
    public void ClearState(EnumStateLayer layer)
    {
        int layerIndex = (int)layer;
        Active[layerIndex]?.Exit();
        Active[layerIndex] = null;   // 层回到未激活（槽位元素本身可空）
    }

    /// <summary>是否有任一活跃状态声明了霸体（免疫 CrowdControl 的进入，不解除已生效的）。
    /// Capability.HasControlImmunity 的合成点之一（将来装备/Buff 霸体在那里或运算）</summary>
    public bool HasSuperArmor()
    {
        for (int i = 0; i < LayerCount; i++)
        {
            EntityState? state = Active[i];
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
            if (Active[j] != null && EnumStateLayerRules.Suppresses((EnumStateLayer)j, (EnumStateLayer)layerIndex))
            {
                return true;
            }
        }
        return false;
    }
}
