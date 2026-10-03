using Assets.Scripts.Entity.Data.Skill;
using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class SkillAttackRuntimeTests
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

    private Entity Actor(string name, Vector3 position, int faction)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        Created.Add(go);
        Entity entity = go.AddComponent<Entity>();
        entity.Brain.enabled = false;
        entity.Config = Asset<CharacterConfigSO>();
        entity.Config.MaxHealth = 100;
        entity.Config.FactionId = faction;
        entity.Config.FaithGainPerEnemyHit = 5;
        entity.Vitals.Initialize(entity.Config);
        return entity;
    }

    private SkillSO Skill(EnumSkillType kind)
    {
        SkillSO skill = Asset<SkillSO>();
        skill.Kind = kind;
        skill.HasAttack = true;
        skill.UseBurstAttack = true;
        skill.Attack = new ComboEntry
        {
            Name = "测试技能", Windup = .1f, Hit = .2f, Recovery = .1f,
            MeleeDamage = 3f, HitRadius = .6f, HitOffset = Vector3.forward, HitLayers = ~0, NextEntry = -1,
        };
        ComboEntry burst = skill.Attack;
        burst.MeleeDamage = 6f;
        skill.BurstAttack = burst;
        if (kind == EnumSkillType.Crown) Host.Slots.Skills.Crown = skill;
        else Host.Slots.Skills.Tide = skill;
        return skill;
    }

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        Host = Actor("SkillAttacker", new Vector3(600f, 600f, 600f), 1);
        Target = Actor("SkillTarget", Host.transform.position + Vector3.forward * 1.3f, 2);
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
    public void 技能失败不转身_成功起手使用输入朝向而非后退方向()
    {
        Skill(EnumSkillType.Crown);
        Host.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        Host.Commands.LookDirection = Vector3.right;
        Host.Commands.MoveDirection = Vector3.left;
        Quaternion before = Host.transform.rotation;
        Assert.IsFalse(Host.Brain.TryCastSkill(EnumSkillType.Crown, out _));
        Assert.AreEqual(before, Host.transform.rotation);
        Assert.AreEqual(0f, Host.Slots.Skills.CrownCooldownRemaining);
        Host.Vitals.Faith!.Update(33);
        Target.transform.position = Host.transform.position + Vector3.right * 1.3f;
        Assert.IsTrue(Host.Brain.TryCastSkill(EnumSkillType.Crown, out _));
        Assert.Less(Vector3.Angle(Vector3.right, Host.transform.forward), .01f);
        Physics.SyncTransforms();
        Host.Brain.AttackState.Tick(.15f);
        Assert.AreEqual(97f, Target.Vitals.CurrentHp);
    }

    [Test]
    public void AI提供独立目标方向_空方向和非法朝向不改变身体()
    {
        AITreeInputSource ai = Host.gameObject.AddComponent<AITreeInputSource>();
        Target.transform.position = Host.transform.position + Vector3.right * 1.3f;
        ai.Target = Target.transform;
        ai.Activate();
        Host.Commands.ResetLevels();
        ai.GatherCommands(Host.Commands);
        Assert.Less(Vector3.Angle(Vector3.right, Host.Commands.LookDirection), .01f);
        Host.Brain.FaceAttackDirection();
        Quaternion before = Host.transform.rotation;
        Host.Motor.FaceDirection(Vector3.up);
        Host.Motor.FaceDirection(new Vector3(float.NaN, 0f, 0f));
        Host.Motor.FaceDirection(new Vector3(float.PositiveInfinity, 0f, 0f));
        Assert.AreEqual(before, Host.transform.rotation);
        Host.Commands.ResetLevels();
        Assert.AreEqual(Vector3.zero, Host.Commands.LookDirection, "解绑／沉默不得残留视角意图");
    }

    [TestCase(EnumSkillType.Crown, 33, false, 3f)]
    [TestCase(EnumSkillType.Crown, 67, true, 6f)]
    [TestCase(EnumSkillType.Tide, -33, false, 3f)]
    [TestCase(EnumSkillType.Tide, -67, true, 6f)]
    public void 释放归零_命中阶段执行快照_命中后产生新事件(EnumSkillType kind, int faith, bool burst, float damage)
    {
        SkillSO skill = Skill(kind);
        Host.Vitals.Faith!.Update(faith);
        Assert.IsTrue(Host.Brain.TryCastSkill(kind, out SkillCastResult cast));
        Assert.AreEqual(faith, cast.FaithBeforeCast);
        Assert.AreEqual(burst, cast.IsBurst);
        Assert.AreEqual(0, Host.Vitals.Faith.Current);
        Assert.AreEqual(Host.Brain.AttackState, Host.Brain.StateMachine.GetActive(EnumStateLayer.Action));
        // 释放后模板再改，不应改变本次已提交的攻击。
        skill.Attack = default;
        skill.BurstAttack = default;
        Host.Brain.AttackState.Tick(.05f);
        Assert.AreEqual(100f, Target.Vitals.CurrentHp);
        Host.Brain.AttackState.Tick(.1f);
        Host.Brain.AttackState.Tick(.1f);
        Assert.AreEqual(100f - damage, Target.Vitals.CurrentHp);
        Assert.AreEqual(5, Host.Vitals.Faith.Current);
        Assert.AreEqual(-5, Target.Vitals.Faith!.Current);
        Assert.IsFalse(Host.Brain.TryCastSkill(kind, out _), "动作进行中不能再释放");
    }

    [Test]
    public void 潮汐后仅冠冕命中吸血_普攻不吸血_效果到期停止()
    {
        SkillSO tide = Skill(EnumSkillType.Tide);
        SkillSO crown = Skill(EnumSkillType.Crown);
        ModifierEffect steal = Asset<ModifierEffect>();
        steal.Duration = 10f;
        steal.CrownLifeStealRatio = 1f;
        tide.AfterTideEffects = new[] { steal };
        Host.Vitals.ApplyDamage(30f);
        Host.Vitals.Faith!.Update(-33);
        Assert.IsTrue(Host.Brain.TryCastSkill(EnumSkillType.Tide, out _));
        Host.Brain.AttackState.Tick(.15f);
        Assert.AreEqual(70f, Host.Vitals.CurrentHp, "潮汐命中不吸血");
        Host.Brain.StateMachine.ClearState(EnumStateLayer.Action);
        Host.Brain.AttackState.BeginSingle(crown.Attack);
        Host.Brain.StateMachine.ChangeState(Host.Brain.AttackState);
        Host.Brain.AttackState.Tick(.15f);
        Assert.AreEqual(70f, Host.Vitals.CurrentHp, "普通攻击不能继承上一技能的吸血上下文");
        Host.Brain.StateMachine.ClearState(EnumStateLayer.Action);
        Host.Vitals.Faith.Update(33 - Host.Vitals.Faith.Current);
        Assert.IsTrue(Host.Brain.TryCastSkill(EnumSkillType.Crown, out _));
        Host.Brain.AttackState.Tick(.15f);
        Assert.AreEqual(73f, Host.Vitals.CurrentHp);
        Host.Brain.StateMachine.ClearState(EnumStateLayer.Action);
        Host.Slots.Skills.TickCooldown(10f);
        Host.Brain.Modifiers.Tick(11f);
        Host.Vitals.Faith.Update(33 - Host.Vitals.Faith.Current);
        Assert.IsTrue(Host.Brain.TryCastSkill(EnumSkillType.Crown, out _));
        Host.Brain.AttackState.Tick(.15f);
        Assert.AreEqual(73f, Host.Vitals.CurrentHp);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void 无效攻击配置不消费信心冷却或施加效果(int invalidCase)
    {
        SkillSO skill = Skill(EnumSkillType.Crown);
        ModifierEffect effect = Asset<ModifierEffect>();
        skill.Effects = new[] { effect };
        Host.Vitals.Faith!.Update(33);
        ComboEntry attack = skill.Attack;
        if (invalidCase == 0) attack.MeleeDamage = float.NaN;
        else
        {
            attack.HitShape = EnumMeleeHitShape.Sector;
            attack.HitAngle = 140f;
            attack.HitHeight = 2f;
            if (invalidCase == 1) attack.HitAngle = 361f;
            if (invalidCase == 2) attack.HitHeight = 0f;
            if (invalidCase == 3) attack.HitShape = (EnumMeleeHitShape)99;
        }
        skill.Attack = attack;
        Assert.IsFalse(Host.Brain.Capability.CanCastSkill(EnumSkillType.Crown), "资格显示与释放使用同一门禁");
        Assert.IsFalse(Host.Brain.TryCastSkill(EnumSkillType.Crown, out _));
        Assert.AreEqual(33, Host.Vitals.Faith.Current);
        Assert.AreEqual(0f, Host.Slots.Skills.CrownCooldownRemaining);
        Assert.IsFalse(Host.Brain.Modifiers.IsHolding(effect));
        Assert.IsNull(Host.Brain.StateMachine.GetActive(EnumStateLayer.Action));
    }

    [Test]
    public void 技能前摇被控制打断_解除后不补发伤害()
    {
        Skill(EnumSkillType.Crown);
        Host.Vitals.Faith!.Update(33);
        Assert.IsTrue(Host.Brain.TryCastSkill(EnumSkillType.Crown, out _));
        ModifierEffect stun = Asset<ModifierEffect>();
        stun.HasControl = true;
        stun.Duration = 1f;
        Host.Brain.Modifiers.Apply(stun);
        Assert.IsNull(Host.Brain.StateMachine.GetActive(EnumStateLayer.Action));
        Host.Brain.Modifiers.Tick(2f);
        Host.Brain.StateMachine.TwoPassTick(.5f);
        Assert.AreEqual(100f, Target.Vitals.CurrentHp);
        Assert.AreEqual(0, Host.Vitals.Faith.Current, "已成功起手，打断不退款");
    }
}
