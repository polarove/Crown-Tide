using System;

/// <summary>事件实例对应一次已确认的玩法结算；同一实例重复 Invoke 不会重复计数。</summary>
public interface ICrownEvent
{
    bool Invoke();
}

public interface ITideEvent
{
    bool Invoke();
}

/// <summary>Logic 写入 Data；不使用全局总线，也不根据玩家/敌怪类型分支。</summary>
public abstract class FaithEvent
{
    private readonly Entity Target;
    private readonly int Delta;
    private bool Invoked;

    protected FaithEvent(Entity target, int amount, int direction)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        Target = target;
        Delta = direction * amount;
    }

    public bool Invoke()
    {
        if (Invoked || Target == null || Target.IsDead || Target.Vitals.Faith == null || Delta == 0)
            return false;
        Invoked = true;
        Target.Vitals.Faith.Update(Delta);
        return true;
    }
}

public sealed class CrownEvent : FaithEvent, ICrownEvent
{
    public CrownEvent(Entity target, int amount) : base(target, amount, 1) { }
}

public sealed class TideEvent : FaithEvent, ITideEvent
{
    public TideEvent(Entity target, int amount) : base(target, amount, -1) { }
}
