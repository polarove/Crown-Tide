

/// <summary>
/// 装备尝试的结果（SlotContainer.TryEquip* 返回；UI 提示与 AI 决策共用）
/// </summary>
public enum EnumEquipResult
{
    Success = 0,             // 装备成功
    InvalidWeapon = 1,       // 传入空引用或非武器数据
    CapacityExceeded = 2,    // 容量不足：main.handCost + secondary.handCost > weaponCapacity
    SameHandOccupied = 3,    // 同手位已有武器（先卸下再装备）
}