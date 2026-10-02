/// <summary>
/// 数值修饰的统计类型：ModifierList 按"乘法链"聚合——无修饰 = 1，全部活跃条目相乘，
/// Stack 条目按层数自乘（指数叠加）。
/// 消费读点（谁在读哪个乘数；基础值分裂在 Motor/Config/Sheet 的位置一并登记）：
/// - MoveSpeed（基础 = EntityMotor.WalkSpeed × WeaponComboGraph.moveSpeedMultiplier）→ Locomotion 状态 ApplyLocomotion
/// - JumpPower（基础 = EntityMotor.JumpHeight）→ EntityBrain.TryConsumeJump
/// - AttackSpeed（基础 = WeaponSO.attackSpeed × 出招表段时长）→ AttackState 段计时
/// - DamageTaken（基础 = 1）→ EntityBrain.TakeDamage 入口
/// - DamageDealt（基础 = 1）→ M3 命中入口（本轮只挂账不读）
/// </summary>
public enum EnumStatType
{
    MoveSpeed = 0,
    JumpPower,
    AttackSpeed,
    DamageDealt,
    DamageTaken,
}
