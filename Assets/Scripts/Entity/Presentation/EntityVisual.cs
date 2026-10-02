using UnityEngine;

/// <summary>
/// 实体表现（Presentation 层，占位）：只读 Data/Logic/Physics 的状态做呈现——
/// 本轮交付调试 HUD（旧 PlayerController.OnGUI 面板迁移）；动画/特效/外观后置
/// （接入点：状态 Enter/Exit + locomotionStyle 选型键 + HpChanged/Died/EquipmentChanged 事件）。
/// 只读纪律：本组件不改任何数据——唯一例外是"表现层自持对象"（将来的 Renderer 显隐/粒子）。
/// 挂在实体物体上；与 Entity 同生共死（RequireComponent 直连，省一次 null 判断）。
/// </summary>
[RequireComponent(typeof(Entity))]
public sealed class EntityVisual : MonoBehaviour
{
    [Header("调试")]
    [Tooltip("是否显示左上角调试 HUD（速度/状态机/生命/效果/标签/装备）")]
    public bool ShowDebugHud = true;

    private Entity Entity;
    private GUIStyle HudStyle;   // OnGUI 用，延迟创建

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
        if (brain.Machine.GetActive(EnumStateLayer.Action) != null)
        {
            machineState += $"｜{DescribeLayer(EnumStateLayer.Action)}";
        }
        if (brain.Machine.GetActive(EnumStateLayer.CrowdControl) != null)
        {
            machineState += $"｜{DescribeLayer(EnumStateLayer.CrowdControl)}";
        }
        string attackLine = brain.Machine.GetActive(EnumStateLayer.Action) is EntityAttackState attack
            ? $"\n攻击 {attack.Phase}"
            : "";
        string weaponLine = DescribeWeapon();
        string inputLine = brain.InputSource != null ? brain.InputSource.GetType().Name : "无（站桩）";

        GUI.Label(new Rect(10f, 10f, 340f, 260f),
            $"{name}（{(Entity.IsPlayerControlled ? "玩家" : "AI")}｜{inputLine}）" +
            $"\n水平速度 {horizontalSpeed:F2} m/s｜竖直速度 {velocity.y:F2} m/s" +
            $"\n状态机 {machineState}{attackLine}" +
            $"\n生命 {Entity.Vitals.CurrentHp:0}/{Entity.Vitals.MaxHp:0}｜信心 {Entity.Vitals.Faith.Current}/±{Entity.Vitals.FaithCapacity}（钟摆）" +
            $"\n效果 {brain.Modifiers.Describe()}" +
            $"\n标签 {Entity.Tags.Describe()}" +
            $"\n{weaponLine}",
            HudStyle);
    }

    /// <summary>某层活跃状态名（未激活显示 "-"）</summary>
    private string DescribeLayer(EnumStateLayer layer)
    {
        EntityState state = Entity.Brain.Machine.GetActive(layer);
        return state != null ? state.StateName : "-";
    }

    /// <summary>武器行（主/副手与双持判定、出招表键）——容量模型与 Sheet 解析的可见性</summary>
    private string DescribeWeapon()
    {
        WeaponSlot slot = Entity.Slots.Weapons;
        WeaponComboGraph comboGraph = Entity.Slots.CurrentComboGraph;
        return $"武器 {slot.MainHand.Name}{slot.OffHand.Name}（{(slot.IsDualWield ? "双持" : "单持")}，容量 {DescribeHandCost()}｜步态 {comboGraph.LocomotionStyle}）";
    }

    /// <summary>当前占用容量 / 角色总容量</summary>
    private string DescribeHandCost()
    {
        int used = Entity.Slots.WeaponCapacityConsumed;
        int capacity = Entity.config.WeaponCapacity;
        return $"{used}/{capacity}";
    }
}
