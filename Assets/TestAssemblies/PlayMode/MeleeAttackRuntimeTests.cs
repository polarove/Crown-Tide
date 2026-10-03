using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class MeleeAttackRuntimeTests
{
    private readonly List<Object> Created = new();
    private Entity Host = null!;
    private Entity Target = null!;
    private static readonly Vector3 TestOrigin = new(500f, 500f, 500f);

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
        entity.Brain.enabled = false; // 手动推进状态机，隔离真实帧率与重力。
        entity.Config = Asset<CharacterConfigSO>();
        entity.Config.FactionId = faction;
        entity.Config.FaithGainPerEnemyHit = 5;
        entity.Config.FaithGainPerEnemyKill = 10;
        entity.Vitals.Initialize(entity.Config);
        return entity;
    }

    private ComboEntry Entry() => new()
    {
        Name = "测试段", Windup = .1f, Hit = .2f, Recovery = .3f, NextEntry = -1,
        MeleeDamage = 10f, HitRadius = .6f, HitOffset = Vector3.forward, HitLayers = ~0,
    };

    private ComboEntry SectorEntry()
    {
        ComboEntry entry = Entry();
        entry.HitShape = EnumMeleeHitShape.Sector;
        entry.HitRadius = 2.5f;
        entry.HitAngle = 140f;
        entry.HitHeight = 2f;
        entry.HitOffset = Vector3.up;
        return entry;
    }

    private EntityAttackState Start(params ComboEntry[] entries)
    {
        Host.Brain.StateMachine.ClearState(EnumStateLayer.Action);
        WeaponComboGraph graph = Asset<WeaponComboGraph>();
        graph.ComboEntries = entries;
        Host.Slots.UnarmedComboGraph = graph;
        Host.Brain.AttackState.BeginCombo();
        Host.Brain.StateMachine.ChangeState(Host.Brain.AttackState);
        Physics.SyncTransforms();
        return Host.Brain.AttackState;
    }

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        Host = Actor("MeleeHost", TestOrigin, 1);
        Target = Actor("MeleeTarget", TestOrigin + Vector3.forward * 1.3f, 2);
        Assert.DoesNotThrow(() => { _ = Host.Brain.AttackState.Phase; }, "首次攻击前调试阶段应可读取");
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
    public void 仅命中窗口伤害_重复帧与自身不重复结算()
    {
        EntityAttackState state = Start(Entry());
        state.Tick(.05f);
        Assert.AreEqual(100f, Target.Vitals.CurrentHp);
        state.Tick(.06f);
        Assert.AreEqual(90f, Target.Vitals.CurrentHp);
        state.Tick(.06f);
        state.Tick(.3f);
        Assert.AreEqual(90f, Target.Vitals.CurrentHp);
        Assert.AreEqual(100f, Host.Vitals.CurrentHp);
        Assert.AreEqual(5, Host.Vitals.Faith!.Current);
        Assert.AreEqual(-5, Target.Vitals.Faith!.Current);
    }

    [Test]
    public void 大步进跨过整个窗口仍命中一次并正常收招()
    {
        EntityAttackState state = Start(Entry());
        Host.Brain.StateMachine.TwoPassTick(.7f);
        Assert.AreEqual(90f, Target.Vitals.CurrentHp);
        Host.Brain.StateMachine.TwoPassTick(.01f);
        Assert.IsNull(Host.Brain.StateMachine.GetActive(EnumStateLayer.Action));
        Assert.IsFalse(Host.Tags.Has((ulong)EnumEntityTag.Swinging));
    }

    [Test]
    public void 窗口期间进入范围仍可命中_后摇进入不可命中()
    {
        Target.transform.position = TestOrigin + Vector3.forward * 8f;
        EntityAttackState state = Start(Entry());
        state.Tick(.11f);
        Assert.AreEqual(100f, Target.Vitals.CurrentHp);
        Target.transform.position = TestOrigin + Vector3.forward * 1.3f;
        Physics.SyncTransforms();
        state.Tick(.05f);
        Assert.AreEqual(90f, Target.Vitals.CurrentHp);
        Target.transform.position = TestOrigin + Vector3.forward * 8f;
        state = Start(Entry());
        state.Tick(.4f);
        Target.transform.position = TestOrigin + Vector3.forward * 1.3f;
        Physics.SyncTransforms();
        state.Tick(.05f);
        Assert.AreEqual(90f, Target.Vitals.CurrentHp);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void 同实体多个碰撞体只造成一次伤害与信心事件(bool sector)
    {
        for (int i = 0; i < 3; i++)
        {
            var child = new GameObject("ExtraHurtbox");
            child.transform.SetParent(Target.transform, false);
            child.AddComponent<SphereCollider>().radius = .25f;
        }
        EntityAttackState state = Start(sector ? SectorEntry() : Entry());
        state.Tick(.11f);
        Assert.AreEqual(90f, Target.Vitals.CurrentHp);
        Assert.AreEqual(5, Host.Vitals.Faith!.Current);
        Assert.AreEqual(-5, Target.Vitals.Faith!.Current);
    }

    [Test]
    public void 续段重新去重_同一目标可被下一段命中()
    {
        ComboEntry first = Entry();
        first.NextEntry = 1;
        first.CancelWindow = .2f;
        EntityAttackState state = Start(first, Entry());
        state.Tick(.11f);
        Host.Commands.AttackQueued = true;
        state.Tick(.25f);
        Assert.IsFalse(Host.Commands.AttackQueued);
        state.Tick(.11f);
        Assert.AreEqual(80f, Target.Vitals.CurrentHp);
        Assert.AreEqual(10, Host.Vitals.Faith!.Current);
    }

    [Test]
    public void 控制中断攻击_解除后不会续播_新攻击可再次命中()
    {
        EntityAttackState state = Start(Entry());
        state.Tick(.05f);
        ModifierEffect stun = Asset<ModifierEffect>();
        stun.HasControl = true;
        Host.Brain.Modifiers.Apply(stun);
        Assert.IsNull(Host.Brain.StateMachine.GetActive(EnumStateLayer.Action));
        Host.Brain.StateMachine.TwoPassTick(.2f);
        Assert.AreEqual(100f, Target.Vitals.CurrentHp);
        Assert.IsFalse(Host.Tags.Has((ulong)EnumEntityTag.Swinging));
        Host.Brain.Modifiers.Remove(stun);
        Host.Brain.StateMachine.ClearState(EnumStateLayer.CrowdControl);
        Start(Entry()).Tick(.11f);
        Assert.AreEqual(90f, Target.Vitals.CurrentHp);
    }

    [Test]
    public void 碰撞层与Trigger配置生效()
    {
        ComboEntry entry = Entry();
        entry.HitLayers = 1 << 30;
        Start(entry).Tick(.11f);
        Assert.AreEqual(100f, Target.Vitals.CurrentHp);
        Target.GetComponent<CharacterController>().enabled = false;
        var child = new GameObject("TriggerHurtbox");
        child.transform.SetParent(Target.transform, false);
        SphereCollider sphere = child.AddComponent<SphereCollider>();
        sphere.radius = .25f;
        sphere.isTrigger = true;
        entry = Entry();
        Start(entry).Tick(.11f);
        Assert.AreEqual(100f, Target.Vitals.CurrentHp);
        entry.IncludeTriggers = true;
        Start(entry).Tick(.11f);
        Assert.AreEqual(90f, Target.Vitals.CurrentHp);
    }

    [Test]
    public void 缺省或无效判定不会凭空伤害()
    {
        ComboEntry entry = Entry();
        entry.MeleeDamage = 0f;
        Start(entry).Tick(.11f);
        entry = Entry(); entry.HitRadius = 0f;
        Start(entry).Tick(.11f);
        entry = Entry(); entry.HitLayers = 0;
        Start(entry).Tick(.11f);
        entry = Entry(); entry.MeleeDamage = float.NaN;
        Start(entry).Tick(.11f);
        entry = Entry(); entry.Hit = 0f;
        Start(entry).Tick(.11f);
        Assert.AreEqual(100f, Target.Vitals.CurrentHp);
        Assert.AreEqual(0, Host.Vitals.Faith!.Current);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void 查询缓冲扩容不会漏掉密集目标(bool sector)
    {
        var targets = new List<Entity> { Target };
        for (int i = 0; i < 39; i++) targets.Add(Actor("CrowdedTarget", Target.transform.position, 2));
        EntityAttackState state = Start(sector ? SectorEntry() : Entry());
        state.Tick(.11f);
        state.Tick(.05f);
        foreach (Entity target in targets) Assert.AreEqual(90f, target.Vitals.CurrentHp);
        Assert.AreEqual(67, Host.Vitals.Faith!.Current);
    }

    [TestCase(0f, 2.4f, true)]
    [TestCase(60f, 2.4f, true)]
    [TestCase(-60f, 2.4f, true)]
    [TestCase(80f, 2.4f, false)]
    [TestCase(180f, 2f, false)]
    [TestCase(0f, 3.5f, false)]
    public void 扇形按角度与距离检测_侧前方不需贴脸(float angle, float distance, bool hit)
    {
        Target.transform.position = TestOrigin + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * distance;
        EntityAttackState state = Start(SectorEntry());
        state.Tick(.11f);
        state.Tick(.05f);
        Assert.AreEqual(hit ? 90f : 100f, Target.Vitals.CurrentHp);
        Assert.AreEqual(hit ? 5 : 0, Host.Vitals.Faith!.Current);
    }

    [Test]
    public void 扇形随角色朝向旋转_不沿世界固定方向()
    {
        Host.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        Target.transform.position = TestOrigin + Vector3.right * 2.4f;
        Start(SectorEntry()).Tick(.11f);
        Assert.AreEqual(90f, Target.Vitals.CurrentHp);
        Target.transform.position = TestOrigin + Vector3.forward * 2.4f;
        Start(SectorEntry()).Tick(.11f);
        Assert.AreEqual(90f, Target.Vitals.CurrentHp, "新方向侧面不在140度内");
    }

    [Test]
    public void 扇形有高度限制_不误伤高处实体()
    {
        Target.transform.position = TestOrigin + Vector3.forward * 1.5f + Vector3.up * 4f;
        Start(SectorEntry()).Tick(.11f);
        Assert.AreEqual(100f, Target.Vitals.CurrentHp);
    }

    [Test]
    public void 起手快照不被共享出招数组修改影响()
    {
        EntityAttackState state = Start(Entry());
        ComboEntry changed = Entry();
        changed.MeleeDamage = 999f;
        Host.Slots.UnarmedComboGraph!.ComboEntries[0] = changed;
        state.Tick(.11f);
        Assert.AreEqual(90f, Target.Vitals.CurrentHp);
        Assert.AreEqual(999f, Host.Slots.UnarmedComboGraph.ComboEntries[0].MeleeDamage);
    }

    [Test]
    public void 命中处理中取消攻击_不再结算其余查询目标()
    {
        Entity other = Actor("OtherTarget", Target.transform.position, 2);
        Target.Vitals.Died += _ => Host.Brain.StateMachine.ClearState(EnumStateLayer.Action);
        other.Vitals.Died += _ => Host.Brain.StateMachine.ClearState(EnumStateLayer.Action);
        ComboEntry entry = Entry();
        entry.MeleeDamage = 999f;
        Start(entry).Tick(.11f);
        Assert.AreNotEqual(Target.IsDead, other.IsDead, "首个目标死亡触发取消后只能结算一个目标");
        Assert.IsNull(Host.Brain.StateMachine.GetActive(EnumStateLayer.Action));
    }

    [Test]
    public void 友伤降低目标信心_未知阵营不猜敌对()
    {
        Target.Config!.FactionId = 1;
        Start(Entry()).Tick(.11f);
        Assert.AreEqual(0, Host.Vitals.Faith!.Current);
        Assert.AreEqual(-5, Target.Vitals.Faith!.Current);
        Target.Config.FactionId = 0;
        Start(Entry()).Tick(.11f);
        Assert.AreEqual(0, Host.Vitals.Faith.Current);
        Assert.AreEqual(80f, Target.Vitals.CurrentHp);
        Target.Config.FactionId = 2;
        Start(Entry()).Tick(.11f);
        Assert.AreEqual(5, Host.Vitals.Faith.Current);
    }

    [Test]
    public void AI只提交攻击请求_控制门禁仍统一仲裁()
    {
        AITreeInputSource ai = Host.GetComponent<AITreeInputSource>();
        Host.Brain.Bootstrap(Host);
        ai.Target = Target.transform;
        ai.DecisionInterval = 0f;
        ai.EnableMeleeAttack = true;
        Host.Brain.BindInputSource(ai);
        ai.GatherCommands(Host.Commands);
        Assert.IsTrue(Host.Commands.AttackQueued);
        Host.Commands.ClearEdges();
        ModifierEffect stun = Asset<ModifierEffect>();
        stun.HasControl = true;
        Host.Brain.Modifiers.Apply(stun);
        ai.GatherCommands(Host.Commands);
        Assert.IsFalse(Host.Commands.AttackQueued);
        ai.EnableMeleeAttack = false;
        Host.Brain.Modifiers.Remove(stun);
        Host.Brain.StateMachine.ClearState(EnumStateLayer.CrowdControl);
        ai.GatherCommands(Host.Commands);
        Assert.IsFalse(Host.Commands.AttackQueued);
        Assert.Greater(Host.Commands.MoveDirection.z, 0f);
    }

    [UnityTest]
    public IEnumerator AI起手间隔_不因动作空闲而每帧重复请求()
    {
        AITreeInputSource ai = Host.GetComponent<AITreeInputSource>();
        ai.Target = Target.transform;
        ai.EnableMeleeAttack = true;
        ai.MeleeAttackInterval = .5f;
        ai.DecisionInterval = 0f;
        Host.Brain.BindInputSource(ai);
        ai.GatherCommands(Host.Commands);
        Assert.IsTrue(Host.Commands.AttackQueued);
        Host.Commands.ClearEdges();
        float elapsed = 0f;
        bool requestedAgain = false;
        while (elapsed < 1f)
        {
            yield return null;
            elapsed += Time.deltaTime;
            ai.GatherCommands(Host.Commands);
            if (Host.Commands.AttackQueued)
            {
                requestedAgain = true;
                Assert.GreaterOrEqual(elapsed, .5f - Time.deltaTime, "至少经过配置的起手间隔");
                break;
            }
            Assert.AreEqual(Vector3.zero, Host.Commands.MoveDirection, "范围内间隔阶段应等待，避免顶着目标走");
        }
        Assert.IsTrue(requestedAgain, "间隔到期后仍能请求攻击");
        Host.Commands.ClearEdges();
        Host.Brain.BindInputSource(Host.Brain.PlayerSource);
        ai.GatherCommands(Host.Commands);
        Assert.IsFalse(Host.Commands.AttackQueued, "玩家接管后旧 AI 不能提交指令");
    }

    [Test]
    public void 换输入源清除上一操作者的攻击与残留指令()
    {
        EntityAttackState state = Start(Entry());
        state.Tick(.05f);
        Host.Commands.AttackQueued = true;
        Host.Brain.BindInputSource(Host.Brain.PlayerSource);
        Assert.IsNull(Host.Brain.StateMachine.GetActive(EnumStateLayer.Action));
        Assert.IsFalse(Host.Commands.AttackQueued);
        Assert.IsFalse(Host.Tags.Has((ulong)EnumEntityTag.Swinging));
        Host.Brain.StateMachine.TwoPassTick(.2f);
        Assert.AreEqual(100f, Target.Vitals.CurrentHp);
        Assert.AreEqual(1, Host.Config!.FactionId);
    }
}
