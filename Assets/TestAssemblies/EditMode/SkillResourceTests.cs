using Assets.Scripts.Entity.Data.Skill;
using NUnit.Framework;
using UnityEngine;

public sealed class SkillResourceTests
{
    private GameObject Host = null!;
    private CharacterVitals Vitals = null!;
    private SkillSO Crown = null!;
    private SkillSO Tide = null!;
    private SkillSlot Slots = null!;

    [SetUp]
    public void SetUp()
    {
        Host = new GameObject("FaithDataTest");
        Vitals = Host.AddComponent<CharacterVitals>();
        Vitals.Initialize(null);
        Crown = ScriptableObject.CreateInstance<SkillSO>();
        Tide = ScriptableObject.CreateInstance<SkillSO>();
        Tide.Kind = EnumSkillType.Tide;
        Slots = new SkillSlot { Crown = Crown, Tide = Tide };
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(Host);
        Object.DestroyImmediate(Crown);
        Object.DestroyImmediate(Tide);
    }

    [TestCase(-67, false, true, false, true)]
    [TestCase(-66, false, true, false, false)]
    [TestCase(-33, false, true, false, false)]
    [TestCase(-32, false, false, false, false)]
    [TestCase(0, false, false, false, false)]
    [TestCase(32, false, false, false, false)]
    [TestCase(33, true, false, false, false)]
    [TestCase(66, true, false, false, false)]
    [TestCase(67, true, false, true, false)]
    public void 信心阈值与强化边界(int value, bool crown, bool tide, bool crownBurst, bool tideBurst)
    {
        Vitals.Faith!.Update(value);
        Assert.AreEqual(crown, Slots.CanCast(EnumSkillType.Crown, Vitals.Faith));
        Assert.AreEqual(tide, Slots.CanCast(EnumSkillType.Tide, Vitals.Faith));
        Assert.AreEqual(crownBurst, Slots.IsBurstReady(EnumSkillType.Crown, Vitals.Faith));
        Assert.AreEqual(tideBurst, Slots.IsBurstReady(EnumSkillType.Tide, Vitals.Faith));
    }

    [TestCase(EnumSkillType.Crown, 67)]
    [TestCase(EnumSkillType.Tide, -67)]
    public void 成功归零并独立启动冷却(EnumSkillType kind, int value)
    {
        Vitals.Faith!.Update(value);
        Assert.IsTrue(Slots.Consume(kind, Vitals.Faith));
        Assert.AreEqual(0, Vitals.Faith.Current);
        Assert.AreEqual(kind == EnumSkillType.Crown ? 5f : 0f, Slots.CrownCooldownRemaining);
        Assert.AreEqual(kind == EnumSkillType.Tide ? 5f : 0f, Slots.TideCooldownRemaining);
        Vitals.Faith.Update(value);
        Assert.IsFalse(Slots.Consume(kind, Vitals.Faith));
        Assert.AreEqual(value, Vitals.Faith.Current);
        Slots.TickCooldown(5f);
        Assert.IsTrue(Slots.CanCast(kind, Vitals.Faith));
    }

    [Test]
    public void 失败不消费_非法种类_错位配置_空槽_无资源()
    {
        Vitals.Faith!.Update(67);
        Assert.IsFalse(Slots.Consume((EnumSkillType)2, Vitals.Faith));
        Assert.IsFalse(Slots.Consume(EnumSkillType.Crown, null));
        Crown.Kind = EnumSkillType.Tide;
        Assert.IsFalse(Slots.Consume(EnumSkillType.Crown, Vitals.Faith));
        Slots.Crown = null;
        Assert.IsFalse(Slots.Consume(EnumSkillType.Crown, Vitals.Faith));
        Assert.AreEqual(67, Vitals.Faith.Current);
        Assert.AreEqual(0f, Slots.CrownCooldownRemaining);
    }

    [Test]
    public void 极端增量钳制与构造初始值()
    {
        Vitals.Faith!.Update(int.MaxValue);
        Assert.AreEqual(67, Vitals.Faith.Current);
        Vitals.Faith.Update(int.MaxValue);
        Assert.AreEqual(67, Vitals.Faith.Current);
        Vitals.Faith.Update(int.MinValue);
        Assert.AreEqual(-67, Vitals.Faith.Current);
        Assert.AreEqual(67, new SkillResource(Vitals, int.MaxValue).Current);
        Assert.AreEqual(-67, new SkillResource(Vitals, int.MinValue).Current);
    }

    [Test]
    public void 阈值读取配置而非固定增量()
    {
        Crown.FaithThreshold = 40;
        Vitals.Faith!.Update(33);
        Assert.IsFalse(Slots.CanCast(EnumSkillType.Crown, Vitals.Faith));
        Vitals.Faith.Update(7);
        Assert.IsTrue(Slots.CanCast(EnumSkillType.Crown, Vitals.Faith));
        Crown.Cooldown = float.NaN;
        Assert.IsFalse(Slots.CanCast(EnumSkillType.Crown, Vitals.Faith));
    }
}
