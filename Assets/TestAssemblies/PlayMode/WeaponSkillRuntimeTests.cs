using Assets.Scripts.Entity.Data;
using Assets.Scripts.Entity.Data.Skill;
using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class WeaponSkillRuntimeTests
{
    private readonly List<Object> Created = new();
    private Entity Host = null!;
    private Entity Target = null!;

    private T Asset<T>() where T : ScriptableObject
    {
        T asset = ScriptableObject.CreateInstance<T>();
        Created.Add(asset);
        return asset;
    }

    private Entity Actor(string name, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        Created.Add(go);
        Entity entity = go.AddComponent<Entity>();
        CharacterConfigSO config = Asset<CharacterConfigSO>();
        config.MaxHealth = 100f;
        entity.Config = config;
        entity.Vitals.Initialize(config);
        entity.Brain.enabled = false;
        return entity;
    }

    private SkillSO Skill()
    {
        SkillSO skill = Asset<SkillSO>();
        skill.Kind = EnumSkillType.Weapon;
        skill.Cooldown = 4f;
        skill.HasAttack = true;
        skill.Attack = new ComboEntry
        {
            Name = "测试拳击", Windup = .1f, Hit = .2f, Recovery = .1f,
            MeleeDamage = 10f, HitRadius = .6f, HitOffset = Vector3.forward,
            HitLayers = ~0, NextEntry = -1,
        };
        return skill;
    }

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        Host = Actor("WeaponSkillHost", new Vector3(900f, 900f, 900f));
        Target = Actor("WeaponSkillTarget", Host.transform.position + Vector3.forward * 1.3f);
        Host.Slots.UnarmedWeaponSkill = Skill();
        WeaponSkillBinding.Sync(Host);
        yield return null;
        Physics.SyncTransforms();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        for (int i = Created.Count - 1; i >= 0; i--)
            if (Created[i] != null) Object.Destroy(Created[i]);
        Created.Clear();
        yield return null;
    }

    [Test]
    public void 空手第三槽可释放_非零信心不消耗_成功独立冷却_实际命中不切换控制权()
    {
        IInputSource? source = Host.Brain.InputSource;
        Assert.AreEqual(0, Host.Vitals.Faith!.Current);
        Assert.IsFalse(Host.Brain.TryCastSkill(EnumSkillType.Weapon, out _));
        Assert.AreEqual(0f, Host.Slots.Skills.WeaponCooldownRemaining);
        Host.Vitals.Faith.Update(-1);
        Assert.IsTrue(Host.Brain.TryCastSkill(EnumSkillType.Weapon, out SkillCastResult cast));
        Assert.IsFalse(cast.IsBurst);
        Assert.AreEqual(-1, Host.Vitals.Faith.Current);
        Assert.AreEqual(4f, Host.Slots.Skills.WeaponCooldownRemaining);
        Assert.AreEqual(0f, Host.Slots.Skills.CrownCooldownRemaining);
        Assert.AreEqual(0f, Host.Slots.Skills.TideCooldownRemaining);
        Host.Brain.AttackState.Tick(.15f);
        Assert.AreEqual(Target.Vitals.MaxHp - 10f, Target.Vitals.CurrentHp);
        Assert.AreSame(source, Host.Brain.InputSource);
        Assert.IsFalse(Host.Brain.TryCastSkill(EnumSkillType.Weapon, out _));
    }

    [Test]
    public void 满信心不强化不归零_冷却结束后可再次释放()
    {
        Host.Vitals.Faith!.Update(67);
        Assert.IsTrue(Host.Brain.TryCastSkill(EnumSkillType.Weapon, out SkillCastResult cast));
        Assert.AreEqual(67, cast.FaithBeforeCast);
        Assert.IsFalse(cast.IsBurst);
        Assert.AreEqual(67, Host.Vitals.Faith.Current);
        Host.Brain.StateMachine.ClearState(EnumStateLayer.Action);
        Host.Slots.Skills.TickCooldown(3f);
        Assert.IsFalse(Host.Brain.TryCastSkill(EnumSkillType.Weapon, out _));
        Host.Slots.Skills.TickCooldown(1f);
        Assert.IsTrue(Host.Brain.TryCastSkill(EnumSkillType.Weapon, out _));
    }

    [Test]
    public void 装备卸下映射技能_副手回退_换武器保留冷却且模板不变()
    {
        Host.Slots.Skills.WeaponCooldownRemaining = 2f;
        WeaponSO main = Asset<WeaponSO>();
        main.SpecialSkill = Skill();
        WeaponSO off = Asset<WeaponSO>();
        off.SpecialSkill = Skill();
        Host.Slots.TryEquipWeapon(off, EnumHandSlotType.OffHand, 99);
        Assert.AreSame(off.SpecialSkill, Host.Slots.Skills.Weapon);
        Host.Slots.TryEquipWeapon(main, EnumHandSlotType.MainHand, 99);
        Assert.AreSame(main.SpecialSkill, Host.Slots.Skills.Weapon);
        Host.Slots.UnequipWeapon(EnumHandSlotType.MainHand);
        Assert.AreSame(off.SpecialSkill, Host.Slots.Skills.Weapon);
        Host.Slots.UnequipWeapon(EnumHandSlotType.OffHand);
        Assert.AreSame(Host.Slots.UnarmedWeaponSkill, Host.Slots.Skills.Weapon);
        Assert.AreEqual(2f, Host.Slots.Skills.WeaponCooldownRemaining);
        Assert.AreEqual(4f, main.SpecialSkill.Cooldown);
        Assert.IsNull(Host.LastSkillCast, "装备不会自动释放技能");
    }

    [Test]
    public void 失控死亡与非法配置拒绝_失败不消费冷却或信心()
    {
        Host.Vitals.Faith!.Update(-33);
        ModifierEffect stun = Asset<ModifierEffect>();
        stun.HasControl = true;
        stun.Duration = 2f;
        Host.Brain.Modifiers.Apply(stun);
        Assert.IsFalse(Host.Brain.TryCastSkill(EnumSkillType.Weapon, out _));
        Host.Brain.Modifiers.Remove(stun);
        Host.Brain.StateMachine.ClearState(EnumStateLayer.CrowdControl);
        Host.Slots.Skills.Weapon!.Cooldown = float.NaN;
        Assert.IsFalse(Host.Brain.TryCastSkill(EnumSkillType.Weapon, out _));
        Host.Slots.Skills.Weapon.Cooldown = 4f;
        ComboEntry entry = Host.Slots.Skills.Weapon.Attack;
        entry.MeleeDamage = -1f;
        Host.Slots.Skills.Weapon.Attack = entry;
        Assert.IsFalse(Host.Brain.TryCastSkill(EnumSkillType.Weapon, out _));
        Assert.AreEqual(-33, Host.Vitals.Faith.Current);
        Assert.AreEqual(0f, Host.Slots.Skills.WeaponCooldownRemaining);
        Host.Brain.TakeDamage(999f);
        Assert.IsFalse(Host.Brain.TryCastSkill(EnumSkillType.Weapon, out _));
        Assert.IsNull(Host.LastSkillCast);
    }

    [Test]
    public void 冷却每实体独立_已销毁模板不可释放()
    {
        Host.Vitals.Faith!.Update(1);
        Target.Slots.Skills.SetWeaponSkill(Host.Slots.Skills.Weapon);
        Assert.IsTrue(Host.Brain.TryCastSkill(EnumSkillType.Weapon, out _));
        Assert.AreEqual(0f, Target.Slots.Skills.WeaponCooldownRemaining);
        SkillSO skill = Target.Slots.Skills.Weapon!;
        Object.DestroyImmediate(skill);
        Assert.IsFalse(Target.Brain.TryCastSkill(EnumSkillType.Weapon, out _));
        Assert.AreEqual(0f, Target.Slots.Skills.WeaponCooldownRemaining);
    }

    [Test]
    public void 已销毁Unity输入源按空处理_回落本实体AI()
    {
        IInputSource destroyed = Target.Brain.PlayerSource!;
        Object.DestroyImmediate(Target.gameObject);
        Host.Brain.BindInputSource(destroyed);
        Assert.AreSame(Host.Brain.AiSource, Host.Brain.InputSource);
    }
}
