using UnityEngine;

/// <summary>
/// 实体（组合根，七层架构的唯一角色类——需求钦定：所有角色统一叫 Entity，不再有
/// Player/NPC/Character 子类。玩家与敌人的差异 = IsPlayerControlled（只决定输入源绑定）
/// + 槽位内容 + 决策树 + config，不来自继承）。
/// 本类只做三件事：
/// 1. RequireComponent 声明组件组合（组合优于继承）；
/// 2. 唯一 Awake：缓存组件 → Brain.Bootstrap（单 Awake 规则——同物体多组件 Awake 顺序
///    未定义，统一入口根治初始化竞态；其余组件零 Awake）；
/// 3. 只读门面：把 Logic/Presentation 常读的查询聚合成短路径（不透传全组件——防 god-object）。
/// 门禁不在这里：能不能 = Capability，血量伤害 = Vitals/Brain.TakeDamage，
/// 装备 = SlotContainer，外观 = EntityVisual（各答其职，需求钦定）。
/// 网络留坑：entityId 预留映射 NetworkObject；全实体无静态/单例依赖。
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(EntityBrain))]
[RequireComponent(typeof(EntityMotor))]
[RequireComponent(typeof(CharacterVitals))]
[RequireComponent(typeof(CharacterSlotContainer))]
public sealed class Entity : MonoBehaviour
{
    [Header("角色数据")]
    [Tooltip("角色配置（体质/信心上限/手部容量；空 = 各组件内置默认值）")]
    public CharacterConfigSO config;

    [Tooltip("实体标识（多人预留：将来映射 NetworkObject/网络 id；本地无用）")]
    public int EntityId;

    [Tooltip("是否玩家操控（需求钦定唯一职责：决定 Brain 绑定 PlayerInputSource 还是 AITreeInputSource；"
        + "只管谁来下指令，不管指令能不能执行——那是 Capability 的事）")]
    [SerializeField] private bool isPlayerControlled;

    // ---- 组件缓存（唯一 Awake 取一次；不在玩法路径反复 GetComponent）----
    public EntityBrain Brain { get; private set; }
    public EntityMotor Motor { get; private set; }
    public CharacterVitals Vitals { get; private set; }
    public CharacterSlotContainer Slots { get; private set; }

    /// <summary>命名标签集合（状态直写 + Modifier 投影，见 CharacterTagSet）</summary>
    public CharacterTagSet Tags { get; } = new CharacterTagSet();

    /// <summary>是否玩家操控。运行时 set = 附身/换脑（Brain 重绑输入源），
    /// Inspector 勾选则在下一次 Bootstrap 生效</summary>
    public bool IsPlayerControlled
    {
        get => isPlayerControlled;
        set
        {
            isPlayerControlled = value;
            if (Brain != null)
            {
                Brain.BindInputSource(value);
            }
        }
    }

    /// <summary>指令缓冲直通（状态读指令的短路径）</summary>
    public CommandBuffer Commands => Brain.Commands;

    // ---- 常用只读门面（Logic/Presentation 高频读点）----

    /// <summary>是否已死亡（管线门禁/UI 判定）</summary>
    public bool IsDead => Vitals.IsDead;

    /// <summary>是否冲刺中（Locomotion 层活跃态判定；CameraRig 读它做奔跑加速的呈现）</summary>
    public bool IsSprinting => Brain.StateMachine.GetActive(EnumStateLayer.Locomotion) is EntitySprintState;

    private void Awake()
    {
        Brain = GetComponent<EntityBrain>();
        Motor = GetComponent<EntityMotor>();
        Vitals = GetComponent<CharacterVitals>();
        Slots = GetComponent<CharacterSlotContainer>();
        Brain.Bootstrap(this);
    }
}
