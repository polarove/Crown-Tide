using UnityEngine;

/// <summary>双侧运行时关联与控制权归还；唯一倒计时来自目标 ModifierList。</summary>
internal sealed class PossessionSession
{
    internal readonly Entity Origin;
    internal readonly Entity Target;
    internal readonly ModifierEffect Carrier;
    internal readonly ModifierEffect Marker;
    internal readonly IInputSource? OriginalInput;
    private readonly IInputSource? TargetInput;
    private bool Ended;
    private readonly float InitialAmount;

    internal PossessionSession(Entity origin, Entity target, PossessionProfileSO profile, IInputSource? originalInput, float initialAmount)
    {
        InitialAmount = initialAmount;
        Origin = origin;
        Target = target;
        Carrier = profile.PossessedEffect!;
        Marker = profile.SoulOutEffect!;
        OriginalInput = originalInput;
        TargetInput = target.Brain.InputSource;
    }

    internal float Remaining => !Ended && Target != null && Carrier != null
        ? Target.Brain.Modifiers.GetRemaining(Carrier) : 0f;

    internal bool IsValid => !Ended && Origin != null && Target != null
        && Origin.isActiveAndEnabled && Target.isActiveAndEnabled
        && Origin.Brain != null && Target.Brain != null
        && Origin.Brain.isActiveAndEnabled && Target.Brain.isActiveAndEnabled
        && !Origin.IsDead && Origin.IsPossessing && Target.IsPossessed && Target.Vitals.CurrentGreenHp > 0f
        && Marker != null && Carrier != null
        && Origin.Brain.Modifiers.IsHolding(Marker) && Target.Brain.Modifiers.IsHolding(Carrier);

    internal bool IsTicking => (Origin != null && Origin.Brain != null && Origin.Brain.Modifiers.IsTicking)
        || (Target != null && Target.Brain != null && Target.Brain.Modifiers.IsTicking);

    internal void Start()
    {
        Origin.ActivePossession = Target.ActivePossession = this;
        Origin.Brain.Modifiers.Apply(Marker);
        Target.Vitals.SetGreenHealth(InitialAmount);
        Target.Brain.Modifiers.Apply(Carrier, InitialAmount / 1000f);
        Origin.Brain.StateMachine.ChangeState(new EntityPossessingState(Origin, Origin.Brain.StateMachine));
        Target.Brain.StateMachine.ChangeState(new EntityPossessedState(Target, Target.Brain.StateMachine));
        // AI → AI 绑定可能同源，也必须取消旧命中段，不能让本次扇形继续占用其他身体。
        Origin.Brain.StateMachine.ClearState(EnumStateLayer.Action);
        // 本地玩家使用目标自身输入/相机；AI 使用目标自身 AI，不占用本地设备。
        bool player = OriginalInput is PlayerInputSource;
        if (player && Origin.Brain.PlayerSource != null && Target.Brain.PlayerSource != null)
            Target.Brain.PlayerSource.InheritDevices(Origin.Brain.PlayerSource);
        Origin.Brain.BindInputSource(Origin.Brain.AiSource);
        Target.Brain.BindInputSource(player ? Target.Brain.PlayerSource : Target.Brain.AiSource, preserveAction: true);
    }

    internal void End()
    {
        if (Ended || IsTicking) return; // 周期结算后的 Brain 接缝再清理，禁止修改迭代中的列表。
        Ended = true;
        Release(Target, Carrier, TargetInput);
        Release(Origin, Marker, OriginalInput); // 原角色死亡也返回原相机，行动仍由 CanOperate 拦截。
    }

    private void Release(Entity entity, ModifierEffect effect, IInputSource? source)
    {
        if (entity == null || entity.Brain == null) return;
        if (!ReferenceEquals(entity.ActivePossession, this)) return;
        entity.ActivePossession = null;
        entity.Brain.StateMachine.ClearState(EnumStateLayer.Possession);
        if (effect != null) entity.Brain.Modifiers.Remove(effect);
        entity.Brain.BindInputSource(source, preserveAction: true);
    }
}

/// <summary>击杀后的逻辑接缝；资格为纯查询，实际 Buff / 状态 / 输入交接在执行阶段。</summary>
public static class PossessionLogic
{
    internal static bool TryBeginKilled(Entity attacker, Entity target, PossessionProfileSO profile, float initialAmount)
    {
        if (initialAmount <= 0f || float.IsNaN(initialAmount) || float.IsInfinity(initialAmount)
            || initialAmount / 1000f <= 0f) return false;
        if (!CanReceiveKill(attacker, target, profile)) return false;
        attacker.Brain.StateMachine.ClearState(EnumStateLayer.Action); // 终止本次施放者的命中段，包括连续附身载体。
        PossessionSession? previous = attacker.ActivePossession;
        Entity origin = previous != null ? previous.Origin : attacker;
        IInputSource? source = previous != null ? previous.OriginalInput : attacker.Brain.InputSource;
        // 连续技能击杀更换载体，仍归还最初本体；不额外禁止被附身身体释放 V。
        if (previous != null)
        {
            previous.End();
            if (attacker.ActivePossession != null) return false;
        }
        new PossessionSession(origin, target, profile, source, initialAmount).Start();
        return true;
    }

    private static bool CanReceiveKill(Entity attacker, Entity target, PossessionProfileSO profile)
    {
        if (attacker == null || target == null || attacker == target || profile == null || !profile.IsValid
            || !attacker.CanOperate || !target.IsDead || !target.isActiveAndEnabled
            || !attacker.Brain.isActiveAndEnabled || !target.Brain.isActiveAndEnabled
            || target.ActivePossession != null || attacker.IsPossessing
            || target.Brain.Modifiers.IsTicking || attacker.Brain.Modifiers.IsTicking) return false;
        PossessionSession? current = attacker.ActivePossession;
        Entity origin = current != null ? current.Origin : attacker;
        if (origin == null || origin.IsDead || !origin.isActiveAndEnabled || origin == target) return false;
        if (attacker.Brain.InputSource is PlayerInputSource && target.Brain.PlayerSource == null) return false;
        if (attacker.Brain.InputSource is not PlayerInputSource && attacker.Brain.InputSource is not AITreeInputSource) return false;
        return target.Brain.AiSource != null && origin.Brain.AiSource != null
            && !target.Brain.Modifiers.IsHolding(profile.PossessedEffect!);
    }

    public static void Reconcile(Entity entity)
    {
        PossessionSession? session = entity != null ? entity.ActivePossession : null;
        if (session != null && !session.IsValid) session.End();
    }

    internal static void OnUnavailable(Entity entity)
    {
        if (entity != null && entity.ActivePossession != null) entity.ActivePossession.End();
    }
}
