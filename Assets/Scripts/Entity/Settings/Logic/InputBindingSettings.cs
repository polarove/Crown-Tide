using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>绑定请求、校验及运行实例同步；共享输入资产始终只读。</summary>
public sealed class InputBindingSettings : IDisposable
{
    public readonly struct Entry
    {
        public readonly Guid ActionId;
        public readonly int Index;
        public readonly string Label;
        public Entry(InputAction action, int index, string label)
        {
            ActionId = action.id;
            Index = index;
            Label = label;
        }
    }

    public InputActionAsset Actions { get; }
    public bool IsRebinding => Operation != null;
    public string Status { get; private set; } = "点击键位开始修改，修改后自动保存。";
    public int LastRebindEndFrame { get; private set; } = -1;
    public event Action? Changed;
    private readonly IBindingOverrideStore Store;
    private readonly HashSet<InputActionAsset> Instances = new();
    private InputActionRebindingExtensions.RebindingOperation? Operation;

    public InputBindingSettings(InputActionAsset template, IBindingOverrideStore store)
    {
        Store = store;
        Actions = UnityEngine.Object.Instantiate(template);
        Actions.Disable();
        Actions.bindingMask = null;
        Actions.devices = null;
        Actions.RemoveAllBindingOverrides();
        try
        {
            string json = Store.Load();
            if (!string.IsNullOrWhiteSpace(json)) Actions.LoadBindingOverridesFromJson(json);
        }
        catch (Exception)
        {
            Actions.RemoveAllBindingOverrides();
            Status = "保存的键位无法读取，当前使用默认键位；重新改绑可覆盖旧记录。";
        }
    }

    public List<Entry> GetEntries(string group)
    {
        var result = new List<Entry>();
        foreach (InputAction action in Actions)
        {
            if (action.actionMap.name != "Player" || action.name == "MouseLook") continue;
            for (int i = 0; i < action.bindings.Count; i++)
            {
                InputBinding binding = action.bindings[i];
                if (binding.isComposite || !InGroup(binding, group)) continue;
                string label = Label(action.name);
                if (binding.isPartOfComposite) label += " · " + Label(binding.name);
                if (action.name == "SwitchShoulder" && group == "Gamepad") label += " · " + (binding.path.EndsWith("left") ? "左" : "右");
                result.Add(new Entry(action, i, label));
            }
        }
        return result;
    }

    public string Display(Entry entry) => Actions.FindAction(entry.ActionId.ToString(), true).GetBindingDisplayString(entry.Index);

    public void Register(InputActionAsset instance)
    {
        // 不把当前玩法表的 GUID 覆盖误灌入 UI 表／其他测试或模块自己的输入表。
        foreach (InputAction action in Actions)
            if (instance.FindAction(action.id.ToString()) == null) return;
        if (instance == Actions || !Instances.Add(instance)) return;
        ApplyProfile(instance);
    }

    public void Unregister(InputActionAsset instance) => Instances.Remove(instance);

    public bool TryRebind(Entry entry, string path, string group)
    {
        InputAction action = Actions.FindAction(entry.ActionId.ToString(), true);
        InputBinding binding = action.bindings[entry.Index];
        if (!InGroup(binding, group) || binding.isComposite || action.name == "MouseLook")
            return Report(false, "这项绑定不能修改。");
        bool deviceMatches = group == "Gamepad" ? path.StartsWith("<Gamepad>/", StringComparison.OrdinalIgnoreCase)
            : path.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("<Mouse>/", StringComparison.OrdinalIgnoreCase);
        if (!deviceMatches) return Report(false, "请使用当前分类的设备。");
        string? layout = InputControlPath.TryGetControlLayout(path);
        string expected = !binding.isPartOfComposite && action.expectedControlType == "Vector2" ? "Vector2" : "Button";
        if (layout == null || !InputSystem.IsFirstLayoutBasedOnSecond(layout, expected))
            return Report(false, expected == "Vector2" ? "请使用二维摇杆。" : "请使用按键或扳机，不能绑定鼠标位移／摇杆。");
        if (IsReserved(path)) return Report(false, "该键用于设置、重开或调试，请选择其他键。");
        foreach (InputAction other in Actions)
        {
            for (int i = 0; i < other.bindings.Count; i++)
            {
                InputBinding candidate = other.bindings[i];
                if (other == action && i == entry.Index || candidate.isComposite || !InGroup(candidate, group)) continue;
                if (string.Equals(candidate.effectivePath, path, StringComparison.OrdinalIgnoreCase))
                    return Report(false, "键位冲突：已用于「" + Label(other.name) + "」。原键位保持不变。");
            }
        }
        string previous = Actions.SaveBindingOverridesAsJson();
        action.ApplyBindingOverride(entry.Index, path);
        return SaveAndSync(previous, "已保存「" + entry.Label + "」的新键位。");
    }

