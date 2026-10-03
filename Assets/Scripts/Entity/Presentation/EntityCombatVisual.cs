using Assets.Scripts.Entity.Data.Skill;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>演示战斗反馈：只读资源、动作与实际生命变更；自持UI、范围线和外观，不写玩法数据。</summary>
[RequireComponent(typeof(Entity))]
[DefaultExecutionOrder(110)]
public sealed class EntityCombatVisual : MonoBehaviour
{
    private Entity Host = null!;
    private CharacterController Controller = null!;
    private Canvas? Overlay;
    private RectTransform Panel = null!;
    private RectTransform HeadLink = null!;
    private Text Title = null!;
    private Text Health = null!;
    private Text Faith = null!;
    private Text Skills = null!;
    private RectTransform HealthFill = null!;
    private RectTransform FaithFill = null!;
    private Font? HudFont;
    private LineRenderer Range = null!;
    private Material? RangeMaterial;
    private MeshRenderer[] Body = System.Array.Empty<MeshRenderer>();
    private MaterialPropertyBlock[] OriginalBlocks = System.Array.Empty<MaterialPropertyBlock>();
    private MaterialPropertyBlock TintBlock = null!;
    private readonly List<Popup> Popups = new();
    private float PreviousHp;
    private float FlashUntil;
    private Color FlashColor;
    private static readonly List<Rect> PlacedPanels = new();
    private static int LayoutFrame = -1;
    private static readonly Color PlayerColor = new(0.2f, 0.85f, 1f);
    private static readonly Color EnemyColor = new(1f, 0.55f, 0.25f);
    private static readonly Color HealColor = new(0.25f, 1f, 0.4f);
    private static readonly Color DamageColor = new(1f, 0.25f, 0.25f);
    private static readonly Color TideColor = new(0.7f, 0.4f, 1f);
    private static readonly Color CrownColor = new(1f, 0.85f, 0.25f);

    private sealed class Popup
    {
        public Text Label = null!;
        public float Started;
        public float Delta;
    }

    public bool IsStatusVisible => Overlay != null && Overlay.enabled && Panel.gameObject.activeSelf;
    public bool IsRangeVisible => Range != null && Range.enabled;
    public int ActivePopupCount => Popups.Count;
    public float LastHpDelta { get; private set; }
    public Rect StatusScreenRect { get; private set; }

    private void Awake()
    {
        TintBlock = new MaterialPropertyBlock();
        Host = GetComponent<Entity>();
        Controller = GetComponent<CharacterController>();
        Body = GetComponentsInChildren<MeshRenderer>();
        OriginalBlocks = new MaterialPropertyBlock[Body.Length];
        for (int i = 0; i < Body.Length; i++)
        {
            OriginalBlocks[i] = new MaterialPropertyBlock();
            Body[i].GetPropertyBlock(OriginalBlocks[i]);
        }
    }

    private void OnEnable()
    {
        PreviousHp = Host.Vitals.CurrentHp;
        Host.Vitals.HpChanged += OnHpChanged;
    }

    private void OnHpChanged(CharacterVitals vitals)
    {
        float delta = vitals.CurrentHp - PreviousHp;
        PreviousHp = vitals.CurrentHp;
        if (Mathf.Approximately(delta, 0f)) return; // 满血治疗不展示虚假的回血量。
        LastHpDelta = delta;
        FlashColor = delta > 0f ? HealColor : DamageColor;
        FlashUntil = Time.time + .22f;
        EnsureVisuals();
        if (Popups.Count >= 8)
        {
            Destroy(Popups[0].Label.gameObject);
            Popups.RemoveAt(0);
        }
        Text label = MakeText("HP Change", Overlay!.transform, 24);
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.text = delta > 0f ? $"+{delta:0.#}" : $"{delta:0.#}";
        label.color = FlashColor;
        var shadow = label.gameObject.AddComponent<Shadow>();
        shadow.effectColor = Color.black;
        shadow.effectDistance = new Vector2(1.5f, -1.5f);
        Popups.Add(new Popup { Label = label, Started = Time.time, Delta = delta });
    }

