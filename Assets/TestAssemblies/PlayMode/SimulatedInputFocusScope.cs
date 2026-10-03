using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>模拟设备测试不依赖编辑器窗口焦点；退出时恢复原配置，不修改资产。</summary>
internal sealed class SimulatedInputFocusScope : IDisposable
{
    private readonly InputSettings Original = InputSystem.settings;
    private readonly HideFlags OriginalFlags;
    private readonly InputSettings TestSettings;

    public SimulatedInputFocusScope()
    {
        OriginalFlags = Original.hideFlags;
        TestSettings = UnityEngine.Object.Instantiate(Original);
        TestSettings.hideFlags = HideFlags.DontSave;
        TestSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
        TestSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
        // InputManager 会在替换时销毁 HideAndDontSave 的默认临时配置；先保留原对象。
        if (OriginalFlags == HideFlags.HideAndDontSave) Original.hideFlags = HideFlags.DontSave;
        InputSystem.settings = TestSettings;
    }

    public void Dispose()
    {
        InputSystem.settings = Original;
        Original.hideFlags = OriginalFlags;
        UnityEngine.Object.Destroy(TestSettings);
    }
}
