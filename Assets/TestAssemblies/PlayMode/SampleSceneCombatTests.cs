using NUnit.Framework;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class SampleSceneCombatTests
{
    [UnityTearDown]
    public IEnumerator UnloadTestScene()
    {
        Scene scene = SceneManager.GetSceneByName("SampleScene");
        if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
    }

    [UnityTest]
    public IEnumerator 鼠标攻击真实扣血_附身后命中原角色信心降低() => VerifyInputAttack(false);

    [UnityTest]
    public IEnumerator 手柄攻击真实扣血_附身后命中原角色信心降低() => VerifyInputAttack(true);

    private static IEnumerator VerifyInputAttack(bool useGamepad)
    {
        using var focus = new SimulatedInputFocusScope();
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
        yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Additive);
        yield return null;
        Scene scene = SceneManager.GetSceneByName("SampleScene");
        try
        {
            GameObject[] roots = scene.GetRootGameObjects();
            Entity player = System.Array.Find(roots, go => go.name == "Player").GetComponent<Entity>();
            Entity enemy = System.Array.Find(roots, go => go.name == "Enemy").GetComponent<Entity>();
            Assert.AreEqual(1, player.Config!.FactionId);
            Assert.AreEqual(2, enemy.Config!.FactionId);
            enemy.Brain.AiSource!.Target = null;
            enemy.Brain.AiSource.EnableMeleeAttack = false; // 只隔离输入测试，不修改共享资产。
            PlayerInput input = player.GetComponent<PlayerInput>();
            input.neverAutoSwitchControlSchemes = true;
            Pair(input, keyboard, mouse, gamepad, useGamepad);
            Teleport(enemy, player.transform.position + Vector3.forward * 1.5f, Quaternion.Euler(0f, 180f, 0f));
            player.transform.rotation = Quaternion.identity;
            Physics.SyncTransforms();
            yield return new WaitForSeconds(.25f);
            float enemyBefore = enemy.Vitals.CurrentHp;
            PressAttack(mouse, gamepad, useGamepad, true);
            yield return WaitForHit(enemy, enemyBefore);
            PressAttack(mouse, gamepad, useGamepad, false);
            InputAction attackAction = input.actions.FindAction("Attack");
            Assert.AreEqual(enemyBefore - 3f, enemy.Vitals.CurrentHp,
                $"source={player.Brain.InputSource?.GetType().Name}, action={player.Brain.StateMachine.GetActive(EnumStateLayer.Action)?.StateName}, " +
                $"phase={player.Brain.AttackState.Phase}, scheme={input.currentControlScheme}, attackEnabled={attackAction.enabled}, " +
                $"attackValue={attackAction.ReadValue<float>()}, mousePressed={mouse.leftButton.isPressed}, " +
                $"player={player.transform.position}, enemy={enemy.transform.position}, forward={player.transform.forward}");
            Assert.AreEqual(5, player.Vitals.Faith!.Current);
            yield return WaitForRecovery(player);

            if (useGamepad) InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.LeftShoulder));
            else InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.V));
            float possessionWait = 0f;
            while (!(enemy.Brain.InputSource is PlayerInputSource) && possessionWait < 2f)
            {
                possessionWait += Time.deltaTime;
                yield return null;
            }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.QueueStateEvent(gamepad, new GamepadState());
            InputAction possessAction = input.actions.FindAction("Possess");
            Assert.IsTrue(enemy.Brain.InputSource is PlayerInputSource,
                $"scheme={input.currentControlScheme}, devices={string.Join(",", input.devices)}, " +
                $"possessEnabled={possessAction.enabled}, possessValue={possessAction.ReadValue<float>()}, " +
                $"V={keyboard.vKey.isPressed}, canUse={player.Brain.Capability.CanUseSkill()}, " +
                $"candidate={EntityBrain.FindPossessionCandidate(player)?.name}, " +
                $"source={player.Brain.InputSource?.GetType().Name}, soulOut={player.Brain.HasSoulOut}");
            Assert.IsTrue(player.Brain.HasSoulOut);
            Assert.AreEqual(1, player.Config.FactionId, "附身不改变本体阵营");
            Assert.AreEqual(2, enemy.Config.FactionId);
            Pair(enemy.GetComponent<PlayerInput>(), keyboard, mouse, gamepad, useGamepad);
            float playerBefore = player.Vitals.CurrentHp;
            int faithBefore = player.Vitals.Faith.Current;
            PressAttack(mouse, gamepad, useGamepad, true);
            yield return WaitForHit(player, playerBefore);
            PressAttack(mouse, gamepad, useGamepad, false);
            Assert.AreEqual(playerBefore - 3f, player.Vitals.CurrentHp);
            Assert.AreEqual(faithBefore - 5, player.Vitals.Faith.Current);
            Assert.AreEqual(0, enemy.Vitals.Faith!.Current, "本体先受击 -5，再命中敌方本体 +5");
        }
        finally
        {
            InputSystem.RemoveDevice(keyboard);
            InputSystem.RemoveDevice(mouse);
            InputSystem.RemoveDevice(gamepad);
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator 演示敌人追击后自主攻击_同样走信心事件()
    {
        yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Additive);
        yield return null;
        Scene scene = SceneManager.GetSceneByName("SampleScene");
        try
        {
            GameObject[] roots = scene.GetRootGameObjects();
            Entity player = System.Array.Find(roots, go => go.name == "Player").GetComponent<Entity>();
            Entity enemy = System.Array.Find(roots, go => go.name == "Enemy").GetComponent<Entity>();
            player.Brain.BindInputSource(player.Brain.AiSource); // 站桩隔离真实设备输入。
            player.Brain.AiSource!.Target = null;
            Assert.IsTrue(enemy.Brain.AiSource!.EnableMeleeAttack);
            float before = player.Vitals.CurrentHp;
            float elapsed = 0f;
            while (player.Vitals.CurrentHp == before && elapsed < 5f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            Assert.Less(player.Vitals.CurrentHp, before, "AI 应追击并在近距离请求普通攻击");
            Assert.Less(player.Vitals.Faith!.Current, 0);
            Assert.Greater(enemy.Vitals.Faith!.Current, 0);
        }
        finally
        {
            // UnityTearDown 即使断言失败也等待卸载，防止下一项读到同名旧场景。
        }
        yield return null;
    }

    private static void Pair(PlayerInput input, Keyboard keyboard, Mouse mouse, Gamepad gamepad, bool useGamepad)
    {
        input.neverAutoSwitchControlSchemes = true;
        if (useGamepad) input.SwitchCurrentControlScheme("Gamepad", gamepad);
        else input.SwitchCurrentControlScheme("Keyboard&Mouse", keyboard, mouse);
    }

    private static IEnumerator WaitForHit(Entity target, float before)
    {
        float elapsed = 0f;
        while (target.Vitals.CurrentHp == before && elapsed < 2f)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private static IEnumerator WaitForRecovery(Entity actor)
    {
        float elapsed = 0f;
        while (actor.Brain.StateMachine.GetActive(EnumStateLayer.Action) != null && elapsed < 2f)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        Assert.IsNull(actor.Brain.StateMachine.GetActive(EnumStateLayer.Action), "攻击应在后摇后结束");
    }

    private static void PressAttack(Mouse mouse, Gamepad gamepad, bool useGamepad, bool pressed)
    {
        if (useGamepad) InputSystem.QueueStateEvent(gamepad, pressed ? new GamepadState().WithButton(GamepadButton.West) : new GamepadState());
        else InputSystem.QueueStateEvent(mouse, pressed ? new MouseState().WithButton(MouseButton.Left) : new MouseState());
    }

    private static void Teleport(Entity entity, Vector3 position, Quaternion rotation)
    {
        CharacterController controller = entity.GetComponent<CharacterController>();
        controller.enabled = false;
        entity.transform.SetPositionAndRotation(position, rotation);
        controller.enabled = true;
    }
}
