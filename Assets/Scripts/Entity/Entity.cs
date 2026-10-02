using UnityEngine;

/// <summary>
/// 实体（组合根，七层架构的唯一角色类——需求钦定：所有角色统一叫 Entity，不再有
/// Player/NPC/Character 子类。玩家与敌人的差异 = 开局输入源绑定（startPlayerControlled）
/// + 槽位内容 + 决策树 + Config，不来自继承）。
/// 「当前是否玩家控制」不存布尔——唯一真相是 Brain.InputSource（is PlayerInputSource
/// 即玩家驱动；附身切换 = 换绑输入源，查询方一律问 InputSource）。
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
    public CharacterConfigSO? Config;

    [Tooltip("实体标识（多人预留：将来映射 NetworkObject/网络 id；本地无用）")]
    public int EntityId;

    [Tooltip("开局绑定玩家输入源（Bootstrap 据此选 PlayerInputSource / AITreeInputSource；"
        + "只管谁来下指令，不管指令能不能执行——那是 Capability 的事）。"
        + "运行时「是否玩家控制」的唯一真相是 Brain.InputSource（is PlayerInputSource），"
        + "控制权转移 = Brain.BindInputSource（V / LB 附身演示）")]
    [SerializeField] private bool startPlayerControlled;

    /// <summary>开局绑定选择（Brain.Bootstrap 读一次；运行时控制状态看 Brain.InputSource）</summary>
    public bool StartPlayerControlled => startPlayerControlled;

    // ---- 组件缓存（唯一 Awake 取一次；不在玩法路径反复 GetComponent）----
    // 这批由 Awake（单入口，先于任何玩法调用）注入，故按非空不变量声明；
    // `= null!` 是断言不是赋值（零运行时开销）——避免每个读点都写 `?.`
    public EntityBrain Brain { get; private set; } = null!;
    public EntityMotor Motor { get; private set; } = null!;
    public CharacterVitals Vitals { get; private set; } = null!;
    public CharacterSlotContainer Slots { get; private set; } = null!;

    /// <summary>命名标签集合（状态直写 + Modifier 投影，见 CharacterTagSet）</summary>
    public CharacterTagSet Tags { get; } = new CharacterTagSet();

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
