using Assets.Scripts.Entity.Data.Armor;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 实体表现（Presentation 层，占位）：只读 Data/Logic/Physics 的状态做呈现——
/// 本轮交付调试 HUD（旧 PlayerController.OnGUI 面板迁移；含护甲部位/件数/套装档位）；动画/特效/外观后置
/// （接入点：状态 Enter/Exit + locomotionStyle 选型键 + HpChanged/Died/EquipmentChanged 事件）。
/// 只读纪律：本组件不改任何数据——唯一例外是"表现层自持对象"（将来的 Renderer 显隐/粒子）。
/// 挂在实体物体上；与 Entity 同生共死（RequireComponent 直连，省一次 null 判断）。
/// </summary>
[RequireComponent(typeof(Entity))]
[DefaultExecutionOrder(100)]
public sealed class EntityVisual : MonoBehaviour
{
    [Header("头顶调试面板")]
    public float DebugPanelWidth = 380f;
    public float DebugHeadOffset = 0.25f;

    private Entity Entity = null!;   // Awake 注入（RequireComponent 保证存在）
    private CharacterController Controller = null!;
    private Canvas? PanelCanvas;
    private RectTransform PanelRect = null!;
    private Text PanelText = null!;
    private Font? PanelFont;
    private RectTransform LinkRect = null!;
    private static readonly List<Rect> PlacedPanels = new();
    private static int LayoutFrame = -1;

    public Rect DebugPanelScreenRect { get; private set; }

    public bool IsDebugPanelVisible => DebugSystem.IsEnabled && isActiveAndEnabled;

    public Vector3 DebugAnchor => Controller != null
        ? new Vector3(Controller.bounds.center.x, Controller.bounds.max.y + DebugHeadOffset, Controller.bounds.center.z)
        : transform.position + Vector3.up * (1f + DebugHeadOffset);

    private void Awake()
    {
        Entity = GetComponent<Entity>();
        Controller = GetComponent<CharacterController>();
    }

    // Canvas 只读渲染：角色头顶投影到屏幕，屏幕尺寸变化与附身换相机后每帧刷新。
    private void LateUpdate()
    {
        if (!IsDebugPanelVisible || Entity == null)
        {
            if (PanelCanvas != null) PanelCanvas.enabled = false;
            return;
        }
        EnsurePanel();
        PanelCanvas!.enabled = false;
        PanelText.text = GetDebugText();
        foreach (Camera camera in Camera.allCameras)
        {
            if (!camera.isActiveAndEnabled || camera.cameraType != CameraType.Game)
            {
                continue;
            }
            float uiScale = Mathf.Max(0.75f, Screen.height / 720f);
            PanelText.fontSize = Mathf.RoundToInt(14f * uiScale);
            float width = Mathf.Min(Mathf.Max(100f, DebugPanelWidth) * uiScale, camera.pixelRect.width);
            PanelRect.sizeDelta = new Vector2(width, 250f);
            float height = PanelText.preferredHeight + 20f;
            if (TryGetDebugPanelRect(camera, new Vector2(width, height), out Rect rect))
            {
                rect = AvoidPanelOverlap(rect, camera);
                DebugPanelScreenRect = rect;
                PanelRect.sizeDelta = rect.size;
                PanelRect.anchoredPosition = new Vector2(rect.x, Screen.height - rect.yMax);
                Vector3 head = camera.WorldToScreenPoint(DebugAnchor);
                Vector2 end = new Vector2(rect.center.x, Screen.height - rect.yMax);
                Vector2 start = new Vector2(head.x, head.y);
                Vector2 delta = end - start;
                LinkRect.anchoredPosition = start;
                LinkRect.sizeDelta = new Vector2(delta.magnitude, 2f);
                LinkRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
                PanelCanvas.enabled = true;
                break;
            }
        }
    }

    private static Rect AvoidPanelOverlap(Rect rect, Camera camera)
    {
        if (LayoutFrame != Time.frameCount)
        {
            LayoutFrame = Time.frameCount;
            PlacedPanels.Clear();
        }
        Rect viewport = camera.pixelRect;
        for (int pass = 0; pass < PlacedPanels.Count; pass++)
        {
            foreach (Rect other in PlacedPanels)
            {
                if (!rect.Overlaps(other)) continue;
                if (other.xMax + 12f + rect.width <= viewport.xMax)
                    rect.x = other.xMax + 12f;
                else if (other.xMin - 12f - rect.width >= viewport.xMin)
                    rect.x = other.xMin - 12f - rect.width;
                else
                    rect.y = Mathf.Max(Screen.height - viewport.yMax, other.yMin - rect.height - 12f);
            }
        }
        PlacedPanels.Add(rect);
        return rect;
    }

