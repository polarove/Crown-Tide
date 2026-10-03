using UnityEngine;

/// <summary>Logic 根据装备选择第三槽技能，经数据接口提交；不写共享模板，不重置冷却。</summary>
public static class WeaponSkillBinding
{
    public static void Sync(Entity entity)
    {
        if (entity == null) return;
        WeaponSlot weapons = entity.Slots.Weapons;
        WeaponSO? weapon = weapons.MainHand != null ? weapons.MainHand : weapons.OffHand;
        SkillSO? skill = weapon != null ? weapon.SpecialSkill : entity.Slots.UnarmedWeaponSkill;
        entity.Slots.Skills.SetWeaponSkill(skill);
    }
}
