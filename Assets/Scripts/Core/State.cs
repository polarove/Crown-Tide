/// <summary>
/// 状态基类：持有黑板与状态机引用，所有状态实例在控制器 Awake 构造一次（运行期零 new）。
/// Enter/Exit 是动画/特效的预留接口，保持为空——不得放移动逻辑，否则转换帧会双重移动。
/// </summary>
public abstract class State<TBoard> where TBoard : Blackboard
{
    public abstract string StateName { get; }       // 调试显示用

    protected TBoard Board { get; }
    protected StateMachine<TBoard> Machine { get; }

    protected State(TBoard board, StateMachine<TBoard> machine)
    {
        Board = board;
        Machine = machine;
    }

    /// <summary>进入状态时调用（动画/特效预留）</summary>
    public virtual void Enter() { }

    /// <summary>离开状态时调用（动画/特效预留）</summary>
    public virtual void Exit() { }

    /// <summary>纯转换判定，只允许调用 Machine.ChangeState，不做移动</summary>
    public abstract void HandleTransitions();

    /// <summary>本帧动作：选速度 + 调控制器的共享运动接口</summary>
    public abstract void Tick();
}