    private void EnsurePanel()
    {
        if (PanelCanvas != null) return;
        var canvasObject = new GameObject($"{name} Debug Panel", typeof(RectTransform), typeof(Canvas));
        PanelCanvas = canvasObject.GetComponent<Canvas>();
        PanelCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        PanelCanvas.sortingOrder = 20;
        var link = new GameObject("Head Link", typeof(RectTransform), typeof(Image));
        LinkRect = link.GetComponent<RectTransform>();
        LinkRect.SetParent(canvasObject.transform, false);
        LinkRect.anchorMin = LinkRect.anchorMax = Vector2.zero;
        LinkRect.pivot = new Vector2(0f, 0.5f);
        link.GetComponent<Image>().color = new Color(0.4f, 0.75f, 0.9f, 0.8f);
        link.GetComponent<Image>().raycastTarget = false;
        var panel = new GameObject("Status", typeof(RectTransform), typeof(Image));
        PanelRect = panel.GetComponent<RectTransform>();
        PanelRect.SetParent(canvasObject.transform, false);
        PanelRect.anchorMin = PanelRect.anchorMax = PanelRect.pivot = Vector2.zero;
        Image background = panel.GetComponent<Image>();
        background.color = new Color(0.04f, 0.06f, 0.09f, 0.88f);
        background.raycastTarget = false;
        var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(PanelRect, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10f, 10f);
        textRect.offsetMax = new Vector2(-10f, -10f);
        PanelText = textObject.GetComponent<Text>();
        PanelFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 14);
        PanelText.font = PanelFont;
        PanelText.fontSize = 14;
        PanelText.alignment = TextAnchor.UpperLeft;
        PanelText.horizontalOverflow = HorizontalWrapMode.Wrap;
        PanelText.verticalOverflow = VerticalWrapMode.Truncate;
        PanelText.raycastTarget = false;
        PanelText.supportRichText = false;
        PanelText.color = Color.white;
    }

    private void OnDisable()
    {
        if (PanelCanvas != null) PanelCanvas.enabled = false;
    }

    private void OnDestroy()
    {
        if (PanelCanvas != null) Destroy(PanelCanvas.gameObject);
        if (PanelFont != null) Destroy(PanelFont);
    }

    /// <summary>投影到当前视口，面板底边跟随实体头顶；背后与视口外的实体不显示。</summary>
    public bool TryGetDebugPanelRect(Camera camera, Vector2 size, out Rect rect)
    {
        rect = default;
        if (!IsDebugPanelVisible)
        {
            return false;
        }
        Vector3 viewport = camera.WorldToViewportPoint(DebugAnchor);
        if (viewport.z <= camera.nearClipPlane || viewport.z > camera.farClipPlane
            || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
        {
            return false;
        }
        Vector3 screen = camera.WorldToScreenPoint(DebugAnchor);
        Rect bounds = camera.pixelRect;
        float top = Screen.height - bounds.yMax;
        float width = Mathf.Min(size.x, bounds.width);
        float height = Mathf.Min(size.y, bounds.height);
        rect = new Rect(
            Mathf.Clamp(screen.x - width / 2f, bounds.xMin, bounds.xMax - width),
            Mathf.Clamp(Screen.height - screen.y - height, top, top + bounds.height - height),
            width, height);
        return true;
    }

    public string GetDebugText()
    {

        EntityBrain brain = Entity.Brain;
        Vector3 velocity = brain.DebugVelocity;
        float horizontalSpeed = new Vector3(velocity.x, 0f, velocity.z).magnitude;

        // 各活跃层状态名（如"Sprint｜Air｜Attack"），未激活的层不显示
        string machineState = $"{DescribeLayer(EnumStateLayer.Locomotion)}｜{DescribeLayer(EnumStateLayer.Aerial)}";
        if (brain.StateMachine.GetActive(EnumStateLayer.Action) != null)
        {
            machineState += $"｜{DescribeLayer(EnumStateLayer.Action)}";
        }
        if (brain.StateMachine.GetActive(EnumStateLayer.CrowdControl) != null)
        {
            machineState += $"｜{DescribeLayer(EnumStateLayer.CrowdControl)}";
        }
        string attackLine = brain.StateMachine.GetActive(EnumStateLayer.Action) is EntityAttackState attack
            ? $"\n攻击 {attack.Phase}"
            : "";
        string weaponLine = DescribeWeapon();
        string armorLine = DescribeArmor();
        string inputLine = brain.InputSource != null ? brain.InputSource.GetType().Name : "无（站桩）";
        // 附身状态（buff 驱动：载体的剩余时长就是会话剩余时间，无需任何会话管理器）。
        // 两种身份只读展示；已有会话时禁止嵌套附身。
        string possessionLine = "";
        if (brain.IsPossessing)
        {
            possessionLine += $"\n附身中｜剩余 {brain.PossessionRemaining:0.0}s（到期自动换回）";
        }
        if (brain.HasSoulOut)
        {
            possessionLine += "\n灵魂出窍中（操作权在别处）";
        }

        return $"{name}（{(Entity.Brain.InputSource is PlayerInputSource ? "玩家" : "AI")}｜{inputLine}）" +
            $"\n水平速度 {horizontalSpeed:F2} m/s｜竖直速度 {velocity.y:F2} m/s" +
            $"\n状态机 {machineState}{attackLine}" +
            $"\n生命 {Entity.Vitals.CurrentHp:0}/{Entity.Vitals.MaxHp:0}｜信心 {Entity.Vitals.Faith?.Current ?? 0}/±{Entity.Vitals.FaithCapacity}" +
            $"\n冠冕 {DescribeSkill(Assets.Scripts.Entity.Data.Skill.EnumSkillType.Crown)}｜潮汐 {DescribeSkill(Assets.Scripts.Entity.Data.Skill.EnumSkillType.Tide)}" +
            $"\n效果 {brain.Modifiers.Describe()}" +
            $"\n标签 {Entity.Tags.Describe()}" +
            $"\n{weaponLine}" +
            $"\n{armorLine}" +
            possessionLine;
    }

    private string DescribeSkill(Assets.Scripts.Entity.Data.Skill.EnumSkillType kind)
    {
        SkillSlot slots = Entity.Slots.Skills;
        if (!slots.TryGet(kind, out SkillSO? skill, out float cooldown) || skill == null) return "未装备";
        bool burst = slots.IsBurstReady(kind, Entity.Vitals.Faith);
        bool canCast = Entity.Brain.Capability.CanCastSkill(kind);
        return $"{(canCast ? "可释放" : "不可释放")}{(burst ? "·强化" : "")}·CD {cooldown:0.0}s";
    }

    /// <summary>某层活跃状态名（未激活显示 "-"）</summary>
    private string DescribeLayer(EnumStateLayer layer)
    {
        EntityState? state = Entity.Brain.StateMachine.GetActive(layer);
        return state != null ? state.StateName : "-";
    }

    /// <summary>武器行（主/副手与双持判定、出招表键）——容量模型与 Sheet 解析的可见性</summary>
    private string DescribeWeapon()
    {
        WeaponSlot slot = Entity.Slots.Weapons;
        WeaponComboGraph? comboGraph = Entity.Slots.CurrentComboGraph;
        string mainHand = slot.MainHand != null ? slot.MainHand.Name : "空手";
        string offHand = slot.OffHand != null ? slot.OffHand.Name : "";
        string locomotion = comboGraph != null ? comboGraph.LocomotionStyle.ToString() : "无表";
        return $"武器 {mainHand}{offHand}（{(slot.IsDualWield ? "双持" : "单持")}，容量 {DescribeHandCost()}｜步态 {locomotion}）";
    }

    /// <summary>当前占用容量 / 角色总容量（未配 Config 时容量显示 0——与 CharacterVitals 的兜底口径一致）</summary>
    private string DescribeHandCost()
    {
        int used = Entity.Slots.WeaponCapacityConsumed;
        int capacity = Entity.Config != null ? Entity.Config.WeaponCapacity : 0;
        return $"{used}/{capacity}";
    }

    /// <summary>护甲行：已穿部位 + 件数 + 套装档位生效情况（套装引擎变更驱动的可见性）</summary>
    private string DescribeArmor()
    {
        ArmorSlot slot = Entity.Slots.Armor;
        System.Text.StringBuilder sb = new("护甲 ");
        int partCount = ArmorSlot.PartCount;
        for (int i = 0; i < partCount; i++)
        {
            ArmorSO? piece = slot.Get(i);
            if (piece != null)
            {
                sb.Append(ArmorSlot.PartAt(i) switch
                {
                    EnumArmorPart.Head => "头",
                    EnumArmorPart.Chest => "胸",
                    EnumArmorPart.Legs => "腿",
                    EnumArmorPart.Feet => "足",
                    _ => "?",
                });
            }
        }

        ArmorSetBonusList armorSets = Entity.Brain.ArmorSets;
        sb.Append(' ').Append(slot.EquippedCount).Append('/').Append(partCount);
        sb.Append("｜套装 ").Append(armorSets != null ? armorSets.Describe() : "无");
        return sb.ToString();
    }
}