    private void EnsureVisuals()
    {
        if (Overlay != null) return;
        HudFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 16);
        var go = new GameObject($"{name} Combat HUD", typeof(RectTransform), typeof(Canvas));
        Overlay = go.GetComponent<Canvas>();
        Overlay.renderMode = RenderMode.ScreenSpaceOverlay;
        Overlay.sortingOrder = 10;
        HeadLink = MakeImage("Head Link", go.transform, new Color(.5f, .8f, .9f, .65f)).rectTransform;
        HeadLink.pivot = new Vector2(0f, .5f);
        Panel = MakeImage("Status", go.transform, new Color(.03f, .04f, .07f, .9f)).rectTransform;
        Title = MakeText("Title", Panel, 16);
        Health = MakeText("HP", Panel, 14);
        Faith = MakeText("Faith", Panel, 14);
        Skills = MakeText("Skills", Panel, 14);
        Skills.alignment = TextAnchor.UpperLeft;
        RectTransform hpBack = MakeImage("HP Background", Panel, new Color(.18f, .22f, .27f)).rectTransform;
        HealthFill = MakeImage("HP Fill", hpBack, new Color(.1f, .55f, .38f)).rectTransform;
        RectTransform faithBack = MakeImage("Faith Background", Panel, new Color(.18f, .22f, .27f)).rectTransform;
        FaithFill = MakeImage("Faith Fill", faithBack, CrownColor).rectTransform;
        Place(hpBack, 10f, 31f, 220f, 19f);
        Place(faithBack, 10f, 54f, 220f, 19f);
        Place(Title.rectTransform, 10f, 5f, 220f, 22f);
        Place(Health.rectTransform, 10f, 31f, 220f, 19f);
        Place(Faith.rectTransform, 10f, 54f, 220f, 19f);
        Place(Skills.rectTransform, 10f, 78f, 220f, 58f);
        // 标签最后渲染，避免被条形遮住；信心零点及双方技能阈值均可见。
        Title.transform.SetAsLastSibling();
        Health.transform.SetAsLastSibling();
        Faith.transform.SetAsLastSibling();
        RectTransform zero = MakeImage("Faith Zero", faithBack, Color.white).rectTransform;
        Place(zero, 109f, 0f, 2f, 19f);
        AddThreshold(faithBack, Host.Slots.Skills.Crown, 1f);
        AddThreshold(faithBack, Host.Slots.Skills.Tide, -1f);
        var rangeObject = new GameObject("Attack Range");
        rangeObject.transform.SetParent(transform, false);
        Range = rangeObject.AddComponent<LineRenderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        RangeMaterial = new Material(shader);
        Range.sharedMaterial = RangeMaterial;
        Range.useWorldSpace = true;
        Range.widthMultiplier = .045f;
        Range.shadowCastingMode = ShadowCastingMode.Off;
        Range.receiveShadows = false;
        Range.positionCount = 43;
        Range.enabled = false;
    }

    private void AddThreshold(RectTransform parent, SkillSO? skill, float sign)
    {
        if (skill == null || Host.Vitals.FaithCapacity <= 0) return;
        float fraction = Mathf.Clamp01((float)skill.FaithThreshold / Host.Vitals.FaithCapacity);
        RectTransform tick = MakeImage("Skill Threshold", parent, new Color(1f, 1f, 1f, .7f)).rectTransform;
        Place(tick, 110f + sign * fraction * 110f - 1f, 0f, 2f, 19f);
    }

    private Text MakeText(string label, Transform parent, int size)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Text text = go.GetComponent<Text>();
        text.font = HudFont!;
        text.fontSize = size;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static Image MakeImage(string label, Transform parent, Color color)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private void LateUpdate()
    {
        EnsureVisuals();
        bool controlled = Host.HasPlayerView;
        Color bodyColor = controlled ? PlayerColor : Host.HasSoulOut ? new Color(.35f, .6f, .8f) : EnemyColor;
        for (int i = 0; i < Body.Length; i++)
        {
            if (Body[i] == null) continue;
            Body[i].GetPropertyBlock(TintBlock);
            Color tint = Time.time < FlashUntil ? FlashColor : bodyColor;
            TintBlock.SetColor("_BaseColor", tint);
            TintBlock.SetColor("_Color", tint);
            Body[i].SetPropertyBlock(TintBlock);
        }
        UpdateRange(controlled);
        RefreshStatus(controlled);
        Camera? view = null;
        foreach (Camera camera in Camera.allCameras)
            if (camera.isActiveAndEnabled && camera.cameraType == CameraType.Game
                && camera.WorldToViewportPoint(Controller.bounds.center).z > camera.nearClipPlane)
            { view = camera; break; }
        Overlay!.enabled = view != null;
        Overlay.scaleFactor = Mathf.Clamp(Screen.height / 720f, .75f, 2f);
        if (view != null) PositionStatus(view, controlled);
        for (int i = Popups.Count - 1; i >= 0; i--)
        {
            Popup popup = Popups[i];
            float age = Time.time - popup.Started;
            if (age > 1.15f)
            {
                Destroy(popup.Label.gameObject);
                Popups.RemoveAt(i);
                continue;
            }
            if (view == null) continue;
            Vector3 point = view.WorldToScreenPoint(Controller.bounds.center + Vector3.up * (.6f + age * .8f));
            Color color = popup.Delta > 0f ? HealColor : DamageColor;
            color.a = Mathf.Clamp01((1.15f - age) * 3f);
            popup.Label.color = color;
            float offset = controlled ? popup.Delta > 0f ? -130f : -35f : 45f;
            Place(popup.Label.rectTransform, point.x / Overlay.scaleFactor + offset,
                (Screen.height - point.y) / Overlay.scaleFactor - i * 8f, 90f, 32f);
        }
    }

    private void RefreshStatus(bool controlled)
    {
        Title.text = Host.IsDead ? "已倒下 · Backspace 重来" : controlled ? "你正在控制" : Host.HasSoulOut ? "原角色 · 附身中" : name == "Enemy" ? "敌人" : name;
        Title.color = controlled ? PlayerColor : EnemyColor;
        Health.text = $"生命 {Host.Vitals.CurrentHp:0.#} / {Host.Vitals.MaxHp:0.#}";
        Faith.text = $"信心 {Host.Vitals.Faith?.Current ?? 0:+0;-0;0}";
        Place(HealthFill, 0f, 0f, 220f * Mathf.Clamp01(Host.Vitals.CurrentHp / Host.Vitals.MaxHp), 19f);
        float fraction = Mathf.Clamp((float)(Host.Vitals.Faith?.Current ?? 0) / Mathf.Max(1, Host.Vitals.FaithCapacity), -1f, 1f);
        Place(FaithFill, 110f + Mathf.Min(0f, fraction) * 110f, 0f, Mathf.Abs(fraction) * 110f, 19f);
        FaithFill.GetComponent<Image>().color = fraction < 0f ? TideColor : CrownColor;
        if (controlled)
        {
            Skills.text = SkillStatus(EnumSkillType.Crown, "Q / RB 冠冕") + "\n" + SkillStatus(EnumSkillType.Tide, "E / Y 潮汐");
            string special = "V / LB 附身 · Backspace 重来";
            if (Host.Brain.Modifiers.GetCrownLifeStealRatio() > 0f)
            {
                float remaining = 0f;
                bool permanent = false;
                SkillSO? tide = Host.Slots.Skills.Tide;
                if (tide != null && tide.AfterTideEffects != null)
                    foreach (ModifierEffect effect in tide.AfterTideEffects)
                    {
                        if (effect == null || effect.CrownLifeStealRatio <= 0f) continue;
                        float time = Host.Brain.Modifiers.GetRemaining(effect);
                        if (time > 0f) remaining = Mathf.Max(remaining, time);
                        else if (Host.Brain.Modifiers.IsHolding(effect)) permanent = true;
                    }
                special = remaining > 0f && !permanent ? $"冠冕吸血 {remaining:0.0}s · 命中回血" : "冠冕吸血中 · 命中才回血";
            }
            if (Host.IsPossessing)
            {
                special = $"附身剩余 {Host.PossessionRemaining:0.0}s";
            }
            Skills.text += "\n" + special;
        }
        else Skills.text = Host.Brain.StateMachine.GetActive(EnumStateLayer.Action) is EntityAttackState attack
            ? attack.PhaseIndex == 0 ? "准备攻击" : attack.PhaseIndex == 1 ? "挥击" : "收招" : "";
    }

    private string SkillStatus(EnumSkillType kind, string label)
    {
        if (!Host.Slots.Skills.TryGet(kind, out SkillSO? skill, out float cooldown) || skill == null) return label + "：未装备";
        if (cooldown > 0f) return $"{label}：冷却 {cooldown:0.0}s";
        if (Host.Brain.Capability.CanCastSkill(kind))
            return label + (Host.Slots.Skills.IsBurstReady(kind, Host.Vitals.Faith) ? "：强化可用" : "：可释放");
        if ((long)(int)kind * (Host.Vitals.Faith?.Current ?? 0) < skill.FaithThreshold)
            return $"{label}：需{(int)kind * skill.FaithThreshold:+0;-0;0}";
        return label + "：当前状态不可用";
    }

    private void PositionStatus(Camera view, bool controlled)
    {
        Vector3 head = view.WorldToScreenPoint(new Vector3(Controller.bounds.center.x, Controller.bounds.max.y + .25f, Controller.bounds.center.z));
        Vector3 viewport = view.WorldToViewportPoint(Controller.bounds.center);
        bool visible = viewport.z > view.nearClipPlane && viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f;
        // F12打开详细面板时不叠放两套状态，实际命中反馈与范围照常保留。
        Panel.gameObject.SetActive(visible && !DebugSystem.IsEnabled);
        HeadLink.gameObject.SetActive(visible && !DebugSystem.IsEnabled);
        if (!visible) return;
        float scale = Overlay!.scaleFactor;
        float height = (controlled ? 140f : 102f) * scale;
        float width = 240f * scale;
        Rect bounds = view.pixelRect;
        Rect rect = new(Mathf.Clamp(controlled ? head.x - width - 18f * scale : head.x + 18f * scale,
                bounds.xMin, Mathf.Max(bounds.xMin, bounds.xMax - width)),
            Mathf.Clamp(Screen.height - head.y - height, Screen.height - bounds.yMax, Screen.height - bounds.yMin - height), width, height);
        if (LayoutFrame != Time.frameCount) { LayoutFrame = Time.frameCount; PlacedPanels.Clear(); }
        foreach (Rect other in PlacedPanels)
        {
            if (!rect.Overlaps(other)) continue;
            if (other.xMax + 8f + rect.width <= bounds.xMax) rect.x = other.xMax + 8f;
            else if (other.xMin - 8f - rect.width >= bounds.xMin) rect.x = other.xMin - 8f - rect.width;
            else rect.y = Mathf.Max(Screen.height - bounds.yMax, other.y - rect.height - 8f);
        }
        PlacedPanels.Add(rect);
        StatusScreenRect = rect;
        Place(Panel, rect.x / scale, rect.y / scale, rect.width / scale, rect.height / scale);
        Vector2 start = new(head.x / scale, head.y / scale);
        Vector2 end = new((controlled ? rect.xMax : rect.xMin) / scale, (Screen.height - rect.yMax) / scale);
        Vector2 delta = end - start;
        HeadLink.anchorMin = HeadLink.anchorMax = Vector2.zero;
        HeadLink.anchoredPosition = start;
        HeadLink.sizeDelta = new Vector2(delta.magnitude, 1.5f);
        HeadLink.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
    }

    private void UpdateRange(bool controlled)
    {
        EntityAttackState? attack = Host.Brain.StateMachine.GetActive(EnumStateLayer.Action) as EntityAttackState;
        WeaponComboGraph? graph = Host.Slots.CurrentComboGraph;
        ComboEntry entry = attack != null ? attack.CurrentEntry : graph != null && graph.ComboEntries != null && graph.ComboEntries.Length > 0 ? graph.ComboEntries[0] : default;
        Range.enabled = !Host.IsDead && (controlled || attack != null) && entry.IsHitVolumeValid() && entry.HitShape == EnumMeleeHitShape.Sector;
        if (!Range.enabled) return;
        Vector3 forward = attack != null ? transform.forward : Host.Commands.LookDirection;
        forward.y = 0f;
        if (forward.sqrMagnitude < .000001f) forward = transform.forward;
        Quaternion rotation = Quaternion.LookRotation(forward);
        Vector3 center = transform.position + rotation * entry.HitOffset;
        center.y = Controller.bounds.min.y + .045f;
        Color color = controlled ? PlayerColor : EnemyColor;
        if (attack != null) color = attack.PhaseIndex == 0 ? CrownColor : attack.PhaseIndex == 1 ? Color.white : color * .6f;
        Range.startColor = Range.endColor = color;
        RangeMaterial!.SetColor("_BaseColor", color);
        RangeMaterial.SetColor("_Color", color);
        Range.SetPosition(0, center);
        for (int i = 0; i <= 40; i++)
            Range.SetPosition(i + 1, center + rotation * Quaternion.Euler(0f, -entry.HitAngle / 2f + entry.HitAngle * i / 40f, 0f) * Vector3.forward * entry.HitRadius);
        Range.SetPosition(42, center);
    }

    private void OnDisable()
    {
        Host.Vitals.HpChanged -= OnHpChanged;
        if (Overlay != null) Overlay.enabled = false;
        if (Range != null) Range.enabled = false;
        foreach (Popup popup in Popups) if (popup.Label != null) Destroy(popup.Label.gameObject);
        Popups.Clear();
        FlashUntil = 0f;
        for (int i = 0; i < Body.Length; i++) if (Body[i] != null) Body[i].SetPropertyBlock(OriginalBlocks[i]);
    }

    private void OnDestroy()
    {
        if (Overlay != null) Destroy(Overlay.gameObject);
        if (Range != null) Destroy(Range.gameObject);
        if (RangeMaterial != null) Destroy(RangeMaterial);
        if (HudFont != null) Destroy(HudFont);
    }
}
