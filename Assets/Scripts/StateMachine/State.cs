/// <summary>
/// 状态基类：持有黑板与状态机引用，所有状态实例在控制器 Awake 构造一次（运行期零 new）。
/// Enter/Exit 是动画/特效的预留接口，保持为空——不得放移动逻辑，否则转换帧会双重移动。
/// 每个状态属于一个层（Layer）：同层互斥、异层叠加，详见 StateLayer。
/// </summary>
public abstract class State<TBoard> where TBoard : Blackboard
{
    /// <summary>本状态所属的层（同层互斥、异层叠加）</summary>
    public abstract StateLayer Layer { get; }

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

    /// <summary>动作型霸体：本状态活跃期间免疫 CrowdControl（眩晕/击退等）的进入。
    /// 默认 false，需要的状态覆写（如重击、技能演出）。随状态生命周期自动生效/失效，不会忘关。
    /// 注意：霸体是免疫不是解控——已生效的 CC 不因此清除；挡不挡伤害由命中入口决定（通常不挡，只挡打断）。
    /// 同族修饰（无敌帧 GrantsInvincibility 等）都走这个模式：状态声明能力，外部系统在入口仲裁</summary>
    public virtual bool GrantsSuperArmor => false;

    /// <summary>声明"本状态活跃期间锁定移动"（攻击/闪避/吟唱等主动动作）。
    /// Locomotion 层消费此声明：站桩（方向清零 + 零速 Move 保留贴地），状态退出自动恢复。
    /// 与 GrantsSuperArmor 同模式：状态声明需求，别处仲裁</summary>
    public virtual bool LocksMovement => false;

    /// <summary>离开状态时调用（动画/特效预留）</summary>
    public virtual void Exit() { }

    /// <summary>纯转换判定，只允许调用 Machine.ChangeState，不做移动</summary>
    public abstract void HandleTransitions();

    /// <summary>本帧动作：选速度 + 调控制器的共享运动接口</summary>
    public abstract void Tick();
}
