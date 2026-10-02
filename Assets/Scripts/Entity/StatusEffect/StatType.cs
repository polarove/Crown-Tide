/// <summary>
/// 数值修饰的统计类型：效果容器按"乘法链"聚合——无修饰 = 1，全部活跃条目相乘，
/// Stack 条目按层数自乘（指数叠加）。
/// 消费读点（谁在读哪个乘数）：MoveSpeed → NpcStateBase.ApplyLocomotion、
/// JumpPower → NpcController.TryConsumeJump、AttackSpeed → NpcAttackState 攻击时长、
/// DamageTaken → NpcController.TakeDamage、DamageDealt → M3 命中入口（本轮只挂账不读）。
/// </summary>
public enum StatType
{
    MoveSpeed = 0,
    JumpPower,
    AttackSpeed,
    DamageDealt,
    DamageTaken,
}
