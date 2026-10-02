using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class PossessionCameraInputTests
{
    [UnityTest]
    public IEnumerator 鼠标转向和切肩_两次附身返回后仍可使用()
    {
        return VerifyRoundTrips(useGamepad: false);
    }

    [UnityTest]
    public IEnumerator 手柄右摇杆和切肩_两次附身返回后仍可使用()
    {
        return VerifyRoundTrips(useGamepad: true);
    }

    private static IEnumerator VerifyRoundTrips(bool useGamepad)
    {
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
            CameraRig playerRig = System.Array.Find(roots, go => go.name == "Main Camera").GetComponent<CameraRig>();
            CameraRig enemyRig = System.Array.Find(roots, go => go.name == "Enemy Camera").GetComponent<CameraRig>();
            PlayerInput input = player.GetComponent<PlayerInput>();
            input.neverAutoSwitchControlSchemes = true;
            if (useGamepad) input.SwitchCurrentControlScheme("Gamepad", gamepad);
            else input.SwitchCurrentControlScheme("Keyboard&Mouse", keyboard, mouse);
            yield return null;
            yield return VerifyLook(playerRig, mouse, gamepad, useGamepad, "附身前");
            for (int cycle = 0; cycle < 2; cycle++)
            {
                PlayerInputSource source = player.Brain.PlayerSource!;
                Assert.IsTrue(player.Brain.TryBeginPossession(enemy, source.DebugPossessionEffect, source.DebugSoulOutEffect));
                yield return null;
                yield return null;
                enemy.Brain.Modifiers.Tick(11f);
                yield return null;
                yield return null;
                Assert.IsTrue(player.Brain.InputSource is PlayerInputSource);
                Assert.IsTrue(playerRig.GetComponent<Camera>().enabled);
                Assert.IsFalse(enemyRig.GetComponent<Camera>().enabled);
                Assert.IsTrue(System.Array.Exists(input.devices.ToArray(), device => device == (useGamepad ? (InputDevice)gamepad : mouse)),
                    "返回后应保留原设备配对");
                yield return VerifyLook(playerRig, mouse, gamepad, useGamepad, $"第 {cycle + 1} 次返回");
                if (useGamepad) playerRig.CurrentShoulderSide = -1;
                int oldPlayerSide = playerRig.CurrentShoulderSide;
                int oldEnemySide = enemyRig.CurrentShoulderSide;
                if (useGamepad)
                    InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.DpadRight));
                else
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F1));
                yield return null;
                yield return null;
                Assert.AreEqual(useGamepad ? 1 : -oldPlayerSide, playerRig.CurrentShoulderSide);
                Assert.AreEqual(oldEnemySide, enemyRig.CurrentShoulderSide, "未激活的相机不应响应切肩");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.QueueStateEvent(gamepad, new GamepadState());
                yield return null;
                if (useGamepad)
                    InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.RightStick));
                else
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F2));
                yield return new WaitForSeconds(0.6f);
                Vector3 eye = player.transform.position + Vector3.up * playerRig.FirstPersonEyeHeight;
                if (cycle == 0)
                    Assert.Less(Vector3.Distance(playerRig.transform.position, eye), 1f, "返回后仍能切到第一人称");
                else
                    Assert.Greater(Vector3.Distance(playerRig.transform.position, eye), 3f, "第二次返回后仍能切回第三人称");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.QueueStateEvent(gamepad, new GamepadState());
                yield return null;
            }
        }
        finally
        {
            SceneManager.UnloadSceneAsync(scene);
            InputSystem.RemoveDevice(keyboard);
            InputSystem.RemoveDevice(mouse);
            InputSystem.RemoveDevice(gamepad);
        }
        yield return null;
    }

    private static IEnumerator VerifyLook(CameraRig rig, Mouse mouse, Gamepad gamepad, bool useGamepad, string stage)
    {
        Quaternion before = rig.transform.rotation;
        if (useGamepad)
            InputSystem.QueueStateEvent(gamepad, new GamepadState { rightStick = new Vector2(0.8f, 0f) });
        else
            InputSystem.QueueDeltaStateEvent(mouse.delta, new Vector2(80f, 0f));
        yield return new WaitForSeconds(0.05f);
        var localAsset = typeof(CameraRig).GetField("LocalInputActions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(rig) as InputActionAsset;
        Assert.Greater(Quaternion.Angle(before, rig.transform.rotation), 0.1f,
            $"{stage} 无法转向；鼠标={mouse.deviceId}，手柄={gamepad.deviceId}，角色设备={string.Join(",", rig.FollowEntity!.GetComponent<PlayerInput>().devices)}，相机输入启用={localAsset?.FindAction("MouseLook")?.enabled}/{localAsset?.FindAction("Look")?.enabled}");
        InputSystem.QueueStateEvent(gamepad, new GamepadState());
        yield return null;
    }
}
