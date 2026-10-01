/// <summary>
/// 通用状态机：两段式 Tick——每帧先做转换判定、再执行当前状态动作。
/// 转换帧由「新状态」执行本帧移动（与"切换当帧即换速度"的手感一致），
/// 且每帧只跑一次 HandleTransitions，不做链式转换。
/// 纯 C# 类（非 MonoBehaviour）：由各实体控制器在 Awake 构造一次、Update 里 Tick。
/// </summary>
public sealed class StateMachine<TBoard> where TBoard : Blackboard
{
    public State<TBoard> CurrentState { get; private set; }

    /// <summary>初始化并进入初始状态（控制器 Awake 中调用一次）</summary>
    public void Initialize(State<TBoard> initialState)
    {
        CurrentState = initialState;
        CurrentState.Enter();
    }

    /// <summary>每帧调用：先判定转换，再执行状态动作</summary>
    public void Tick()
    {
        CurrentState.HandleTransitions();
        CurrentState.Tick();
    }

    /// <summary>切换状态：Exit 旧 → 换 → Enter 新；空或同一实例直接忽略</summary>
    public void ChangeState(State<TBoard> nextState)
    {
        if (nextState == null || nextState == CurrentState)
        {
            return;
        }

        CurrentState.Exit();
        CurrentState = nextState;
        CurrentState.Enter();
    }
}
