using Assets.Scripts.Entity.Data.Skill;
using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public sealed class KillPossessionRuntimeTests
{
    private readonly List<Object> Created = new();
    private Entity Origin = null!;
    private Entity Target = null!;
    private SkillSO Weapon = null!;
    private PossessionProfileSO Profile = null!;
    private InputActionAsset Actions = null!;
    private SimulatedInputFocusScope Focus = null!;
    private Keyboard SetupKeyboard = null!;
    private Mouse SetupMouse = null!;

    private T Asset<T>() where T : ScriptableObject
    {
        T asset = ScriptableObject.CreateInstance<T>();
        Created.Add(asset);
        return asset;
    }

    private Entity Actor(string name, Vector3 position, int faction)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        Created.Add(go);
        go.transform.position = position;
        Entity actor = go.AddComponent<Entity>();
        PlayerInput input = actor.GetComponent<PlayerInput>();
        input.actions = Actions;
        input.defaultActionMap = "Player";
        input.notificationBehavior = PlayerNotifications.SendMessages;
        actor.GetComponent<PlayerInputSource>().FallbackActions = Actions;
        actor.Config = Asset<CharacterConfigSO>();
        actor.Config.MaxHealth = 100f;
        actor.Config.FactionId = faction;
        actor.Config.FaithGainPerEnemyHit = 2;
        actor.Config.FaithGainPerEnemyKill = 5;
        go.SetActive(true);
        actor.Motor.Gravity = 0f;
        actor.Brain.AiSource!.Target = null;
        actor.Brain.AiSource.EnableMeleeAttack = false;
        return actor;
    }

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        Focus = new SimulatedInputFocusScope();
        SetupKeyboard = InputSystem.AddDevice<Keyboard>();
        SetupMouse = InputSystem.AddDevice<Mouse>();
        Actions = InputActionAsset.FromJson(System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "Input/PlayerControls.inputactions")));
        Created.Add(Actions);
        Profile = Asset<PossessionProfileSO>();
        Profile.PossessedEffect = Asset<ModifierEffect>();
        Profile.PossessedEffect.Name = "被附身";
        Profile.PossessedEffect.Category = EnumModifierCategory.Buff;
        Profile.PossessedEffect.Duration = 10f;
        Profile.SoulOutEffect = Asset<ModifierEffect>();
        Profile.SoulOutEffect.Name = "附身中";
        Profile.SoulOutEffect.Category = EnumModifierCategory.Buff;
        Profile.SoulOutEffect.Duration = 0f;
        Weapon = Asset<SkillSO>();
        Weapon.Kind = EnumSkillType.Weapon;
        Weapon.Cooldown = 4f;
        Weapon.HasAttack = true;
        Weapon.PossessionOnKill = Profile;
        Weapon.Attack = new ComboEntry { Name = "木棍测试", Windup = .1f, Hit = .2f, Recovery = .1f,
            MeleeDamage = 10f, HitOffset = Vector3.forward, HitRadius = .8f, HitLayers = ~0, NextEntry = -1 };
        Origin = Actor("PossessionOrigin", new Vector3(800f, 800f, 800f), 1);
        Target = Actor("PossessionTarget", Origin.transform.position + Vector3.forward * 1.3f, 2);
        Origin.Slots.UnarmedWeaponSkill = Target.Slots.UnarmedWeaponSkill = Weapon;
        WeaponSkillBinding.Sync(Origin);
        WeaponSkillBinding.Sync(Target);
        Origin.Brain.BindInputSource(Origin.Brain.PlayerSource);
        Origin.Vitals.Faith!.Update(10);
        Target.Vitals.Faith!.Update(10);
        yield return null;
        Physics.SyncTransforms();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        for (int i = Created.Count - 1; i >= 0; i--)
            if (Created[i] != null && Created[i] is GameObject) Object.Destroy(Created[i]);
        yield return null;
        for (int i = Created.Count - 1; i >= 0; i--)
            if (Created[i] != null) Object.Destroy(Created[i]);
        Created.Clear();
        yield return null;
        InputSystem.RemoveDevice(SetupKeyboard);
        InputSystem.RemoveDevice(SetupMouse);
        Focus.Dispose();
    }

    private void KillWithWeapon()
    {
        Target.Brain.TakeDamage(95f);
        Assert.IsTrue(Origin.Brain.TryCastSkill(EnumSkillType.Weapon, out _));
        Origin.Brain.AttackState.Tick(.15f);
        Assert.IsTrue(Target.IsPossessed, "必须由实际命中造成击杀");
    }

    [Test]
    public void 指定技能实际击杀立即附身_红血为零仍可操作_不消耗信心()
    {
        int deaths = 0;
        Target.Vitals.Died += _ => deaths++;
        KillWithWeapon();
        Assert.AreEqual(1, deaths);
        Assert.AreEqual(0f, Target.Vitals.CurrentHp);
        Assert.IsTrue(Target.IsDead);
        Assert.IsTrue(Target.CanOperate);
        Assert.IsTrue(Target.Brain.StateMachine.HasState<EntityPossessedState>());
        Assert.IsTrue(Origin.IsPossessing);
        Assert.AreSame(Target.Brain.PlayerSource, Target.Brain.InputSource);
        Assert.AreSame(Origin.Brain.AiSource, Origin.Brain.InputSource);
        Assert.AreEqual(17, Origin.Vitals.Faith!.Current, "击杀事件保留，V 不清零");
        Assert.AreEqual(4f, Origin.Slots.Skills.WeaponCooldownRemaining);
        Assert.IsTrue(Target.Brain.Capability.CanMove());
        Assert.IsTrue(Target.Brain.TryCastSkill(EnumSkillType.Weapon, out _));
        Target.Vitals.ApplyHeal(100f);
        Assert.AreEqual(0f, Target.Vitals.CurrentHp, "被附身不能复活红血");
        Assert.AreEqual(1, deaths);
    }

    [Test]
    public void 未击杀不附身_普通攻击击杀不附身_已经死亡不能再次结算()
    {
        Assert.IsTrue(Origin.Brain.TryCastSkill(EnumSkillType.Weapon, out _));
        Origin.Brain.AttackState.Tick(.15f);
        Assert.AreEqual(90f, Target.Vitals.CurrentHp);
        Assert.IsFalse(Target.IsPossessed);
        Assert.AreSame(Origin.Brain.PlayerSource, Origin.Brain.InputSource);
        Assert.AreEqual(90f, Origin.Brain.ResolveHit(Target, 100f, true));
        Assert.IsFalse(Target.IsPossessed);
        Assert.IsFalse(Target.CanOperate);
        Assert.AreEqual(0f, Origin.Brain.ResolveHit(Target, 10f, true, EnumSkillType.Weapon, Profile, 10000f));
    }

    [Test]
    public void 被附身仍受失控限制_显式解控技能可释放_身份不被眩晕清除()
    {
        KillWithWeapon();
        ModifierEffect stun = Asset<ModifierEffect>();
        stun.HasControl = true;
        Target.Brain.Modifiers.Apply(stun);
        Assert.IsTrue(Target.IsPossessed);
        Assert.IsFalse(Target.Brain.Capability.CanMove());
        Assert.IsFalse(Target.Brain.TryCastSkill(EnumSkillType.Weapon, out _));
        SkillSO cleanse = Asset<SkillSO>();
        cleanse.Kind = EnumSkillType.Crown;
        cleanse.FaithThreshold = 1;
        cleanse.AllowWhileControlled = true;
        cleanse.DispelOnCast = EnumModifierCategory.Debuff;
        Target.Slots.Skills.Crown = cleanse;
        new CrownEvent(Target, 10).Invoke();
        Assert.IsTrue(Target.Brain.TryCastSkill(EnumSkillType.Crown, out _));
        Assert.IsFalse(Target.Brain.Modifiers.IsHolding(stun));
        Assert.IsTrue(Target.IsPossessed);
    }

    [Test]
    public void 单侧倒计时可延长_到期清双方Buff并恢复输入_尸体保持死亡()
    {
        KillWithWeapon();
        Assert.AreEqual(-1f, Origin.Brain.Modifiers.GetRemaining(Profile.SoulOutEffect!));
        Target.Brain.Modifiers.ExtendDuration(Profile.PossessedEffect!, 2f);
        Assert.AreEqual(12f, Target.PossessionRemaining);
        Target.Brain.Modifiers.Tick(11f);
        PossessionLogic.Reconcile(Target);
        Assert.IsTrue(Target.IsPossessed);
        Target.Brain.Modifiers.Tick(1f);
        PossessionLogic.Reconcile(Target);
        Assert.IsFalse(Origin.IsPossessing);
        Assert.IsFalse(Target.IsPossessed);
        Assert.IsFalse(Target.CanOperate);
        Assert.AreEqual(0f, Target.Vitals.CurrentHp);
        Assert.IsFalse(Origin.Brain.Modifiers.IsHolding(Profile.SoulOutEffect!));
        Assert.AreSame(Origin.Brain.PlayerSource, Origin.Brain.InputSource);
        Assert.AreSame(Target.Brain.AiSource, Target.Brain.InputSource);
    }

    [Test]
    public void 原角色死亡立即归还视角并清理_不恢复红血()
    {
        KillWithWeapon();
        Origin.Brain.TakeDamage(100f);
        Assert.IsTrue(Origin.IsDead);
        Assert.IsTrue(Origin.HasPlayerView);
        Assert.IsFalse(Origin.CanOperate);
        Assert.IsFalse(Target.IsPossessed);
        Assert.IsFalse(Target.Brain.Modifiers.IsHolding(Profile.PossessedEffect!));
    }

    [Test]
    public void 目标停用立即归还_重新启用不会恢复旧附身()
    {
        KillWithWeapon();
        Target.gameObject.SetActive(false);
        Assert.AreSame(Origin.Brain.PlayerSource, Origin.Brain.InputSource);
        Assert.IsFalse(Origin.IsPossessing);
        Target.gameObject.SetActive(true);
        Assert.IsFalse(Target.IsPossessed);
        Assert.IsTrue(Target.IsDead);
    }

    [Test]
    public void 起手快照不受换技能影响_连续击杀转新身体仍回最初本体()
    {
        Target.Brain.TakeDamage(95f);
        Assert.IsTrue(Origin.Brain.TryCastSkill(EnumSkillType.Weapon, out _));
        Origin.Slots.Skills.SetWeaponSkill(null);
        Origin.Brain.AttackState.Tick(.15f);
        Assert.IsTrue(Target.IsPossessed);
        Entity next = Actor("NextCarrier", Target.transform.position + Vector3.forward, 1);
        Assert.AreEqual(100f, Target.Brain.ResolveHit(next, 100f, true, EnumSkillType.Weapon, Profile, 10000f));
        Assert.IsTrue(next.IsPossessed);
        Assert.IsFalse(Target.IsPossessed);
        Assert.IsTrue(Origin.IsPossessing);
        Assert.AreSame(next.Brain.PlayerSource, next.Brain.InputSource);
        next.Brain.Modifiers.Remove(Profile.PossessedEffect!);
        PossessionLogic.Reconcile(next);
        Assert.IsFalse(Origin.IsPossessing);
        Assert.AreSame(Origin.Brain.PlayerSource, Origin.Brain.InputSource);
    }

    [Test]
    public void AI击杀走同一结算但不占用本地玩家输入()
    {
        Origin.Brain.BindInputSource(Origin.Brain.AiSource);
        KillWithWeapon();
        Assert.AreSame(Target.Brain.AiSource, Target.Brain.InputSource);
        Assert.IsFalse(Target.HasPlayerView);
        Assert.IsTrue(Target.CanOperate);
    }

    [Test]
    public void 周期伤害致死本体_迭代后收尾不跳过其他效果或越界()
    {
        KillWithWeapon();
        ModifierEffect dot = Asset<ModifierEffect>();
        dot.Duration = 2f;
        dot.HasPeriodic = true;
        dot.TickInterval = .1f;
        dot.DamagePerTick = 100f;
        Origin.Brain.Modifiers.Apply(dot);
        Assert.DoesNotThrow(() => Origin.Brain.Modifiers.Tick(.1f));
        PossessionLogic.Reconcile(Origin);
        Assert.IsTrue(Origin.IsDead);
        Assert.IsFalse(Target.IsPossessed);
        Assert.IsTrue(Origin.HasPlayerView);
    }

    [UnityTest]
    public IEnumerator 键鼠两次击杀附身返回后_设备归属移动与视角正常()
    {
        return VerifyDevices(false);
    }

    [UnityTest]
    public IEnumerator 手柄两次击杀附身返回后_设备归属移动与视角正常()
    {
        return VerifyDevices(true);
    }

    [TestCase(10)]
    [TestCase(-10)]
    public void 正负信心均取释放快照_命中涨信心和后续改值不影响绿血时长(int faith)
    {
        Origin.Vitals.Faith!.Reset();
        Origin.Vitals.Faith.Update(faith);
        Target.Brain.TakeDamage(95f);
        Assert.IsTrue(Origin.Brain.TryCastSkill(EnumSkillType.Weapon, out _));
        Origin.Vitals.Faith.Reset();
        Origin.Vitals.Faith.Update(1);
        Origin.Brain.AttackState.Tick(.15f);
        Assert.IsTrue(Target.IsPossessed);
        Assert.AreEqual(10000f, Target.Vitals.CurrentGreenHp);
        Assert.AreEqual(10f, Target.PossessionRemaining);
        Assert.AreEqual(8, Origin.Vitals.Faith.Current);
        Assert.AreEqual(1000f, Profile.K, "不改共享模板");
    }

    [Test]
    public void 附身继承目标控制效果信心冷却_绿血沿正常伤害并耗尽返回()
    {
        ModifierEffect debuff = Asset<ModifierEffect>();
        debuff.Duration = 30f;
        debuff.Category = EnumModifierCategory.Debuff;
        debuff.StatModifiers = new[] { new StatModifierEntry { Stat = EnumStatType.DamageTaken, Multiplier = 2f } };
        Target.Brain.Modifiers.Apply(debuff);
        Target.Slots.Skills.WeaponCooldownRemaining = 3f;
        int faith = Target.Vitals.Faith!.Current;
        // 直接真实红血击杀接缝，换算来自显式起手快照。
        Origin.Brain.ResolveHit(Target, 100f, true, EnumSkillType.Weapon, Profile, 10000f);
        Assert.IsTrue(Target.IsPossessed);
        Assert.IsTrue(Target.Brain.Modifiers.IsHolding(debuff));
        Assert.AreEqual(faith, Target.Vitals.Faith.Current);
        Assert.AreEqual(3f, Target.Slots.Skills.WeaponCooldownRemaining);
        Assert.IsFalse(Target.Brain.TryCastSkill(EnumSkillType.Weapon, out _));
        Assert.AreEqual(20f, Origin.Brain.ResolveHit(Target, 10f, true));
        Assert.AreEqual(9980f, Target.Vitals.CurrentGreenHp);
        Assert.AreEqual(0f, Target.Vitals.CurrentHp);
        Target.Brain.TakeDamage(10000f);
        Assert.IsFalse(Target.IsPossessed);
        Assert.IsFalse(Target.CanOperate);
        Assert.IsTrue(Target.Brain.Modifiers.IsHolding(debuff), "只移除附身标记");
        Assert.AreEqual(0f, Target.Vitals.CurrentGreenHp);
        Assert.AreSame(Origin.Brain.PlayerSource, Origin.Brain.InputSource);
    }

    [Test]
    public void 目标攻击状态在死亡附身交接中保持_普通死亡不清Buff和Faith()
    {
        Target.Brain.AttackState.BeginCombo();
        Target.Brain.StateMachine.ChangeState(Target.Brain.AttackState);
        Origin.Brain.ResolveHit(Target, 100f, true, EnumSkillType.Weapon, Profile, 10000f);
        Assert.AreSame(Target.Brain.AttackState, Target.Brain.StateMachine.GetActive(EnumStateLayer.Action));
        Assert.IsTrue(Target.Tags.Has((ulong)EnumEntityTag.Swinging));
        ModifierEffect buff = Asset<ModifierEffect>();
        buff.Duration = 30f;
        Origin.Brain.Modifiers.Apply(buff);
        int faith = Origin.Vitals.Faith!.Current;
        Origin.Brain.TakeDamage(100f);
        Assert.IsTrue(Origin.Brain.Modifiers.IsHolding(buff));
        Assert.AreEqual(faith, Origin.Vitals.Faith.Current);
    }

    [Test]
    public void 绿血周期伤害耗尽_迭代完成后归还且保留其他Debuff()
    {
        KillWithWeapon();
        ModifierEffect dot = Asset<ModifierEffect>();
        dot.Duration = 5f;
        dot.HasPeriodic = true;
        dot.TickInterval = .1f;
        dot.DamagePerTick = 10000f;
        Target.Brain.Modifiers.Apply(dot);
        Assert.DoesNotThrow(() => Target.Brain.Modifiers.Tick(.1f));
        PossessionLogic.Reconcile(Target);
        Assert.IsFalse(Target.IsPossessed);
        Assert.IsTrue(Target.Brain.Modifiers.IsHolding(dot));
        Assert.AreSame(Origin.Brain.PlayerSource, Origin.Brain.InputSource);
    }

    [Test]
    public void 被附身冠冕吸血恢复绿血_技能消耗目标信心且不复活红血()
    {
        KillWithWeapon();
        Target.Brain.TakeDamage(100f);
        ModifierEffect steal = Asset<ModifierEffect>();
        steal.CrownLifeStealRatio = 1f;
        Target.Brain.Modifiers.Apply(steal);
        SkillSO crown = Asset<SkillSO>();
        crown.Kind = EnumSkillType.Crown;
        crown.FaithThreshold = 1;
        crown.Cooldown = 2f;
        Target.Slots.Skills.Crown = crown;
        new CrownEvent(Target, 1).Invoke(); // 前两次正常受击已将继承的信心降至0。
        Assert.IsTrue(Target.Brain.TryCastSkill(EnumSkillType.Crown, out _));
        Assert.AreEqual(0, Target.Vitals.Faith!.Current);
        Assert.AreEqual(2f, Target.Slots.Skills.CrownCooldownRemaining);
        Assert.AreEqual(17, Origin.Vitals.Faith!.Current);
        float hp = Target.Vitals.CurrentGreenHp;
        Target.Brain.ResolveHit(Origin, 10f, true, EnumSkillType.Crown);
        Assert.AreEqual(hp + 10f, Target.Vitals.CurrentGreenHp);
        Assert.AreEqual(0f, Target.Vitals.CurrentHp);
    }

    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void 非法系数拒绝释放_不改变信心或冷却(float k)
    {
        Profile.K = k;
        Assert.IsFalse(Origin.Brain.TryCastSkill(EnumSkillType.Weapon, out _));
        Assert.AreEqual(10, Origin.Vitals.Faith!.Current);
        Assert.AreEqual(0f, Origin.Slots.Skills.WeaponCooldownRemaining);
        Assert.IsFalse(Target.IsPossessed);
    }

    private CameraRig CameraFor(Entity entity)
    {
        var go = new GameObject(entity.name + " Camera");
        go.SetActive(false);
        Created.Add(go);
        CameraRig rig = go.AddComponent<CameraRig>();
        rig.FollowEntity = entity;
        rig.InputActionAsset = Actions;
        rig.LockAndHideCursor = false;
        go.SetActive(true);
        return rig;
    }

    private IEnumerator VerifyDevices(bool pad)
    {
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
        try
        {
            CameraRig originRig = CameraFor(Origin);
            PlayerInput input = Origin.GetComponent<PlayerInput>();
            Assert.IsTrue(input.enabled, "源绑定必须启用 PlayerInput");
            Assert.IsTrue(input.user.valid, "激活输入源后必须建立 InputUser");
            input.neverAutoSwitchControlSchemes = true;
            if (pad) input.SwitchCurrentControlScheme("Gamepad", gamepad);
            else input.SwitchCurrentControlScheme("Keyboard&Mouse", keyboard, mouse);
            yield return null;
            yield return null;
            for (int cycle = 0; cycle < 2; cycle++)
            {
                Entity carrier = cycle == 0 ? Target : Actor("SecondTarget", Target.transform.position + Vector3.right * 3f, 2);
                CameraRig rig = CameraFor(carrier);
                carrier.Brain.TakeDamage(95f);
                Origin.Brain.ResolveHit(carrier, 10f, true, EnumSkillType.Weapon, Profile, 10000f);
                Assert.IsTrue(carrier.IsPossessed);
                yield return null;
                yield return null;
                Assert.IsTrue(rig.GetComponent<Camera>().enabled);
                Assert.IsFalse(originRig.GetComponent<Camera>().enabled);
                PlayerInput controlled = carrier.GetComponent<PlayerInput>();
                Assert.IsTrue(System.Array.Exists(controlled.devices.ToArray(), device => device == (pad ? (InputDevice)gamepad : mouse)));
                carrier.Brain.Modifiers.Remove(Profile.PossessedEffect!);
                PossessionLogic.Reconcile(carrier);
                yield return null;
                yield return null;
                Assert.IsTrue(originRig.GetComponent<Camera>().enabled);
                Assert.IsFalse(rig.GetComponent<Camera>().enabled);
                Assert.IsTrue(System.Array.Exists(input.devices.ToArray(), device => device == (pad ? (InputDevice)gamepad : mouse)));
                Quaternion before = originRig.transform.rotation;
                Vector3 beforePosition = Origin.transform.position;
                for (int frame = 0; frame < 6; frame++)
                {
                    if (pad) InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = Vector2.up, rightStick = new Vector2(.8f, 0f) });
                    else
                    {
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                        InputSystem.QueueDeltaStateEvent(mouse.delta, new Vector2(16f, 0f));
                    }
                    yield return null;
                }
                yield return new WaitForSeconds(.2f); // 手柄为速率输入，不能用极快的批处理帧数代替经过时间。
                Assert.Greater(Quaternion.Angle(before, originRig.transform.rotation), .1f);
                Assert.Greater(Vector3.Distance(beforePosition, Origin.transform.position), .01f);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.QueueStateEvent(gamepad, new GamepadState());
                yield return null;
            }
        }
        finally
        {
            InputSystem.RemoveDevice(keyboard);
            InputSystem.RemoveDevice(mouse);
            InputSystem.RemoveDevice(gamepad);
        }
    }
}
