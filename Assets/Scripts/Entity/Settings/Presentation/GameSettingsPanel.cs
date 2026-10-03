using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>仅创建与刷新设置 UI；绑定、暂停、退出都提交给逻辑入口。</summary>
public sealed class GameSettingsPanel : MonoBehaviour
{
    private GameSettingsController Controller = null!;
    private GameObject Modal = null!;
    private GameObject Confirm = null!;
    private RectTransform Content = null!;
    private CanvasGroup Interaction = null!;
    private Text Status = null!;
    private Font UiFont = null!;
    private InputBindingSettings? DisplayedBindings;
    private string Group = "Keyboard&Mouse";
    private readonly List<(InputBindingSettings.Entry Entry, Text Text)> Rows = new();
    private GameObject? OwnedEventSystem;
    private Button Resume = null!;
    private Button ConfirmExit = null!;

    private void Awake()
    {
        Controller = GetComponent<GameSettingsController>();
        UiFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 20);
        Build();
        Controller.Changed += Refresh;
        Refresh();
    }

    private void Build()
    {
        RectTransform canvasRoot = Rect("Settings Canvas", transform);
        Canvas canvas = canvasRoot.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;
        CanvasScaler scaler = canvasRoot.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900);
        scaler.matchWidthOrHeight = .5f;
        canvasRoot.gameObject.AddComponent<GraphicRaycaster>();

        // 常驻入口也向逻辑提交请求；锁定鼠标时使用快捷键。
        Button launcher = Button("SettingsEntry", canvasRoot, "设置  ·  Esc / Menu", () => Controller.SetOpen(true));
        Place(launcher.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-280, -34), new Vector2(260, 44));
        RectTransform shade = Rect("Settings Modal", canvasRoot);
        Stretch(shade);
        shade.gameObject.AddComponent<Image>().color = new Color(.015f, .025f, .045f, .86f);
        Modal = shade.gameObject;
        RectTransform panel = Rect("Panel", shade);
        Place(panel, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(900, 760));
        panel.pivot = new Vector2(.5f, .5f);
        panel.gameObject.AddComponent<Image>().color = new Color(.06f, .09f, .14f, 1f);
        Interaction = panel.gameObject.AddComponent<CanvasGroup>();
        Text title = Text("Title", panel, "设置", 30);
        Place(title.rectTransform, Vector2.up, Vector2.up, new Vector2(40, -28), new Vector2(600, 46));
        Text intro = Text("Intro", panel, "游戏已暂停  ·  键位自动保存  ·  滚轮 / 方向键浏览  ·  Esc / Menu 返回", 18);
        Place(intro.rectTransform, Vector2.up, Vector2.up, new Vector2(40, -80), new Vector2(820, 30));
        Button keyboard = Button("KeyboardTab", panel, "键盘 / 鼠标", () => SelectGroup("Keyboard&Mouse"));
        Place(keyboard.GetComponent<RectTransform>(), Vector2.up, Vector2.up, new Vector2(40, -124), new Vector2(390, 44));
        Button gamepad = Button("GamepadTab", panel, "手柄", () => SelectGroup("Gamepad"));
        Place(gamepad.GetComponent<RectTransform>(), Vector2.up, Vector2.up, new Vector2(470, -124), new Vector2(390, 44));

        RectTransform scrollRoot = Rect("Binding Scroll", panel);
        Place(scrollRoot, Vector2.up, Vector2.up, new Vector2(40, -184), new Vector2(820, 410));
        scrollRoot.gameObject.AddComponent<Image>().color = new Color(.025f, .045f, .075f);
        ScrollRect scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        RectTransform viewport = Rect("Viewport", scrollRoot);
        Stretch(viewport);
        viewport.offsetMax = new Vector2(-20, 0);
        viewport.gameObject.AddComponent<RectMask2D>();
        Content = Rect("Bindings", viewport);
        Content.anchorMin = new Vector2(0, 1);
        Content.anchorMax = Vector2.one;
        Content.pivot = new Vector2(.5f, 1);
        Content.anchoredPosition = Vector2.zero;
        scroll.viewport = viewport;
        scroll.content = Content;
        RectTransform barRect = Rect("Binding Scrollbar", scrollRoot);
        barRect.anchorMin = new Vector2(1, 0);
        barRect.anchorMax = Vector2.one;
        barRect.pivot = Vector2.one;
        barRect.anchoredPosition = new Vector2(-4, -6);
        barRect.sizeDelta = new Vector2(12, -12);
        barRect.gameObject.AddComponent<Image>().color = new Color(.08f, .14f, .2f);
        Scrollbar bar = barRect.gameObject.AddComponent<Scrollbar>();
        RectTransform handle = Rect("Handle", barRect);
        Stretch(handle);
        Image handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = new Color(.3f, .55f, .7f);
        bar.handleRect = handle;
        bar.targetGraphic = handleImage;
        bar.direction = Scrollbar.Direction.BottomToTop;
        bar.navigation = new Navigation { mode = Navigation.Mode.None };
        scroll.verticalScrollbar = bar;
        Status = Text("Binding Status", panel, "", 18);
        Place(Status.rectTransform, Vector2.up, Vector2.up, new Vector2(40, -610), new Vector2(820, 68));
        Status.horizontalOverflow = HorizontalWrapMode.Wrap;
        Button defaults = Button("RestoreDefaults", panel, "恢复默认键位", Controller.RestoreDefaults);
        Place(defaults.GetComponent<RectTransform>(), Vector2.up, Vector2.up, new Vector2(40, -692), new Vector2(250, 44));
        Resume = Button("ResumeGame", panel, "继续游戏", () => Controller.SetOpen(false));
        Place(Resume.GetComponent<RectTransform>(), Vector2.up, Vector2.up, new Vector2(325, -692), new Vector2(250, 44));
        Button quit = Button("RequestQuit", panel, "退出游戏", Controller.RequestQuit);
        Place(quit.GetComponent<RectTransform>(), Vector2.up, Vector2.up, new Vector2(610, -692), new Vector2(250, 44));

        RectTransform confirmation = Rect("Quit Confirmation", shade);
        Stretch(confirmation);
        confirmation.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .9f);
        Confirm = confirmation.gameObject;
        Text question = Text("Quit Question", confirmation, "确定退出游戏？", 30);
        Place(question.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-220, 60), new Vector2(440, 60));
        question.alignment = TextAnchor.MiddleCenter;
        Button cancel = Button("CancelQuit", confirmation, "返回设置", Controller.CancelQuit);
        Place(cancel.GetComponent<RectTransform>(), new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-220, -30), new Vector2(200, 50));
        ConfirmExit = Button("ConfirmQuit", confirmation, "确认退出", Controller.ConfirmQuit);
        Place(ConfirmExit.GetComponent<RectTransform>(), new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(20, -30), new Vector2(200, 50));
    }

    private void SelectGroup(string group)
    {
        Group = group;
        DisplayedBindings = null;
        Refresh();
    }

    private void Refresh()
    {
        bool wasOpen = Modal.activeSelf;
        Modal.SetActive(Controller.IsOpen);
        Confirm.SetActive(Controller.QuitConfirmation);
        if (Controller.IsOpen) EnsureEventSystem();
        Interaction.interactable = CanInteract();
        if (Controller.Bindings?.IsRebinding == true && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null!);
        Status.text = Controller.Bindings?.Status ?? "没有配置输入动作资源，暂不能修改键位。";
        if (Controller.Bindings != DisplayedBindings)
        {
            DisplayedBindings = Controller.Bindings;
            RebuildRows();
        }
        if (DisplayedBindings != null)
            foreach (var row in Rows) row.Text.text = DisplayedBindings.Display(row.Entry);
        if (Controller.IsOpen && !wasOpen && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(Resume.gameObject);
        if (Controller.QuitConfirmation && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(ConfirmExit.gameObject);
        if (!Controller.IsOpen && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null!);
    }

    private void RebuildRows()
    {
        foreach (Transform child in Content) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        Rows.Clear();
        if (DisplayedBindings == null) return;
        List<InputBindingSettings.Entry> entries = DisplayedBindings.GetEntries(Group);
        Content.sizeDelta = new Vector2(0, entries.Count * 50 + 12);
        Content.anchoredPosition = Vector2.zero;
        for (int i = 0; i < entries.Count; i++)
        {
            InputBindingSettings.Entry entry = entries[i];
            Text label = Text("Action " + entry.Label, Content, entry.Label, 20);
            Place(label.rectTransform, Vector2.up, Vector2.up, new Vector2(18, -i * 50 - 6), new Vector2(390, 42));
            Button binding = Button("Binding " + entry.Label, Content, "", () => Controller.BeginRebind(entry, Group));
            Place(binding.GetComponent<RectTransform>(), Vector2.up, Vector2.up, new Vector2(430, -i * 50 - 6), new Vector2(360, 42));
            Rows.Add((entry, binding.GetComponentInChildren<Text>()));
        }
    }

    private void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        if (OwnedEventSystem != null) return;
        OwnedEventSystem = new GameObject("Settings EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        DontDestroyOnLoad(OwnedEventSystem);
        OwnedEventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        return (RectTransform)obj.transform;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static void Place(RectTransform rect, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private Text Text(string name, Transform parent, string value, int size)
    {
        RectTransform rect = Rect(name, parent);
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = UiFont;
        text.text = value;
        text.fontSize = size;
        text.color = new Color(.9f, .95f, 1f);
        text.alignment = TextAnchor.MiddleLeft;
        text.raycastTarget = false;
        return text;
    }

    private Button Button(string name, Transform parent, string label, UnityEngine.Events.UnityAction clicked)
    {
        RectTransform rect = Rect(name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(.12f, .23f, .34f);
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(clicked);
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(.45f, .8f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;
        Text text = Text("Label", rect, label, 20);
        Stretch(text.rectTransform);
        text.alignment = TextAnchor.MiddleCenter;
        return button;
    }

    private void OnDestroy()
    {
        if (Controller != null) Controller.Changed -= Refresh;
        if (OwnedEventSystem != null) Destroy(OwnedEventSystem);
        if (UiFont != null) Destroy(UiFont);
    }

    private void LateUpdate()
    {
        Interaction.interactable = CanInteract();
        if (Controller.IsOpen && Interaction.interactable && EventSystem.current != null
            && (EventSystem.current.currentSelectedGameObject == null || !EventSystem.current.currentSelectedGameObject.activeInHierarchy))
            EventSystem.current.SetSelectedGameObject(Resume.gameObject);
        // 手柄导航到滚动区外的按键时，让选中行保持可见。
        if (!Controller.IsOpen || EventSystem.current == null) return;
        GameObject? selected = EventSystem.current.currentSelectedGameObject;
        if (selected == null || selected.transform.parent != Content) return;
        RectTransform row = (RectTransform)selected.transform;
        RectTransform viewport = (RectTransform)Content.parent;
        float top = -row.anchoredPosition.y;
        float offset = Content.anchoredPosition.y;
        if (top < offset) offset = top;
        else if (top + row.rect.height > offset + viewport.rect.height) offset = top + row.rect.height - viewport.rect.height;
        Content.anchoredPosition = new Vector2(0, Mathf.Clamp(offset, 0, Mathf.Max(0, Content.rect.height - viewport.rect.height)));
    }

    private bool CanInteract() => !Controller.QuitConfirmation && Controller.Bindings?.IsRebinding != true
        && Time.frameCount > (Controller.Bindings?.LastRebindEndFrame ?? -2) + 1;
}
