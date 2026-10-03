using UnityEngine;

/// <summary>仅保存绑定覆盖数据；不持有角色或输入组件。</summary>
public interface IBindingOverrideStore
{
    string Load();
    void Save(string json);
}

public sealed class PlayerPrefsBindingOverrideStore : IBindingOverrideStore
{
    private const string Key = "CrownTide.InputOverrides.v1";
    public string Load() => PlayerPrefs.GetString(Key, "");
    public void Save(string json)
    {
        PlayerPrefs.SetString(Key, json);
        PlayerPrefs.Save();
    }
}
