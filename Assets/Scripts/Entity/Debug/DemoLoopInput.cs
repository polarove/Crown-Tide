using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>机制验证场景快捷键；与角色死亡／附身状态独立。</summary>
[RequireComponent(typeof(DemoLoopReset))]
public sealed class DemoLoopInput : MonoBehaviour
{
    private DemoLoopReset Reset = null!;
    private InputAction Restart = null!;
    private InputAction ToggleAttack = null!;

    private void Awake()
    {
        Reset = GetComponent<DemoLoopReset>();
        Restart = new InputAction("ResetDemo", InputActionType.Button);
        Restart.AddBinding("<Keyboard>/backspace");
        ToggleAttack = new InputAction("ToggleDemoEnemyAttack", InputActionType.Button);
        ToggleAttack.AddBinding("<Keyboard>/f8");
    }

    private void OnEnable()
    {
        Restart?.Enable();
        if (DebugSystem.IsEnabled) ToggleAttack?.Enable();
    }

    private void OnDisable()
    {
        Restart?.Disable();
        ToggleAttack?.Disable();
    }

    private void Update()
    {
        if (DebugSystem.IsEnabled && !ToggleAttack.enabled) ToggleAttack.Enable();
        else if (!DebugSystem.IsEnabled && ToggleAttack.enabled) ToggleAttack.Disable();
        if (Reset.IsResetting || GameSettingsController.IsGameplayInputBlocked) return;
        if (Restart.WasPressedThisFrame()) Reset.RequestReset();
        else if (DebugSystem.IsEnabled && ToggleAttack.WasPressedThisFrame()) Reset.ToggleEnemyAttack();
    }

    private void OnDestroy()
    {
        Restart?.Dispose();
        ToggleAttack?.Dispose();
    }
}
