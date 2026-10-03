using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class GameSettingsTests
{
    private sealed class MemoryStore : IBindingOverrideStore
    {
        public string Json = "";
        public bool Fail;
        public string Load() => Json;
        public void Save(string json) { if (Fail) throw new InvalidOperationException(); Json = json; }
    }
    private sealed class FakeExit : IGameExit { public int Calls; public void Quit() => Calls++; }
    private Keyboard Keys = null!;
    private Mouse Mouse = null!;
    private Gamepad Pad = null!;
    private GameSettingsController Settings = null!;
    private MemoryStore Store = null!;
    private SimulatedInputFocusScope Focus = null!;
    private Scene Scene;
    private Entity Player = null!;
    private Entity Enemy = null!;
    private CameraRig Rig = null!;
    private InputActionAsset Template = null!;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        Focus = new SimulatedInputFocusScope();
        Keys = InputSystem.AddDevice<Keyboard>();
        Mouse = InputSystem.AddDevice<Mouse>();
        Pad = InputSystem.AddDevice<Gamepad>();
        Settings = GameSettingsController.Instance!;
        Settings.SetOpen(false);
        Store = new MemoryStore();
        yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Additive);
        Scene = SceneManager.GetSceneByName("SampleScene");
        GameObject[] roots = Scene.GetRootGameObjects();
        Player = Array.Find(roots, root => root.name == "Player").GetComponent<Entity>();
        Enemy = Array.Find(roots, root => root.name == "Enemy").GetComponent<Entity>();
        Rig = Array.Find(roots, root => root.name == "Main Camera").GetComponent<CameraRig>();
        Template = Rig.InputActionAsset!;
        Settings.ConfigureBindings(Template, Store);
        Enemy.Brain.AiSource!.EnableMeleeAttack = false;
        Enemy.Brain.AiSource.Target = null;
        Player.Brain.AiSource!.Target = null;
        Pair(Player);
        DebugSystem.SetEnabled(false);
        yield return null;
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        Settings.SetOpen(false);
        Settings.Exit = new PlatformGameExit();
        if (Scene.IsValid() && Scene.isLoaded) yield return SceneManager.UnloadSceneAsync(Scene);
        Settings.ConfigureBindings(Template, new PlayerPrefsBindingOverrideStore());
        InputSystem.RemoveDevice(Keys);
        InputSystem.RemoveDevice(Mouse);
        InputSystem.RemoveDevice(Pad);
        DebugSystem.SetEnabled(false);
        Focus.Dispose();
        yield return null;
        yield return null;
    }

    private InputBindingSettings.Entry Entry(string name, string group = "Keyboard&Mouse", int occurrence = 0)
    {
        foreach (var entry in Settings.Bindings!.GetEntries(group))
            if (Settings.Bindings.Actions.FindAction(entry.ActionId.ToString()).name == name && occurrence-- == 0) return entry;
        throw new InvalidOperationException(name);
    }

    private Button Button(string name)
    {
        foreach (Button button in Settings.GetComponentsInChildren<Button>(true))
            if (button.name == name && button.gameObject.activeInHierarchy) return button;
        throw new InvalidOperationException(name);
    }

    private void Pair(Entity entity)
    {
        PlayerInput input = entity.GetComponent<PlayerInput>();
        input.neverAutoSwitchControlSchemes = true;
        input.SwitchCurrentControlScheme("Keyboard&Mouse", Keys, Mouse);
    }

    private IEnumerator Click(Button button)
    {
        var corners = new Vector3[4];
        button.GetComponent<RectTransform>().GetWorldCorners(corners);
        Vector2 center = (corners[0] + corners[2]) * .5f;
        InputSystem.QueueStateEvent(Mouse, new MouseState { position = center });
        yield return null;
        InputSystem.QueueStateEvent(Mouse, new MouseState { position = center }.WithButton(MouseButton.Left));
        yield return null;
        InputSystem.QueueStateEvent(Mouse, new MouseState { position = center });
        yield return null;
    }

    [UnityTest]
    public IEnumerator Esc菜单暂停战斗和调试_鼠标解锁_关闭不残留攻击()
    {
        Time.timeScale = .75f;
        Vector3 before = Player.transform.position;
        InputSystem.QueueStateEvent(Keys, new KeyboardState(Key.Escape));
        yield return null;
        yield return null;
        Assert.IsTrue(Settings.IsOpen);
        Assert.AreEqual(0f, Time.timeScale);
        Assert.AreEqual(CursorLockMode.None, Cursor.lockState);
        InputSystem.QueueStateEvent(Keys, new KeyboardState(Key.W, Key.Space, Key.F12, Key.F8, Key.Backspace));
        InputSystem.QueueStateEvent(Mouse, new MouseState().WithButton(MouseButton.Left));
        yield return new WaitForSecondsRealtime(.15f);
        Assert.AreEqual(before, Player.transform.position);
        Assert.IsNull(Player.Brain.StateMachine.GetActive(EnumStateLayer.Action));
        Assert.IsFalse(DebugSystem.IsEnabled);
        Assert.IsTrue(Scene.isLoaded);
        Button("ResumeGame").onClick.Invoke();
        InputSystem.QueueStateEvent(Keys, new KeyboardState());
        InputSystem.QueueStateEvent(Mouse, new MouseState());
        yield return null;
        yield return null;
        yield return null;
        Assert.AreEqual(.75f, Time.timeScale);
        Assert.IsNull(Player.Brain.StateMachine.GetActive(EnumStateLayer.Action));
        Assert.IsFalse(Player.Commands.AttackQueued);
        Time.timeScale = 1f;
    }

    [UnityTest]
    public IEnumerator UI实际改绑_新键生效_保存加载_恢复默认_共享模板不修改()
    {
        string original = Template.SaveBindingOverridesAsJson();
        Settings.SetOpen(true);
        Button("Binding 跳跃").onClick.Invoke();
        Assert.IsTrue(Settings.Bindings!.IsRebinding);
        InputSystem.QueueStateEvent(Keys, new KeyboardState(Key.J));
        yield return new WaitForSecondsRealtime(.25f);
        Assert.IsFalse(Settings.Bindings.IsRebinding);
        var jump = Entry("Jump");
        Assert.AreEqual("<Keyboard>/j", Settings.Bindings.Actions.FindAction(jump.ActionId.ToString()).bindings[jump.Index].effectivePath);
        Assert.IsNotEmpty(Store.Json);
        Assert.AreEqual(original, Template.SaveBindingOverridesAsJson());
        InputSystem.QueueStateEvent(Keys, new KeyboardState());
        Button("ResumeGame").onClick.Invoke();
        yield return new WaitForSecondsRealtime(.1f);
        Assert.IsTrue(Player.Motor.IsGrounded);
        InputSystem.QueueStateEvent(Keys, new KeyboardState(Key.J));
        yield return null;
        yield return null;
        Assert.Greater(Player.Motor.VerticalVelocity, 0f);
        InputSystem.QueueStateEvent(Keys, new KeyboardState());
        Settings.ConfigureBindings(Template, Store);
        Assert.AreEqual("<Keyboard>/j", Settings.Bindings!.Actions.FindAction("Jump").bindings[0].effectivePath);
        Settings.SetOpen(true);
        Button("RestoreDefaults").onClick.Invoke();
        Assert.AreEqual("<Keyboard>/space", Player.GetComponent<PlayerInput>().actions.FindAction("Jump").bindings[0].effectivePath);
    }

    [UnityTest]
    public IEnumerator 改绑冲突保留旧键_Esc取消不关菜单_Menu也能取消()
    {
        Settings.SetOpen(true);
        var jump = Entry("Jump");
        Assert.IsFalse(Settings.Bindings!.TryRebind(jump, "<Keyboard>/q", "Keyboard&Mouse"));
        Assert.IsFalse(Settings.Bindings.TryRebind(jump, "<Keyboard>/escape", "Keyboard&Mouse"));
        Assert.IsFalse(Settings.Bindings.TryRebind(jump, "<Gamepad>/buttonEast", "Keyboard&Mouse"));
        Assert.IsFalse(Settings.Bindings.TryRebind(jump, "<Mouse>/delta", "Keyboard&Mouse"));
        Assert.AreEqual("<Keyboard>/space", Settings.Bindings.Actions.FindAction("Jump").bindings[0].effectivePath);
        Button("Binding 跳跃").onClick.Invoke();
        InputSystem.QueueStateEvent(Keys, new KeyboardState(Key.Escape));
        yield return null;
        yield return null;
        Assert.IsFalse(Settings.Bindings.IsRebinding);
        Assert.IsTrue(Settings.IsOpen);
        InputSystem.QueueStateEvent(Keys, new KeyboardState());
        yield return null;
        Button("GamepadTab").onClick.Invoke();
        Button("Binding 跳跃").onClick.Invoke();
        InputSystem.QueueStateEvent(Pad, new GamepadState().WithButton(GamepadButton.Start));
        yield return null;
        yield return null;
        Assert.IsFalse(Settings.Bindings.IsRebinding);
        Assert.IsTrue(Settings.IsOpen);
        InputSystem.QueueStateEvent(Pad, new GamepadState());
    }

    [UnityTest]
    public IEnumerator 绑定同步相机和附身身体_新实例也获得存档_保存失败回滚()
    {
        var shoulder = Entry("SwitchShoulder");
        Assert.IsTrue(Settings.Bindings!.TryRebind(shoulder, "<Keyboard>/g", "Keyboard&Mouse"));
        var jump = Entry("Jump");
        Assert.IsTrue(Settings.Bindings.TryRebind(jump, "<Keyboard>/j", "Keyboard&Mouse"));
        Store.Fail = true;
        Assert.IsFalse(Settings.Bindings.TryRebind(jump, "<Keyboard>/k", "Keyboard&Mouse"));
        Assert.AreEqual("<Keyboard>/j", Player.GetComponent<PlayerInput>().actions.FindAction("Jump").bindings[0].effectivePath);
        Store.Fail = false;
        int side = Rig.CurrentShoulderSide;
        InputSystem.QueueStateEvent(Keys, new KeyboardState(Key.G));
        yield return null;
        yield return null;
        Assert.AreEqual(-side, Rig.CurrentShoulderSide);
        InputSystem.QueueStateEvent(Keys, new KeyboardState());
        PlayerInputSource source = Player.Brain.PlayerSource!;
        Assert.IsTrue(Player.Brain.TryBeginPossession(Enemy, source.DebugPossessionEffect, source.DebugSoulOutEffect));
        yield return null;
        yield return null;
        Pair(Enemy);
        Assert.AreEqual("<Keyboard>/j", Enemy.GetComponent<PlayerInput>().actions.FindAction("Jump").bindings[0].effectivePath);
        using var reload = new InputBindingSettings(Template, Store);
        InputActionAsset newCopy = UnityEngine.Object.Instantiate(Template);
        reload.Register(newCopy);
        Assert.AreEqual("<Keyboard>/j", newCopy.FindAction("Jump").bindings[0].effectivePath);
        UnityEngine.Object.Destroy(newCopy);
        Enemy.Brain.Modifiers.Tick(11f);
        yield return null;
        Assert.IsTrue(Player.Brain.InputSource is PlayerInputSource);
        Assert.AreEqual("<Keyboard>/j", Player.GetComponent<PlayerInput>().actions.FindAction("Jump").bindings[0].effectivePath);
        var oldHandle = Scene.handle;
        InputSystem.QueueStateEvent(Keys, new KeyboardState(Key.Backspace));
        float deadline = Time.realtimeSinceStartup + 5f;
        while (Scene.isLoaded && Time.realtimeSinceStartup < deadline) yield return null;
        InputSystem.QueueStateEvent(Keys, new KeyboardState());
        Assert.IsFalse(Scene.isLoaded, "演示重开卸载旧场景");
        Scene = SceneManager.GetSceneByName("SampleScene");
        Assert.IsTrue(Scene.isLoaded);
        Assert.AreNotEqual(oldHandle, Scene.handle);
        GameObject[] roots = Scene.GetRootGameObjects();
        Entity newPlayer = Array.Find(roots, root => root.name == "Player").GetComponent<Entity>();
        Assert.AreEqual("<Keyboard>/j", newPlayer.GetComponent<PlayerInput>().actions.FindAction("Jump").bindings[0].effectivePath,
            "真实场景重开后保留改绑");
    }

    [UnityTest]
    public IEnumerator 手柄改绑_二维摇杆类型校验_退出按钮需二次确认()
    {
        Settings.SetOpen(true);
        Button("GamepadTab").onClick.Invoke();
        Button("Binding 跳跃").onClick.Invoke();
        InputSystem.QueueStateEvent(Pad, new GamepadState().WithButton(GamepadButton.East));
        yield return new WaitForSecondsRealtime(.25f);
        Assert.IsFalse(Settings.Bindings!.IsRebinding);
        Assert.AreEqual("<Gamepad>/buttonEast", Settings.Bindings.Actions.FindAction("Jump").bindings[1].effectivePath, Settings.Bindings.Status);
        Assert.IsFalse(Settings.Bindings.TryRebind(Entry("Look", "Gamepad"), "<Gamepad>/buttonSouth", "Gamepad"));
        Assert.IsTrue(Settings.Bindings.TryRebind(Entry("SwitchShoulder", "Gamepad"), "<Gamepad>/dpad/up", "Gamepad"));
        Settings.SetOpen(false);
        Player.GetComponent<PlayerInput>().SwitchCurrentControlScheme("Gamepad", Pad);
        InputSystem.QueueStateEvent(Pad, new GamepadState());
        yield return null;
        yield return null;
        yield return null;
        Rig.CurrentShoulderSide = 1;
        InputSystem.QueueStateEvent(Pad, new GamepadState().WithButton(GamepadButton.DpadUp));
        yield return null;
        yield return null;
        Assert.AreEqual(-1, Rig.CurrentShoulderSide, "重绑左肩槽仍是选左肩，不退化为切换");
        InputSystem.QueueStateEvent(Pad, new GamepadState());
        Settings.SetOpen(true);
        var exit = new FakeExit();
        Settings.Exit = exit;
        InputSystem.QueueStateEvent(Pad, new GamepadState());
        yield return null;
        yield return null;
        yield return Click(Button("RequestQuit"));
        Assert.IsTrue(Settings.QuitConfirmation);
        Assert.AreEqual(0, exit.Calls);
        yield return Click(Button("CancelQuit"));
        Assert.IsFalse(Settings.QuitConfirmation);
        yield return Click(Button("RequestQuit"));
        yield return Click(Button("ConfirmQuit"));
        Assert.AreEqual(1, exit.Calls);
        InputSystem.QueueStateEvent(Pad, new GamepadState());
    }

    [UnityTest]
    public IEnumerator 损坏存档回退_同步不扩大已启用动作范围()
    {
        Store.Json = "broken json";
        using var bindings = new InputBindingSettings(Template, Store);
        Assert.AreEqual("<Keyboard>/space", bindings.Actions.FindAction("Jump").bindings[0].effectivePath);
        StringAssert.Contains("无法读取", bindings.Status);
        InputActionAsset copy = UnityEngine.Object.Instantiate(Template);
        copy.Disable();
        copy.FindAction("Look").Enable();
        bindings.Register(copy);
        Assert.IsTrue(copy.FindAction("Look").enabled);
        Assert.IsFalse(copy.FindAction("Attack").enabled);
        UnityEngine.Object.Destroy(copy);
        yield return null;
    }
}