    public void BeginRebind(Entry entry, string group)
    {
        CancelRebind();
        InputAction action = Actions.FindAction(entry.ActionId.ToString(), true);
        bool vector = !action.bindings[entry.Index].isPartOfComposite && action.expectedControlType == "Vector2";
        Operation = action.PerformInteractiveRebinding(entry.Index)
            .WithExpectedControlType(vector ? "Vector2" : "Button")
            .WithCancelingThrough("<Keyboard>/escape")
            .WithControlsExcluding("<Mouse>/position")
            .WithControlsExcluding("<Mouse>/delta")
            .WithControlsExcluding("<Mouse>/scroll")
            .WithControlsExcluding("<Gamepad>/start")
            .WithControlsExcluding("<Gamepad>/select")
            .WithTimeout(10f)
            .OnMatchWaitForAnother(.1f)
            .OnApplyBinding((operation, path) => TryRebind(entry, path, group))
            .OnComplete(operation => FinishRebind(operation))
            .OnCancel(operation =>
            {
                Status = "已取消改键位，原绑定保持不变。";
                FinishRebind(operation);
            });
        if (group == "Gamepad") Operation.WithControlsHavingToMatchPath("<Gamepad>");
        else Operation.WithControlsHavingToMatchPath("<Keyboard>").WithControlsHavingToMatchPath("<Mouse>");
        Status = "「" + entry.Label + "」：请按新键" + (vector ? "／拨动摇杆" : "") + "；Esc / Menu 取消，10 秒后自动取消。";
        Operation.Start();
        Changed?.Invoke();
    }

    public void CancelRebind() => Operation?.Cancel();

    private void FinishRebind(InputActionRebindingExtensions.RebindingOperation operation)
    {
        Operation = null;
        operation.Dispose();
        LastRebindEndFrame = Time.frameCount;
        Changed?.Invoke();
    }

    public bool RestoreDefaults()
    {
        CancelRebind();
        string previous = Actions.SaveBindingOverridesAsJson();
        Actions.RemoveAllBindingOverrides();
        return SaveAndSync(previous, "已恢复键盘、鼠标与手柄的默认键位。");
    }

    private bool SaveAndSync(string previous, string success)
    {
        try { Store.Save(Actions.SaveBindingOverridesAsJson()); }
        catch (Exception)
        {
            Actions.LoadBindingOverridesFromJson(previous);
            return Report(false, "键位保存失败，已恢复原绑定。");
        }
        Instances.RemoveWhere(instance => instance == null);
        foreach (InputActionAsset instance in Instances) ApplyProfile(instance);
        return Report(true, success);
    }

    private void ApplyProfile(InputActionAsset instance)
    {
        // 仅暂时停用已启用动作，保持角色／相机各自的设备及启用范围。
        var enabled = new List<InputAction>();
        foreach (InputAction action in instance) if (action.enabled) enabled.Add(action);
        instance.Disable();
        instance.LoadBindingOverridesFromJson(Actions.SaveBindingOverridesAsJson());
        foreach (InputAction action in enabled) action.Enable();
    }

    private bool Report(bool result, string message)
    {
        Status = message;
        Changed?.Invoke();
        return result;
    }

    private static bool InGroup(InputBinding binding, string group) => Array.Exists((binding.groups ?? "").Split(';'), value => value == group);

    private static bool IsReserved(string path)
    {
        string value = path.ToLowerInvariant();
        if (value == "<keyboard>/escape" || value == "<keyboard>/backspace" || value == "<gamepad>/start" || value == "<gamepad>/select") return true;
        for (int i = 3; i <= 12; i++) if (value == "<keyboard>/f" + i) return true;
        return false;
    }

    private static string Label(string name) => name switch
    {
        "Possess" => "附身", "Move" => "移动", "Jump" => "跳跃", "Look" => "视角摇杆",
        "SwitchShoulder" => "切换肩侧", "ToggleView" => "切换视角", "Sprint" => "加速",
        "Attack" => "攻击", "Aim" => "瞄准", "CrownSkill" => "冠冕技能", "TideSkill" => "潮汐技能",
        "up" => "前", "down" => "后", "left" => "左", "right" => "右", _ => name
    };

    public void Dispose()
    {
        CancelRebind();
        Instances.Clear();
        UnityEngine.Object.Destroy(Actions);
    }
}
