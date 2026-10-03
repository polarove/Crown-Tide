/// <summary>正交附身身份；仅记录状态，效果、计时和输入交接由 Logic 管理。</summary>
public sealed class EntityPossessedState : EntityState
{
    public EntityPossessedState(Entity entity, EntityStateMachine machine) : base(entity, machine) { }
    public override EnumStateLayer Layer => EnumStateLayer.Possession;
    public override string StateName => "被附身";
    public override void HandleTransitions() { }
    public override void Tick(float deltaTime) { }
}

public sealed class EntityPossessingState : EntityState
{
    public EntityPossessingState(Entity entity, EntityStateMachine machine) : base(entity, machine) { }
    public override EnumStateLayer Layer => EnumStateLayer.Possession;
    public override string StateName => "附身中";
    public override void HandleTransitions() { }
    public override void Tick(float deltaTime) { }
}
