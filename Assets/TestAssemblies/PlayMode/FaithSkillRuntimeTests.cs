using Assets.Scripts.Entity.Data;
using Assets.Scripts.Entity.Data.Armor;
using Assets.Scripts.Entity.Data.Skill;
using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class FaithSkillRuntimeTests
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

    private Entity Actor(string name)
    {
        var go = new GameObject(name);
        Created.Add(go);
        var entity = go.AddComponent<Entity>();
        entity.Config = Asset<CharacterConfigSO>();
        entity.Vitals.Initialize(entity.Config);
        return entity;
    }

    private SkillSO Skill(EnumSkillType kind)
    {
        SkillSO skill = Asset<SkillSO>();
        skill.Kind = kind;
        if (kind == EnumSkillType.Crown) Host.Slots.Skills.Crown = skill;
        else Host.Slots.Skills.Tide = skill;
        return skill;
    }

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        Host = Actor("FaithHost");
        Target = Actor("FaithTarget");
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        for (int i = Created.Count - 1; i >= 0; i--)
            if (Created[i] != null) Object.Destroy(Created[i]);
        Created.Clear();
        yield return null;
    }

    [UnityTest]
    public IEnumerator 达到满值不自动释放_请求保留强化快照并归零()
    {
        SkillSO skill = Skill(EnumSkillType.Crown);
        ModifierEffect normal = Asset<ModifierEffect>();
        ModifierEffect burst = Asset<ModifierEffect>();
        skill.Effects = new[] { normal };
        skill.BurstEffects = new[] { burst };
        new CrownEvent(Host, 67).Invoke();
        yield return null;
        Assert.AreEqual(67, Host.Vitals.Faith!.Current);
        Assert.IsNull(Host.Brain.LastSkillCast);
        Host.Commands.SkillSlotQueued = (int)EnumSkillType.Crown;
        yield return null;
        Assert.AreEqual(0, Host.Vitals.Faith.Current);
        Assert.AreEqual(67, Host.Brain.LastSkillCast!.Value.FaithBeforeCast);
        Assert.IsTrue(Host.Brain.LastSkillCast.Value.IsBurst);
        Assert.IsTrue(Host.Brain.Modifiers.IsHolding(burst));
        Assert.IsFalse(Host.Brain.Modifiers.IsHolding(normal));
        Assert.AreEqual(0, Host.Commands.SkillSlotQueued);
    }

    [Test]
    public void 普通释放与潮汐强化回退共用Effect()
    {
        SkillSO skill = Skill(EnumSkillType.Tide);
        ModifierEffect normal = Asset<ModifierEffect>();
        skill.Effects = new[] { normal };
        Host.Vitals.Faith!.Update(-33);
        Assert.IsTrue(Host.Brain.TryCastSkill(EnumSkillType.Tide, out var cast));
        Assert.IsFalse(cast.IsBurst);
        Assert.IsTrue(Host.Brain.Modifiers.IsHolding(normal));
        Host.Slots.Skills.TickCooldown(5f);
        Host.Vitals.Faith.Update(-67);
        Assert.IsTrue(Host.Brain.TryCastSkill(EnumSkillType.Tide, out cast));
        Assert.IsTrue(cast.IsBurst);
        Assert.AreEqual(-67, cast.FaithBeforeCast);
        Assert.AreEqual(0, Host.Vitals.Faith.Current);
    }

    [Test]
    public void 失控门禁拒绝_显式解控技能仍能释放()
    {
        SkillSO skill = Skill(EnumSkillType.Crown);
        ModifierEffect stun = Asset<ModifierEffect>();
        stun.HasControl = true;
        stun.Category = EnumModifierCategory.Debuff;
        Host.Brain.Modifiers.Apply(stun);
        Host.Vitals.Faith!.Update(33);
        Assert.IsFalse(Host.Brain.TryCastSkill(EnumSkillType.Crown, out _));
        Assert.AreEqual(33, Host.Vitals.Faith.Current);
        Assert.AreEqual(0f, Host.Slots.Skills.CrownCooldownRemaining);
        skill.AllowWhileControlled = true;
        skill.DispelOnCast = EnumModifierCategory.Debuff;
        Assert.IsTrue(Host.Brain.TryCastSkill(EnumSkillType.Crown, out _));
        Assert.IsFalse(Host.Brain.Modifiers.HasControlActive);
        Assert.AreEqual(0, Host.Vitals.Faith.Current);
    }

    [Test]
    public void 无效效果与死亡请求不消费()
    {
        SkillSO skill = Skill(EnumSkillType.Crown);
        skill.Effects = new ModifierEffect[] { null! };
        Host.Vitals.Faith!.Update(67);
        Assert.IsFalse(Host.Brain.TryCastSkill(EnumSkillType.Crown, out _));
        Assert.AreEqual(67, Host.Vitals.Faith.Current);
        Assert.AreEqual(0f, Host.Slots.Skills.CrownCooldownRemaining);
        Host.Brain.TakeDamage(999f);
        Assert.IsFalse(Host.Brain.TryCastSkill(EnumSkillType.Crown, out _));
        Assert.IsNull(Host.Brain.LastSkillCast);
    }

    [Test]
    public void 事件方向隔离_同实例去重_死亡不结算()
    {
        ICrownEvent gain = new CrownEvent(Host, 12);
        Assert.IsTrue(gain.Invoke());
        Assert.IsFalse(gain.Invoke());
        ITideEvent loss = new TideEvent(Host, 5);
        Assert.IsTrue(loss.Invoke());
        Assert.IsFalse(loss.Invoke());
        Assert.AreEqual(7, Host.Vitals.Faith!.Current);
        Assert.AreEqual(0, Target.Vitals.Faith!.Current);
        Target.Brain.TakeDamage(999f);
        Assert.IsFalse(new CrownEvent(Target, 5).Invoke());
    }

    [Test]
    public void 无效与零实际伤害不降信心_受击通过事件降低()
    {
        Host.Brain.TakeDamage(0f);
        Host.Brain.TakeDamage(float.NaN);
        Assert.AreEqual(100f, Host.Vitals.CurrentHp);
        ModifierEffect immune = Asset<ModifierEffect>();
        immune.StatModifiers = new[] { new StatModifierEntry { Stat = EnumStatType.DamageTaken, Multiplier = 0f } };
        Host.Brain.Modifiers.Apply(immune);
        Host.Brain.TakeDamage(10f);
        Assert.AreEqual(0, Host.Vitals.Faith!.Current);
        Host.Brain.Modifiers.Remove(immune);
        Host.Brain.TakeDamage(10f);
        Assert.AreEqual(-5, Host.Vitals.Faith.Current);
    }

    [Test]
    public void Debuff显式周期结算_跳伤不重复当受击_摘除停止()
    {
        ModifierEffect poison = Asset<ModifierEffect>();
        poison.Duration = 0f;
        poison.HasPeriodic = true;
        poison.DamagePerTick = 1f;
        poison.TickInterval = 1f;
        poison.FaithDeltaPerTick = -3;
        poison.FaithTickInterval = 1f;
        Host.Brain.Modifiers.Apply(poison);
        Host.Brain.Modifiers.Tick(2f);
        Assert.AreEqual(98f, Host.Vitals.CurrentHp);
        Assert.AreEqual(-6, Host.Vitals.Faith!.Current);
        Host.Brain.Modifiers.Remove(poison);
        Host.Brain.Modifiers.Tick(2f);
        Assert.AreEqual(-6, Host.Vitals.Faith.Current);
        ModifierEffect marker = Asset<ModifierEffect>();
        marker.Category = EnumModifierCategory.Buff;
        marker.Duration = 0f;
        Host.Brain.Modifiers.Apply(marker);
        Host.Brain.Modifiers.Tick(100f);
        Assert.AreEqual(-6, Host.Vitals.Faith.Current, "类别与永久标记不自动触发信心事件");
    }

    [Test]
    public void 潮汐授予冠冕吸血_按实际损血治疗_普通攻击不吸血_到期停止()
    {
        SkillSO tide = Skill(EnumSkillType.Tide);
        ModifierEffect steal = Asset<ModifierEffect>();
        steal.Duration = 3f;
        steal.CrownLifeStealRatio = .5f; // 测试样本，不是正式平衡值。
        tide.AfterTideEffects = new[] { steal };
        Host.Brain.TakeDamage(50f, countsAsHit: false);
        Host.Brain.ResolveHit(Target, 10f, true, EnumSkillType.Crown);
        Assert.AreEqual(50f, Host.Vitals.CurrentHp, "平时不吸血");
        Host.Vitals.Faith!.Update(-33);
        Assert.IsTrue(Host.Brain.TryCastSkill(EnumSkillType.Tide, out _));
        Host.Brain.ResolveHit(Target, 10f, true, EnumSkillType.Crown);
        Assert.AreEqual(55f, Host.Vitals.CurrentHp);
        Host.Brain.ResolveHit(Target, 10f, true);
        Assert.AreEqual(55f, Host.Vitals.CurrentHp);
        Host.Brain.ResolveHit(Target, 1000f, true, EnumSkillType.Crown);
        Assert.AreEqual(90f, Host.Vitals.CurrentHp, "目标只剩 70 HP，不能按溢出伤害治疗");
        Host.Brain.Modifiers.Tick(3f);
        Assert.AreEqual(0f, Host.Brain.Modifiers.GetCrownLifeStealRatio());
    }

    [Test]
    public void 命中击杀事件读配置_友伤仅降低目标信心()
    {
        Host.Config!.FaithGainPerEnemyHit = 2;
        Host.Config.FaithGainPerEnemyKill = 9;
        Host.Brain.ResolveHit(Target, 10f, false);
        Assert.AreEqual(0, Host.Vitals.Faith!.Current);
        Assert.AreEqual(-5, Target.Vitals.Faith!.Current);
        Host.Brain.ResolveHit(Target, 1000f, true);
        Assert.AreEqual(11, Host.Vitals.Faith.Current);
        Assert.AreEqual(0f, Host.Brain.ResolveHit(Target, 5f, true));
        Assert.AreEqual(11, Host.Vitals.Faith.Current);
    }

    [Test]
    public void 武器联动逐档叠加_不计入件数_卸下与破套移除()
    {
        ArmorSetSO set = Asset<ArmorSetSO>();
        var tier2 = Asset<ModifierEffect>();
        var tier4 = Asset<ModifierEffect>();
        set.Bonuses = new[] { new ArmorSetBonus { PieceCount = 2, Modifier = tier2 }, new ArmorSetBonus { PieceCount = 4, Modifier = tier4 } };
        var weapon2 = Asset<ModifierEffect>();
        var weapon4 = Asset<ModifierEffect>();
        var weapon = Asset<WeaponSO>();
        weapon.Set = set;
        weapon.SetBonuses = new[] { new ArmorSetBonus { PieceCount = 2, Modifier = weapon2 }, new ArmorSetBonus { PieceCount = 4, Modifier = weapon4 } };
        Assert.AreEqual(EnumEquipResult.Success, Host.Slots.TryEquipWeapon(weapon, EnumHandSlotType.MainHand, 5));
        Assert.IsFalse(Host.Brain.Modifiers.IsHolding(weapon2));
        var parts = new[] { EnumArmorPart.Head, EnumArmorPart.Chest, EnumArmorPart.Legs, EnumArmorPart.Feet };
        for (int i = 0; i < parts.Length; i++)
        {
            ArmorSO armor = Asset<ArmorSO>();
            armor.Set = set;
            armor.Part = parts[i];
            Host.Slots.TryEquipArmor(armor);
            Assert.AreEqual(i >= 1, Host.Brain.Modifiers.IsHolding(weapon2));
            Assert.AreEqual(i == 3, Host.Brain.Modifiers.IsHolding(weapon4));
        }
        Host.Brain.Modifiers.Tick(10f);
        Assert.IsTrue(Host.Brain.Modifiers.IsHolding(tier2), "装备来源按永久实例施加");
        Assert.IsTrue(Host.Brain.Modifiers.IsHolding(weapon4));
        Assert.AreEqual(3f, weapon4.Duration, "不能改共享模板的时长");
        Host.Slots.UnequipArmor(EnumArmorPart.Feet);
        Assert.IsTrue(Host.Brain.Modifiers.IsHolding(weapon2));
        Assert.IsFalse(Host.Brain.Modifiers.IsHolding(weapon4));
        Host.Slots.UnequipWeapon(EnumHandSlotType.MainHand);
        Assert.IsFalse(Host.Brain.Modifiers.IsHolding(weapon2));
        Assert.IsTrue(Host.Brain.Modifiers.IsHolding(tier2));
    }
}
