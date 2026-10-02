using Assets.Scripts.Entity.Data.Armor;
using UnityEngine;

/// <summary>
/// 实体表现（Presentation 层，占位）：只读 Data/Logic/Physics 的状态做呈现——
/// 本轮交付调试 HUD（旧 PlayerController.OnGUI 面板迁移；含护甲部位/件数/套装档位）；动画/特效/外观后置
/// （接入点：状态 Enter/Exit + locomotionStyle 选型键 + HpChanged/Died/EquipmentChanged 事件）。
/// 只读纪律：本组件不改任何数据——唯一例外是"表现层自持对象"（将来的 Renderer 显隐/粒子）。
/// 挂在实体物体上；与 Entity 同生共死（RequireComponent 直连，省一次 null 判断）。
/// </summary>
[RequireComponent(typeof(Entity))]
public sealed class EntityVisual : MonoBehaviour
{
    [Header("调试")]
    [Tooltip("是否显示左上角调试 HUD（速度/状态机/生命/效果/标签/装备/护甲套装）")]
    public bool ShowDebugHud = true;

    private Entity Entity = null!;   // Awake 注入（RequireComponent 保证存在）
    private GUIStyle HudStyle = null!;   // OnGUI 用，延迟创建（??=）

    private void Awake()
    {
        Entity = GetComponent<Entity>();
    }

    // OnGUI 每帧会被调用多次，只做纯显示（拼串有分配，不上玩法路径——HUD 可关）
    private void OnGUI()
    {
        if (!ShowDebugHud || Entity == null)
        {
            return;
        }

        HudStyle ??= new GUIStyle(GUI.skin.box)
        {
            fontSize = 16,
            alignment = TextAnchor.UpperLeft,
            padding = new RectOffset(10, 10, 8, 8),
        };

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
        // 两种角色可能同时存在（附身中的人又被附身），故两行独立判断而非二选一
        string possessionLine = "";
        if (brain.IsPossessing)
        {
            possessionLine += $"\n附身中｜剩余 {brain.PossessionRemaining:0.0}s（到期自动换回）";
        }
        if (brain.HasSoulOut)
        {
            possessionLine += "\n灵魂出窍中（操作权在别处）";
        }

        GUI.Label(new Rect(10f, 10f, 340f, 285f),
            $"{name}（{(Entity.Brain.InputSource is PlayerInputSource ? "玩家" : "AI")}｜{inputLine}）" +
            $"\n水平速度 {horizontalSpeed:F2} m/s｜竖直速度 {velocity.y:F2} m/s" +
            $"\n状态机 {machineState}{attackLine}" +
            $"\n生命 {Entity.Vitals.CurrentHp:0}/{Entity.Vitals.MaxHp:0}｜信心 {Entity.Vitals.Faith?.Current ?? 0}/±{Entity.Vitals.FaithCapacity}（钟摆）" +
            $"\n效果 {brain.Modifiers.Describe()}" +
            $"\n标签 {Entity.Tags.Describe()}" +
            $"\n{weaponLine}" +
            $"\n{armorLine}" +
            possessionLine,
            HudStyle);
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
