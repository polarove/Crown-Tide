using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

/// <summary>本地调试会话开关，独立于任何实体的控制权与死亡状态。</summary>
[DefaultExecutionOrder(-100)]
public sealed class DebugSystem : MonoBehaviour
{
    public static bool IsEnabled { get; private set; }
    private static DebugSystem? Instance;
    private InputAction ToggleAction = null!;
    private static readonly Dictionary<EntityBrain, EntityDebugCommands> Modules = new();
    private static readonly List<EntityBrain> RemovedBrains = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        Instance = null;
        SetEnabled(false);
        RemovedBrains.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance == null)
        {
            new GameObject("Debug System").AddComponent<DebugSystem>();
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        ToggleAction = new InputAction("ToggleDebug", InputActionType.Button);
        ToggleAction.AddBinding("<Keyboard>/f12");
        ToggleAction.AddBinding("<Gamepad>/select");
        ToggleAction.Enable();
    }

    public static void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
        if (enabled)
        {
            RefreshModules();
            return;
        }
        foreach (var pair in Modules)
        {
            if (pair.Key != null) pair.Key.DetachCommandModule(pair.Value);
        }
        Modules.Clear();
    }

    private static void RefreshModules()
    {
        RemovedBrains.Clear();
        foreach (var pair in Modules)
        {
            if (pair.Key == null || !pair.Key.isActiveAndEnabled)
            {
                if (pair.Key != null) pair.Key.DetachCommandModule(pair.Value);
                // Unity 的已销毁对象比较为 null，但字典键仍是可移除的托管引用。
                RemovedBrains.Add(pair.Key!);
            }
        }
        foreach (EntityBrain brain in RemovedBrains) Modules.Remove(brain);
        // 扫描仅发生在 Debug 开启期间，覆盖场景加载、动态生成与重新激活的实体。
        foreach (EntityBrain brain in FindObjectsByType<EntityBrain>())
        {
            if (!brain.isActiveAndEnabled || Modules.ContainsKey(brain)) continue;
            var module = new EntityDebugCommands();
            if (brain.AttachCommandModule(module)) Modules.Add(brain, module);
        }
    }

    private void Update()
    {
        if (!GameSettingsController.IsGameplayInputBlocked && ToggleAction != null && ToggleAction.WasPressedThisFrame())
        {
            SetEnabled(!IsEnabled);
        }
        if (IsEnabled) RefreshModules();
    }

    private void OnDestroy()
    {
        ToggleAction?.Dispose();
        if (Instance == this)
        {
            Instance = null;
            SetEnabled(false);
        }
    }
}
