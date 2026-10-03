using Assets.Scripts.Entity.Data.Skill;
using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class MechanismLoopSceneTests
{
    private SimulatedInputFocusScope Focus = null!;
    private Keyboard Keyboard = null!;
    private Mouse Mouse = null!;
    private Gamepad Pad = null!;
    private Scene Scene;
    private readonly HashSet<Scene> OriginalScenes = new();
    private Entity Player = null!;
    private Entity Enemy = null!;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        OriginalScenes.Clear();
        for (int i = 0; i < SceneManager.sceneCount; i++) OriginalScenes.Add(SceneManager.GetSceneAt(i));
        Focus = new SimulatedInputFocusScope();
        Keyboard = InputSystem.AddDevice<Keyboard>();
        Mouse = InputSystem.AddDevice<Mouse>();
        Pad = InputSystem.AddDevice<Gamepad>();
        yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Additive);
        yield return null;
        Scene = SceneManager.GetSceneByName("SampleScene");
        FindActors();
        Pair(Player, false);
        Assert.IsTrue(Enemy.Brain.AiSource!.EnableMeleeAttack, "保存场景默认仍启用敌人攻击");
        Enemy.Brain.AiSource.EnableMeleeAttack = false;
        Enemy.Brain.AiSource.Target = null;
        Player.Brain.AiSource!.Target = null;
        PlaceActors();
        DebugSystem.SetEnabled(false);
        yield return null;
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        var toUnload = new List<Scene>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.name == "SampleScene" && !OriginalScenes.Contains(scene)) toUnload.Add(scene);
        }
        foreach (Scene scene in toUnload)
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        InputSystem.RemoveDevice(Keyboard);
        InputSystem.RemoveDevice(Mouse);
        InputSystem.RemoveDevice(Pad);
        Focus.Dispose();
        DebugSystem.SetEnabled(false);
        yield return null;
    }

    [UnityTest]
    public IEnumerator 后退仍朝自己的视角攻击_前摇命中方向不被移动覆盖()
    {
        InputSystem.QueueStateEvent(Keyboard, new KeyboardState(Key.S));
        yield return new WaitForSeconds(.12f);
        Vector3 look = Player.Brain.PlayerSource!.ViewTransform!.forward;
        look.y = 0f;
        Assert.Greater(Vector3.Angle(Player.transform.forward, look), 40f, "先真实后退使身体背离视角");
        float hp = Enemy.Vitals.CurrentHp;
        InputSystem.QueueStateEvent(Mouse, new MouseState().WithButton(MouseButton.Left));
        yield return WaitFor(() => Player.Brain.StateMachine.GetActive(EnumStateLayer.Action) is EntityAttackState, "后退中正常起手");
        Assert.Less(Vector3.Angle(Player.transform.forward, look), 1f);
        yield return WaitFor(() => Enemy.Vitals.CurrentHp < hp, "后退中攻击仍命中视角前方");
        Assert.Less(Vector3.Angle(Player.transform.forward, look), 1f, "持续S不得在前摇／命中期间掉头");
        InputSystem.QueueStateEvent(Keyboard, new KeyboardState());
        InputSystem.QueueStateEvent(Mouse, new MouseState());
        Assert.AreEqual(hp - 3f, Enemy.Vitals.CurrentHp);
    }

    [UnityTest]
    public IEnumerator 非Debug也显示资源范围_实际伤害治疗独立跳字_禁用重新订阅不重复()
    {
        EntityCombatVisual visual = Player.GetComponent<EntityCombatVisual>();
        EntityCombatVisual enemyVisual = Enemy.GetComponent<EntityCombatVisual>();
        Assert.IsNotNull(visual);
        Assert.IsTrue(visual.IsStatusVisible);
        Assert.IsTrue(enemyVisual.IsStatusVisible);
        Assert.IsFalse(visual.StatusScreenRect.Overlaps(enemyVisual.StatusScreenRect), "头顶面板不应盖住彼此");
        Assert.IsTrue(visual.IsRangeVisible);
        Assert.IsFalse(enemyVisual.IsRangeVisible, "待机敌人不显示永久攻击圈");
        int faith = Player.Vitals.Faith!.Current;
        Player.Brain.TakeDamage(20f, countsAsHit: false);
        Player.Vitals.ApplyHeal(10f);
        Assert.AreEqual(10f, visual.LastHpDelta);
        Assert.AreEqual(2, visual.ActivePopupCount, "同帧受伤与回血都可观察，不能仅显示净差");
        yield return null;
        yield return null;
        Assert.AreEqual(90f, Player.Vitals.CurrentHp, "显示过程不修改生命");
        Assert.AreEqual(faith, Player.Vitals.Faith.Current, "显示过程不修改信心");
        visual.enabled = false;
        Assert.IsFalse(visual.IsStatusVisible);
        Assert.IsFalse(visual.IsRangeVisible);
        Assert.AreEqual(0, visual.ActivePopupCount);
        visual.enabled = true;
        Player.Brain.TakeDamage(3f, countsAsHit: false);
        Assert.AreEqual(1, visual.ActivePopupCount, "恢复显示不得重复订阅生命事件");
        DebugSystem.SetEnabled(true);
        yield return null;
        yield return null;
        Assert.IsFalse(visual.IsStatusVisible, "详细Debug不与普通资源面板叠放");
        DebugSystem.SetEnabled(false);
        yield return null;
        yield return null;
        Assert.IsTrue(visual.IsStatusVisible);
    }



    [UnityTest]
    public IEnumerator 保存场景扇形普攻_远处侧前方命中且技能使用同一范围()
    {
        var entries = new[]
        {
            Player.Slots.UnarmedComboGraph!.ComboEntries[0],
            Player.Slots.Skills.Crown!.Attack, Player.Slots.Skills.Crown.BurstAttack,
            Player.Slots.Skills.Tide!.Attack, Player.Slots.Skills.Tide.BurstAttack,
        };
        foreach (ComboEntry entry in entries)
        {
            Assert.AreEqual(EnumMeleeHitShape.Sector, entry.HitShape);
            Assert.AreEqual(2.5f, entry.HitRadius);
            Assert.AreEqual(140f, entry.HitAngle);
            Assert.AreEqual(2f, entry.HitHeight);
        }
        Teleport(Enemy, Player.transform.position + Quaternion.Euler(0f, 60f, 0f) * Vector3.forward * 2.4f,
            Quaternion.Euler(0f, 180f, 0f));
        Physics.SyncTransforms();
        yield return Attack(Player, Enemy);
        Assert.AreEqual(5, Player.Vitals.Faith!.Current);
    }

    [UnityTest]
    public IEnumerator 真实受击积累负信心_潮汐归零_普攻重积累_冠冕回血()
    {
        for (int i = 0; i < 7; i++)
        {
            Enemy.Commands.LookDirection = (Player.transform.position - Enemy.transform.position).normalized;
            Enemy.Commands.AttackQueued = true;
            float hpBefore = Player.Vitals.CurrentHp;
            yield return WaitFor(() => Player.Vitals.CurrentHp < hpBefore, "敌人真实近战受击");
            yield return Recover(Enemy);
        }
        Assert.AreEqual(-35, Player.Vitals.Faith!.Current, "负信心全部来自真实近战受击");
        Assert.AreEqual(79f, Player.Vitals.CurrentHp);
        PlaceActors();
        float enemyHp = Enemy.Vitals.CurrentHp;
        InputSystem.QueueStateEvent(Keyboard, new KeyboardState(Key.E));
        yield return WaitFor(() => Player.Brain.LastSkillCast?.Kind == EnumSkillType.Tide, "E 应释放潮汐，不依赖 Debug");
        InputSystem.QueueStateEvent(Keyboard, new KeyboardState());
        Assert.AreEqual(-35, Player.Brain.LastSkillCast!.Value.FaithBeforeCast);
        yield return WaitFor(() => Enemy.Vitals.CurrentHp < enemyHp, "潮汐命中");
        Assert.AreEqual(enemyHp - 30f, Enemy.Vitals.CurrentHp);
        Assert.AreEqual(5, Player.Vitals.Faith.Current, "归零之后，技能命中产生新的冠冕事件");
        Assert.AreEqual(1f, Player.Brain.Modifiers.GetCrownLifeStealRatio());
        yield return Recover(Player);
        for (int i = 0; i < 6; i++) yield return Attack(Player, Enemy);
        Assert.AreEqual(35, Player.Vitals.Faith.Current, "正信心来自潮汐命中与后续普攻");
        Assert.AreEqual(79f, Player.Vitals.CurrentHp, "普通攻击不吸血");
        enemyHp = Enemy.Vitals.CurrentHp;
        InputSystem.QueueStateEvent(Keyboard, new KeyboardState(Key.Q));
        yield return WaitFor(() => Player.Brain.LastSkillCast?.Kind == EnumSkillType.Crown, "Q 应释放冠冕");
        InputSystem.QueueStateEvent(Keyboard, new KeyboardState());
        yield return WaitFor(() => Enemy.Vitals.CurrentHp < enemyHp, "冠冕命中");
        Assert.AreEqual(enemyHp - 15f, Enemy.Vitals.CurrentHp);
        Assert.AreEqual(94f, Player.Vitals.CurrentHp, "测试 100% 吸血按实际冠冕伤害恢复 15");
        Assert.AreEqual(5, Player.Vitals.Faith.Current);
    }

    [UnityTest]
    public IEnumerator 手柄RB和Y走统一技能释放_满值按键强化()
    {
        Pair(Player, true);
        Player.Brain.TakeDamage(45f);
        // 本项隔离输入、强化与回血；完整循环另由真实信心事件验证。
        foreach (ModifierEffect effect in Player.Slots.Skills.Tide!.AfterTideEffects)
            Player.Brain.Modifiers.Apply(effect);
        Player.Vitals.Faith!.Update(67 - Player.Vitals.Faith.Current);
        yield return null;
        Assert.IsNull(Player.Brain.LastSkillCast, "满值不能自动释放");
        float hp = Enemy.Vitals.CurrentHp;
        InputSystem.QueueStateEvent(Pad, new GamepadState().WithButton(GamepadButton.RightShoulder));
        yield return WaitFor(() => Player.Brain.LastSkillCast?.Kind == EnumSkillType.Crown, "RB 冠冕");
        InputSystem.QueueStateEvent(Pad, new GamepadState());
        yield return WaitFor(() => Enemy.Vitals.CurrentHp < hp, "强化冠冕命中");
        Assert.AreEqual(hp - 30f, Enemy.Vitals.CurrentHp);
        Assert.AreEqual(85f, Player.Vitals.CurrentHp, "强化冠冕按实际伤害恢复 30");
        Assert.IsTrue(Player.Brain.LastSkillCast!.Value.IsBurst);
        yield return Recover(Player);
        Player.Vitals.Faith.Update(-67 - Player.Vitals.Faith.Current);
        hp = Enemy.Vitals.CurrentHp;
        InputSystem.QueueStateEvent(Pad, new GamepadState().WithButton(GamepadButton.North));
        yield return WaitFor(() => Player.Brain.LastSkillCast?.Kind == EnumSkillType.Tide, "Y 潮汐");
        InputSystem.QueueStateEvent(Pad, new GamepadState());
        yield return WaitFor(() => Enemy.Vitals.CurrentHp < hp, "强化潮汐命中");
        Assert.AreEqual(hp - 60f, Enemy.Vitals.CurrentHp);
        Assert.AreEqual(85f, Player.Vitals.CurrentHp, "潮汐不享受冠冕吸血");
        Assert.IsTrue(Player.Brain.LastSkillCast!.Value.IsBurst);
    }

    [UnityTest]
    public IEnumerator 潮汐后AI持续反击_真实普攻仍可积累冠冕并回血()
    {
        // 隔离潮汐后这一段：前置负信心与失血由实际伤害入口产生。
        for (int i = 0; i < 7; i++) Enemy.Brain.ResolveHit(Player, 3f, true);
        Assert.AreEqual(-35, Player.Vitals.Faith!.Current);
        InputSystem.QueueStateEvent(Keyboard, new KeyboardState(Key.E));
        yield return WaitFor(() => Player.Brain.LastSkillCast?.Kind == EnumSkillType.Tide, "先释放潮汐");
        InputSystem.QueueStateEvent(Keyboard, new KeyboardState());
        yield return Recover(Player);
        AITreeInputSource ai = Enemy.Brain.AiSource!;
        Assert.AreEqual(1.5f, ai.MeleeAttackInterval, "验证保存的演示节奏");
        ai.Target = Player.transform;
        ai.EnableMeleeAttack = true;
        float hpBeforePressure = Player.Vitals.CurrentHp;
        int attacks = 0;
        while (Player.Vitals.Faith.Current < 33 && attacks < 25 && !Player.IsDead
            && Player.Brain.Modifiers.GetCrownLifeStealRatio() > 0f)
        {
            yield return Attack(Player, Enemy);
            attacks++;
        }
        Assert.IsFalse(Player.IsDead);
        Assert.Less(Player.Vitals.CurrentHp, hpBeforePressure, "AI 必须真实造成受击，没有使用 F8 暂停");
        Assert.GreaterOrEqual(Player.Vitals.Faith.Current, 33, "持续反击下应能在测试 Buff 时间内积累正信心");
        Assert.Greater(Player.Brain.Modifiers.GetCrownLifeStealRatio(), 0f);
        float healed = 0f;
        float lastHp = Player.Vitals.CurrentHp;
        Player.Vitals.HpChanged += vitals =>
        {
            if (vitals.CurrentHp > lastHp) healed += vitals.CurrentHp - lastHp;
            lastHp = vitals.CurrentHp;
        };
        InputSystem.QueueStateEvent(Keyboard, new KeyboardState(Key.Q));
        yield return WaitFor(() => Player.Brain.LastSkillCast?.Kind == EnumSkillType.Crown, "冠冕释放");
        InputSystem.QueueStateEvent(Keyboard, new KeyboardState());
        yield return WaitFor(() => healed > 0f, "在 AI 压力下仍应出现实际冠冕回血事件");
        Assert.AreEqual(15f, healed, "有实际失血时普通冠冕应恢复 15，而非仅 3 点");
        Assert.IsTrue(ai.EnableMeleeAttack);
    }



    [UnityTest]
    public IEnumerator 死亡后Menu打开设置_Backspace重置后仍能移动()
    {
        Player.Brain.TakeDamage(1000f);
        Assert.IsTrue(Player.IsDead);
        InputSystem.QueueStateEvent(Pad, new GamepadState().WithButton(GamepadButton.Start));
        yield return null;
        yield return null;
        Assert.IsTrue(GameSettingsController.Instance!.IsOpen);
        Assert.IsTrue(Player.IsDead, "Menu 不再重开演示");
        GameSettingsController.Instance.SetOpen(false);
        InputSystem.QueueStateEvent(Pad, new GamepadState());
        yield return null;
        yield return null;
        yield return ResetFromInput();
        Assert.IsFalse(Player.IsDead);
        Enemy.Brain.AiSource!.EnableMeleeAttack = false;
        Pair(Player, false);
        yield return null;
        Vector3 before = Player.transform.position;
        InputSystem.QueueStateEvent(Keyboard, new KeyboardState(Key.W));
        yield return new WaitForSeconds(.2f);
        InputSystem.QueueStateEvent(Keyboard, new KeyboardState());
        Assert.Greater(Vector3.Distance(before, Player.transform.position), .1f);
    }

    [UnityTest]
    public IEnumerator 武器技能冷却中重开_新场景清空冷却并保留拳头技能()
    {
        new CrownEvent(Player, 1).Invoke();
        InputSystem.QueueStateEvent(Keyboard, new KeyboardState(Key.V));
        yield return WaitFor(() => Player.LastSkillCast.HasValue, "V 应成功释放第三槽技能");
        Assert.AreEqual(EnumSkillType.Weapon, Player.LastSkillCast!.Value.Kind);
        Assert.Greater(Player.Slots.Skills.WeaponCooldownRemaining, 0f);
        InputSystem.QueueStateEvent(Keyboard, new KeyboardState());
        yield return null;
        yield return ResetFromInput();
        Assert.IsNull(Player.LastSkillCast);
        Assert.AreEqual(0f, Player.Slots.Skills.WeaponCooldownRemaining);
        Assert.AreSame(Player.Slots.UnarmedWeaponSkill, Player.Slots.Skills.Weapon);
        Assert.IsTrue(Player.Brain.InputSource is PlayerInputSource);
        Assert.IsTrue(Enemy.Brain.InputSource is AITreeInputSource);
    }

    private IEnumerator ResetFromInput()
    {
        var oldHandle = Scene.handle;
        InputSystem.QueueStateEvent(Keyboard, new KeyboardState(Key.Backspace));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(Keyboard, new KeyboardState());
        InputSystem.QueueStateEvent(Pad, new GamepadState());
        yield return WaitFor(() => !SceneExists(Scene), "重置完成卸载旧场景", 5f);
        Scene = SceneManager.GetSceneByName("SampleScene");
        Assert.IsTrue(Scene.isLoaded);
        Assert.AreNotEqual(oldHandle, Scene.handle);
        FindActors();
        yield return null;
    }

    private IEnumerator Attack(Entity attacker, Entity target)
    {
        float hp = target.Vitals.CurrentHp;
        InputSystem.QueueStateEvent(Mouse, new MouseState().WithButton(MouseButton.Left));
        yield return WaitFor(() => target.Vitals.CurrentHp < hp, "普攻应实际命中");
        InputSystem.QueueStateEvent(Mouse, new MouseState());
        Assert.AreEqual(hp - 3f, target.Vitals.CurrentHp);
        yield return Recover(attacker);
        yield return null; // 确保松开边沿经过一次输入更新。
    }

    private static IEnumerator Recover(Entity entity) => WaitFor(
        () => entity.Brain.StateMachine.GetActive(EnumStateLayer.Action) == null, "后摇应结束");

    private static bool SceneExists(Scene scene)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i) == scene) return true;
        return false;
    }

    private static IEnumerator WaitFor(System.Func<bool> predicate, string message, float timeout = 3f)
    {
        float elapsed = 0f;
        while (!predicate() && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        Assert.IsTrue(predicate(), message);
    }

    private void FindActors()
    {
        GameObject[] roots = Scene.GetRootGameObjects();
        Player = System.Array.Find(roots, root => root.name == "Player").GetComponent<Entity>();
        Enemy = System.Array.Find(roots, root => root.name == "Enemy").GetComponent<Entity>();
    }

    private void Pair(Entity entity, bool gamepad)
    {
        PlayerInput input = entity.GetComponent<PlayerInput>();
        input.neverAutoSwitchControlSchemes = true;
        if (gamepad) input.SwitchCurrentControlScheme("Gamepad", Pad);
        else input.SwitchCurrentControlScheme("Keyboard&Mouse", Keyboard, Mouse);
    }

    private void PlaceActors()
    {
        Teleport(Player, Player.transform.position, Quaternion.identity);
        Teleport(Enemy, Player.transform.position + Vector3.forward * 1.3f, Quaternion.Euler(0f, 180f, 0f));
        Physics.SyncTransforms();
    }

    private static void Teleport(Entity entity, Vector3 position, Quaternion rotation)
    {
        CharacterController controller = entity.GetComponent<CharacterController>();
        controller.enabled = false;
        entity.transform.SetPositionAndRotation(position, rotation);
        controller.enabled = true;
    }
}
