using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class DebugPresentationTests
{
    [UnityTearDown]
    public IEnumerator UnloadTestScene()
    {
        Scene scene = SceneManager.GetSceneByName("SampleScene");
        if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        DebugSystem.SetEnabled(false);
    }

    [UnityTest]
    public IEnumerator 键盘V_Debug关闭时也能附身()
    {
        return VerifyPossessionInput(useGamepad: false);
    }

    [UnityTest]
    public IEnumerator 手柄LB_Debug关闭时也能附身()
    {
        return VerifyPossessionInput(useGamepad: true);
    }

    private static IEnumerator VerifyPossessionInput(bool useGamepad)
    {
        using var focus = new SimulatedInputFocusScope();
        DebugSystem.SetEnabled(false);
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
            PlayerInput input = player.GetComponent<PlayerInput>();
            input.neverAutoSwitchControlSchemes = true;
            InputAction action = input.actions.FindAction("Player/Possess", throwIfNotFound: true);
            Assert.IsTrue(System.Array.Exists(action.bindings.ToArray(), binding => binding.path == "<Keyboard>/v"));
            Assert.IsTrue(System.Array.Exists(action.bindings.ToArray(), binding => binding.path == "<Gamepad>/leftShoulder"));
            if (useGamepad)
            {
                input.SwitchCurrentControlScheme("Gamepad", gamepad);
                InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.LeftShoulder));
            }
            else
            {
                input.SwitchCurrentControlScheme("Keyboard&Mouse", keyboard, mouse);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.V));
            }
            yield return null;
            yield return null;
            Assert.IsTrue(player.Brain.HasSoulOut);
            Assert.IsTrue(enemy.Brain.InputSource is PlayerInputSource);
            Assert.IsFalse(DebugSystem.IsEnabled);
            InputSystem.QueueStateEvent(gamepad, new GamepadState());
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }
        finally
        {
            // UnityTearDown 即使断言失败也等待卸载。
            InputSystem.RemoveDevice(keyboard);
            InputSystem.RemoveDevice(mouse);
            InputSystem.RemoveDevice(gamepad);
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator Debug开关_双方头顶面板跟随并实时更新()
    {
        yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Additive);
        yield return null;
        Scene scene = SceneManager.GetSceneByName("SampleScene");
        try
        {
            GameObject[] roots = scene.GetRootGameObjects();
            Entity player = System.Array.Find(roots, go => go.name == "Player").GetComponent<Entity>();
            Entity enemy = System.Array.Find(roots, go => go.name == "Enemy").GetComponent<Entity>();
            EntityVisual playerVisual = player.GetComponent<EntityVisual>();
            EntityVisual enemyVisual = enemy.GetComponent<EntityVisual>();
            Camera camera = System.Array.Find(roots, go => go.name == "Main Camera").GetComponent<Camera>();
            DebugSystem.SetEnabled(false);
            Assert.IsFalse(playerVisual.IsDebugPanelVisible);
            Assert.IsFalse(enemyVisual.IsDebugPanelVisible);
            DebugSystem.SetEnabled(true);
            Assert.IsTrue(playerVisual.IsDebugPanelVisible);
            Assert.IsTrue(enemyVisual.IsDebugPanelVisible);
            yield return null;
            yield return null;
            Assert.IsTrue(GameObject.Find("Player Debug Panel").GetComponent<Canvas>().enabled);
            Assert.IsTrue(GameObject.Find("Enemy Debug Panel").GetComponent<Canvas>().enabled);
            Assert.IsFalse(playerVisual.DebugPanelScreenRect.Overlaps(enemyVisual.DebugPanelScreenRect));
            enemy.Brain.AiSource!.Target = null;
            enemy.transform.position = player.transform.position + camera.transform.right;
            Physics.SyncTransforms();
            Assert.IsTrue(enemyVisual.TryGetDebugPanelRect(camera, new Vector2(100f, 50f), out Rect first));
            Vector3 anchor = enemyVisual.DebugAnchor;
            enemy.transform.position += camera.transform.right;
            Physics.SyncTransforms();
            Assert.Greater(Vector3.Distance(anchor, enemyVisual.DebugAnchor), 0.9f);
            Assert.IsTrue(enemyVisual.TryGetDebugPanelRect(camera, new Vector2(100f, 50f), out Rect second));
            Assert.Greater(second.x, first.x);
            string before = enemyVisual.GetDebugText();
            enemy.Brain.TakeDamage(1f);
            Assert.AreNotEqual(before, enemyVisual.GetDebugText());
            Assert.That(enemyVisual.GetDebugText(), Does.Contain("生命").And.Contain("状态机").And.Contain("护甲"));
            enemy.transform.position = camera.transform.position - camera.transform.forward * 10f;
            Physics.SyncTransforms();
            Assert.IsFalse(enemyVisual.TryGetDebugPanelRect(camera, new Vector2(100f, 50f), out _));
            DebugSystem.SetEnabled(false);
            Assert.IsFalse(enemyVisual.TryGetDebugPanelRect(camera, new Vector2(100f, 50f), out _));
        }
        finally
        {
            DebugSystem.SetEnabled(false);
            // UnityTearDown 等待卸载，包含失败路径。
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator F12和手柄Back_统一切换且松开不再次切换()
    {
        using var focus = new SimulatedInputFocusScope();
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
        DebugSystem.SetEnabled(false);
        try
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F12));
            yield return null;
            yield return null;
            Assert.IsTrue(DebugSystem.IsEnabled);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            Assert.IsTrue(DebugSystem.IsEnabled);
            InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.Select));
            yield return null;
            yield return null;
            Assert.IsFalse(DebugSystem.IsEnabled);
        }
        finally
        {
            DebugSystem.SetEnabled(false);
            InputSystem.RemoveDevice(keyboard);
            InputSystem.RemoveDevice(gamepad);
        }
        yield return null;
    }
}
