using System;
using UnityEngine;
using UnityEngine.UIElements;

public class Readme : ScriptableObject
{
    // 全部由 Readme.asset 序列化提供，构造函数阶段为空 → null! 断言（不是赋 null）
    public StyleSheet commonStyle = null!;
    public StyleSheet darkStyle = null!;
    public StyleSheet lightStyle = null!;
    public Texture2D icon = null!;
    public string title = null!;
    public Section[] sections = null!;
    public bool loadedLayout;

    [Serializable]
    public class Section
    {
        // 四个字段值由 Readme.asset 序列化提供，构造函数阶段天然为空 → 用 null! 断言（不是赋 null）
        public string heading = null!, text = null!, linkText = null!, url = null!;
    }
}
