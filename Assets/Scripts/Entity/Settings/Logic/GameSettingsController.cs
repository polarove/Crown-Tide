using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public interface IGameExit
{
    void Quit();
}

public sealed class PlatformGameExit : IGameExit
{
    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}

/// <summary>当前单机演示的设置会话。UI 请求入口，负责暂停、绑定及平台退出。</summary>
[DefaultExecutionOrder(-200)]
public sealed class GameSettingsController : MonoBehaviour
{
    public static GameSettingsController? Instance { get; private set; }
    public static bool IsGameplayInputBlocked => Instance != null && (Instance.IsOpen || Time.frameCount <= Instance.ResumeAfterFrame);
    public bool IsOpen { get; private set; }
    public bool QuitConfirmation { get; private set; }
    public InputBindingSettings? Bindings { get; private set; }
    public IGameExit Exit { private get; set; } = new PlatformGameExit();
    public event Action? Changed;
    private readonly HashSet<InputActionAsset> RuntimeInputs = new();
    private InputAction Menu = null!;
    private float PreviousTimeScale;
    private CursorLockMode PreviousCursorLock;
    private bool PreviousCursorVisible;
    private int ResumeAfterFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        new GameObject("Game Settings").AddComponent<GameSettingsController>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Menu = new InputAction("SettingsMenu", InputActionType.Button);
        Menu.AddBinding("<Keyboard>/escape");
        Menu.AddBinding("<Gamepad>/start");
        Menu.Enable();
        gameObject.AddComponent<GameSettingsPanel>();
    }

    public void ConfigureBindings(InputActionAsset template, IBindingOverrideStore store)
    {
        if (Bindings != null) { Bindings.Changed -= OnBindingsChanged; Bindings.Dispose(); }
        Bindings = new InputBindingSettings(template, store);
        Bindings.Changed += OnBindingsChanged;
        RuntimeInputs.RemoveWhere(instance => instance == null);
        foreach (InputActionAsset instance in RuntimeInputs) Bindings.Register(instance);
        Changed?.Invoke();
    }

    private void OnBindingsChanged() => Changed?.Invoke();

    public static void RegisterInput(InputActionAsset actions)
    {
        if (Instance == null) Bootstrap();
        // 工程级 actions 可能是默认 UI 表，使用角色／相机实际注入的玩法表。
        if (Instance!.Bindings == null && actions.FindAction("Player/CrownSkill") != null
            && actions.FindAction("Player/SwitchShoulder") != null)
            Instance.ConfigureBindings(actions, new PlayerPrefsBindingOverrideStore());
        Instance!.RuntimeInputs.Add(actions);
        Instance.Bindings?.Register(actions);
    }

    public static void UnregisterInput(InputActionAsset actions)
    {
        if (Instance == null) return;
        Instance.RuntimeInputs.Remove(actions);
        Instance.Bindings?.Unregister(actions);
    }

    public void SetOpen(bool open)
    {
        if (IsOpen == open) return;
        if (open)
        {
            PreviousTimeScale = Time.timeScale;
            PreviousCursorLock = Cursor.lockState;
            PreviousCursorVisible = Cursor.visible;
            IsOpen = true;
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Bindings?.CancelRebind();
            IsOpen = false;
            QuitConfirmation = false;
            RestoreSession();
            // 关闭按钮／取消改绑的按键不能成为恢复后的攻击或技能输入。
            ResumeAfterFrame = Time.frameCount + 1;
        }
        Changed?.Invoke();
    }

    public void BeginRebind(InputBindingSettings.Entry entry, string group)
    {
        if (IsOpen && !QuitConfirmation) Bindings?.BeginRebind(entry, group);
    }

    public void RestoreDefaults() { if (IsOpen && !QuitConfirmation) Bindings?.RestoreDefaults(); }
    public void RequestQuit() { if (!IsOpen) return; QuitConfirmation = true; Changed?.Invoke(); }
    public void CancelQuit() { QuitConfirmation = false; Changed?.Invoke(); }
    public void ConfirmQuit() { if (IsOpen && QuitConfirmation) Exit.Quit(); }

    private void Update()
    {
        if (Menu.WasPressedThisFrame())
        {
            if (Bindings?.IsRebinding == true) Bindings.CancelRebind();
            else if (Bindings?.LastRebindEndFrame != Time.frameCount)
            {
                if (QuitConfirmation) CancelQuit();
                else SetOpen(!IsOpen);
            }
        }
        if (IsOpen)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void RestoreSession()
    {
        Time.timeScale = PreviousTimeScale;
        Cursor.lockState = PreviousCursorLock;
        Cursor.visible = PreviousCursorVisible;
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        if (IsOpen) RestoreSession();
        Menu?.Dispose();
        if (Bindings != null) { Bindings.Changed -= OnBindingsChanged; Bindings.Dispose(); }
        RuntimeInputs.Clear();
        Instance = null;
    }
}
