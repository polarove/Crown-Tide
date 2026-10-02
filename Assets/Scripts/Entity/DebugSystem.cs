using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>本地调试会话开关，独立于任何实体的控制权与死亡状态。</summary>
public sealed class DebugSystem : MonoBehaviour
{
    public static bool IsEnabled { get; private set; }
    private static DebugSystem? Instance;
    private InputAction ToggleAction = null!;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        Instance = null;
        IsEnabled = false;
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
    }

    private void Update()
    {
        if (ToggleAction != null && ToggleAction.WasPressedThisFrame())
        {
            SetEnabled(!IsEnabled);
        }
    }

    private void OnDestroy()
    {
        ToggleAction?.Dispose();
        if (Instance == this)
        {
            Instance = null;
            IsEnabled = false;
        }
    }
}
