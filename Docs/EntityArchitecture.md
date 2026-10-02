# 实体架构（七层分层 · Entity 统一）

> 适用范围：Crown Tide 角色系统（Assets/Scripts/Entity/ 全部 39 个文件）。
> 设计依据：`c:\Users\14414\Desktop\角色设计.md`（用户钦定架构文档）。
> 2026-10 按七层架构推倒重建；**旧架构（黑板+决策树+状态机三件套）的设计记录见文末附录
> 与 git tag `pre-entity-rebuild`**（分层状态机/电平边沿指令/效果三成分等思想平移保真进新系统）。
> 战斗排期见 `Docs/CombatSystemPlan.md`。

## 一、七层分层与数据流

```
Input Layer        玩家输入 / AI 决策树 —— 回答"Entity 要做什么"
    ↓
Brain Layer        输入源抽象 + 角色大脑 —— 回答"怎么做"（每帧编排）
    ↓
Logic Layer        读 Data 判断执行写回（Capability 统一查"能不能"）
    ↓
Physics Layer      移动 / 重力 / 地面检测
    ↓
Presentation Layer 动画 / 外观 / 特效 / UI / 相机（只读不修改）
    ↑                   ↑
Data Layer ────────────┘  （Logic 与 Presentation 都读：Entity 的内存 database）
    ↑
Network Layer（横切，本轮只留接缝不实现——见 §十一）
```

| 层 | 文件 | 职责 |
|---|---|---|
| Entity（组合根） | `Entity.cs` | RequireComponent 五件套、唯一 Awake、只读门面 |
| Brain | `EntityBrain.cs` | 唯一 Update 编排、输入源绑定、指令消费翻译、伤害统一入口 |
| Input | `Input/IInputSource.cs`、`CommandBuffer.cs`、`PlayerInputSource.cs`、`AI/AITreeInputSource.cs`、`AI/Decision/` 四件 | 设备/决策 → 指令 |
| Logic | `Logic/`（状态机三件 + Capabilities + 七状态 + `Modifier/` 五件） | 状态流转、动作执行、Buff/Debuff |
| Data | `Data/`：`SlotContainer.cs`（四槽聚合根）+ `Enums.cs` 跨域共享留根；按域四夹：`Character/`（CharacterConfigSO/CharacterVitals/CharacterTagSet，均 Character 前缀）、`Weapon/`（WeaponSO/WeaponComboGraph/WeaponSlot）、`Skill/`（SkillSO/SkillSlot/SkillResource）、`Armor/`（ArmorSO/ArmorSlot）与 `Accessories/`（AccessorySO/AccessorySlot），一文件一类 | 槽位/资源/数值面板——Entity 有什么 |
| Physics | `Physics/EntityMotor.cs` | 运动原语与参数 |
| Presentation | `Presentation/EntityVisual.cs`（HUD）、`CameraRig.cs`（相机） | 只读呈现 |

## 二、核心命名约定：Entity 统一 + IsPlayerControlled

所有角色统一叫 **Entity**，不再有 Player/NPC/Character 三层子类（组件层面零继承）。
`bool IsPlayerControlled` 的**唯一职责**是决定 `EntityBrain` 绑定哪个输入源：
`true → PlayerInputSource`，`false → AITreeInputSource`（双源同挂物体，切换只换绑定）。
它只管"谁来下指令"，不管"指令能不能执行"：

| 问题 | 裁决者 |
|---|---|
| 能不能移动/攻击/跳跃 | `EntityCapabilities`（Logic） |
| 血量/伤害/霸体 | `CharacterVitals` / `EntityBrain.TakeDamage` / `Capability.HasControlImmunity` |
| 槽位/装备/技能 | `SlotContainer` + `SkillSlot`（Data） |
| 外观/动画/相机 | `EntityVisual` / `CameraRig`（Presentation） |

玩家操控 AI（附身）= 把目标 Entity 的 `IsPlayerControlled` 置 true（运行时 setter →
`Brain.BindInputSource` 重绑，F10 调试键演示），原角色置 false 换回 AI 源。
强度差异来自**槽位内容 + 决策树 + config**，不来自类继承。

## 三、组件组合与初始化规则

```
Entity（组合根）
 ├─ RequireComponent: CharacterController / EntityBrain / EntityMotor / CharacterVitals / SlotContainer
 ├─ EntityBrain ── RequireComponent: EntityMotor / CharacterVitals / SlotContainer（单向，不反向要求 Entity）
 ├─ PlayerInputSource ── RequireComponent: PlayerInput（输入源自己要求）
 ├─ AITreeInputSource ──（无要求）
 ├─ EntityVisual ── RequireComponent: Entity（表现层直连）
 └─ CameraRig ── 挂相机物体，持 Entity 引用（不属于实体组件族）
```

- **单 Awake 规则**：全实体唯一 Awake 在 `Entity`——缓存五组件 → `Brain.Bootstrap(this)`
  （装配状态机/七状态/Capability/ModifierList/CommandBuffer → `Vitals.Initialize(config)` →
  绑输入源）。其余组件零 Awake（PlayerInputSource 懒初始化在首次 SetActive）——
  根治同物体多组件 Awake 顺序未定义的竞态。
- **唯一 Update**：全实体仿真集中在 `EntityBrain.Update`；`CameraRig` 用 LateUpdate
  （表现层独立节拍）。将来切 ServerTick 只搬 Brain 一个函数。
- **纯 C# 件**（Brain 构造持有，不是 MonoBehaviour）：`EntityStateMachine`、`ModifierList`、
  `EntityCapabilities`、`CommandBuffer`、`SkillResource`——无 Inspector 数据、无独立节拍，
  挂组件只会多乱序源。

## 四、每帧管线（EntityBrain.Update，顺序勿调整）

| # | 步骤 | 语义要点 |
|---|---|---|
| 1 | `Commands.ResetLevels()` | 电平帧首重置：输入源沉默 = 站桩（附身切换帧安全） |
| 2 | `InputSource.GatherCommands(Commands)` | 玩家/AI 汇流同一缓冲；瞄准射击翻译在 6 |
| 3 | `Modifiers.Tick(dt)` + `skills.TickCooldown(dt)` | 投影先于状态机 = 失控当帧压制；帧末施加的效果次帧压制 |
| 4 | 死亡门：`IsDead → ClearEdges + return` | 不用 enabled=false（会杀网络回调）；尸体站桩 |
| 5 | `Motor.GroundCheck()` | 贴地钳 -2 防下落速度累积 |
| 6 | `TryConsumeAction()` | 攻击起手（含瞄准射击翻译）+ 技能双闸门；起手当帧进前摇 |
| 7 | `Machine.TwoPassTick(dt)` | 全层转换 → 全层动作；连段推进在 AttackState.Tick |
| 8 | `TryConsumeJump()` | 冲量过 Capability 门禁（防绕过层压制） |
| 9 | `Motor.ApplyGravityAndVerticalMove(dt)` | 顶点滞空/下落加重 |
| 10 | `Motor.RotateTowards(...)` | 有方向时平滑转向 |
| 11 | 调试采样 | 位置差反推实测速度（HUD 用） |

`deltaTime` 全链传参（状态/冷却/决策全用累积器，不用 Time.time 差值）——
换 NetworkTime/固定 tick 只改 Brain 取时一处。

## 五、指令语义（CommandBuffer：Input → Logic 的唯一桥）

| 类型 | 字段 | 语义 |
|---|---|---|
| 电平型（帧首重置） | `MoveDirection` / `SprintActive` / `AimActive` | 表达"现在正在"；本帧不写 = 本帧站桩 |
| 边沿型（消费点清空） | `JumpQueued` / `AttackQueued` / `SkillSlotQueued` | 表达"请求一次"；消息回调/决策在帧间置位，无缓冲 |

指令不区分来源（玩家按键与 AI 决策写同一个缓冲）；"指令翻译成什么"归消费点：
**瞄准电平 + 攻击边沿 + Sheet.hasShoot → 射击变体**（Brain.TryConsumeAction，
不放输入源——AI 源自动同享规则不漂移）。
网络镜像就绪：全部原始类型扁平字段——将来 NGO `INetworkSerializable` 原样拷贝。

## 六、状态机（Logic：分层 · 互斥叠加 · 压制 · 两遍 Tick）

`EnumStateLayer`：Locomotion(0) / Aerial(1) / Action(2) / CrowdControl(3)——
同层互斥、异层叠加（奔跑中跳跃 = Sprint + Air 两层并跑）。

- **两遍 Tick**：每帧先全层转换判定、再全层动作执行——转换帧由新状态执行本帧动作
  （切换当帧即换速度），每层不做链式转换。
- **压制规则**（`EnumStateLayerRules`）：CC 层活跃时其余层冻结（不转换不执行但状态保留），
  物理照常；解除后自动恢复。压制不是清除：眩晕前在跑，解除后输入仍在就继续跑。
- **七状态**（`Logic/States/`）：Idle/Walk/Sprint（Locomotion，共用 ApplyLocomotion 出口：
  锁移动仲裁 + MoveSpeed 乘法链 + Sheet 移速修正一处生效）、Grounded/Air（Aerial，只管
  离地/落地时机）、Attack（Action，读出招表的通用连段执行器）、Stun（CC，轮询
  `HasControlActive` 自清 + 站桩）。
- **能力声明模式**：`GrantsSuperArmor` / `LocksMovement` 是构造期设定的普通属性——
  状态声明能力，别处仲裁（Capability / ApplyLocomotion）。
- **失控呈现注册表**：`ModifierData.controlKind`（EnumControlKind）→ Brain 的
  `Dictionary<EnumControlKind, EntityState>` 映射 CC 状态。新失控（冰冻/石化）=
  枚举加成员 + 新状态类 + 注册一行，全"加"零"改"。
- 继承豁免声明：`abstract EntityState → 七个 sealed 状态`是全项目唯一继承点（深 1）——
  多态分发的"万不得已"（机器需要统一句柄存层槽 + Enter/Exit 钩子 + 共享移动助手）；
  组件层面零继承。

## 七、Modifier 三成分（Buff 与 Debuff 是同一系统的正负两半）

一条 `ModifierData`（SO）= 控制/数值/周期三成分，可只开其一（急速=纯数值、眩晕=纯控制、
创伤=纯周期）。运行时状态定格在 `ModifierList` 的条目上——**模板/实例分层**：
SO 是共享模板（绝不运行时改：全场生效 + Play 修改持久化写脏），条目是施加实例。

| 成分 | 归宿 |
|---|---|
| 控制（hasControl） | 投影 CC 层（见 §六注册表）；霸体在 Apply 入口仲裁，施加时刻定死 `controlActive`（免疫≠解控） |
| 数值（statModifiers） | `GetStatMultiplier(EnumStatType)` 乘法链；Stack 条目按层数自乘（指数叠加） |
| 周期（hasPeriodic） | 统一 tick 跳伤走 `Brain.TakeDamage` 统一入口 |

**多挂载单表达**：任意多条目并挂各自倒计时；失控条目取 controlPriority 最高者呈现
（同强度先挂者保持），更高者接管、逐个解除自动降级。数据层多挂、行为层单表达。
叠加策略 Refresh/Stack/Ignore；驱散按 Category 位与过滤（`Dispel(Poison)`=驱毒、
`Dispel(Debuff)`=净化）。
标签成分（grantedTag）：条目活跃期间 Entity 持有该标签——纯命名读写解耦
（写方不定义效果，读方是解释器：Swinging × 武器减伤乘数）。
消费读点清单见 `EnumStatType.cs` 头注释（基础值分裂位置一并登记）。

## 八、Data 层数据模型（Entity 的内存 database）

**新增资源类型只在 Data 层加字段，不污染 Logic**；Logic 读它判断，Presentation 读它展示。

| 部件 | 说明 |
|---|---|
| `CharacterConfigSO` | maxHealth / faithCapacity=67 / weaponCapacity=5（活值直读） |
| `WeaponSO` | EnumWeaponType / handCost（巨斧5·匕首2·圣剑9）/ attackSpeed / swing 乘数 / 单持·双持两张出招表 |
| `WeaponComboGraph` | locomotionStyle 步态键 / moveSpeedMultiplier / comboEntries 连段 / hasCharge+chargeEntry 蓄力 / hasShoot+shootEntry 瞄准射击 |
| `SkillSO` | kind（冠冕/潮汐，枚举值 ±1 = **方向因子**）/ faithDelta（正数幅度）/ cooldown——**双闸门**：冷却（SkillSlot 记剩余）与信心方向闸门独立 |
| `ArmorSO` / `AccessorySO` | 占位持有；Data 层不处理逻辑（饰品给 buff/绑快捷键是 Logic/Presentation 的事） |
| `SlotContainer` | 四槽聚合 + TryEquip*（EnumEquipResult）+ EquipmentChanged 事件 |
| `CharacterVitals` | HP + 信心值（SkillResource：CanApply 方向闸门 + Update(int) 唯一写口）+ 死亡事实 + HpChanged/Died 事件；ApplyDamage 只收调用侧算好的终值 |
| `CharacterTagSet` | 位运算标签容器（状态直写 + 容器投影 SyncOwned 位域划分） |

**容量模型（需求钦定）**：`main.handCost + secondary.handCost ≤ config.weaponCapacity`——
容量 5 时：双匕首 4 可行、巨斧 5 单持可行但无法双持、圣剑 9 装不上。
新武器 = 一份 WeaponSO + 两张表，零代码。
**信心钟摆（需求钦定）**：信心 ∈ `[-faithCapacity, +faithCapacity]` 对称区间、初始 0 居中；
实际增量 = `(int)kind × faithDelta`——**枚举值即方向因子**（Crown=+1 涨、Tide=-1 降，
faithDelta 只配正数幅度，方向由技能位钦定不可能配错）；
`CanApply` 方向闸门贴边锁向（推到 +67 就锁冠冕、必须换潮汐拉回）——防单一技能依赖。
`SkillSlotQueued` 以 0 为"无请求"哨兵（0 不是合法技能位，±1 是两技能位）。
**出招表解析**：双持取主手 `comboGraphDual ?? comboGraphSingle`；单持用其单持表；空手/缺表回退
unarmedComboGraph（再缺 = AttackState 内置默认节奏）。
**数值面板是分布式的**：HP/信心值在 Vitals、移速在 Motor、冷却在 SkillSlot、容量在 config
——Entity 门面聚合成读点，不做大而全 Stats 字典。

## 九、Physics 与运动

`EntityMotor`：旧 EntityController 运动原语平移（去继承变服务）——GroundCheck（贴地钳 -2）、
MoveHorizontal（零方向也 Move：贴地/去穿插）、ApplyGravityAndVerticalMove（顶点滞空 +
下落加重）、RotateTowards。参数全活值；"要不要动"的决策在 Capability/状态机，不归 Motor。

## 十、Presentation 只读纪律

只读 Data/Logic/Physics 状态，不改数据；唯一例外是表现层自持对象（Renderer 显隐）。

- `EntityVisual`（占位）：调试 HUD（速度/状态机/攻击段/生命/信心/效果/标签/装备容量）；
  动画接入点 = 状态 Enter/Exit + locomotionStyle 选型键 + 事件订阅。
- `CameraRig`（角色系统呈现三行为）：抬头 90°（俯仰上限 + 往上地面保护收缩）、
  **低头相机靠近**（低头超阈值沿视线渐缩）、**奔跑加速**（读 `Entity.IsSprinting` FOV 平滑拉大）。
  持 Entity 引用（多人：每玩家自己的相机，spawn 指派）；把自身 Transform 注入
  PlayerInputSource.viewTransform（移动方向的投影基准——全项目无 Camera.main/Find*）。

## 十一、多人就绪清单（本轮只留接缝，兑现点）

| 接缝 | 兑现点 |
|---|---|
| CommandBuffer 扁平原始字段 = 线上输入格式 | `Input/CommandBuffer.cs` |
| IInputSource 即网络接缝（NetworkInputRelay 顶替零改动） | `Input/IInputSource.cs` |
| 无 Camera.main/Find*（viewTransform 注入） | PlayerInputSource + CameraRig |
| 时间纪律：全链 deltaTime/累积器，无 Time.time 差值 | Brain/状态/SkillSlot/AITree |
| 死亡不用 enabled=false（门禁拦截） | Brain 死亡门 + Vitals.IsDead |
| 无静态/单例；伤害唯一入口留服务端校验单点 | Brain.TakeDamage → Vitals.ApplyDamage |
| 唯一 Update 编排（切 ServerTick 搬一处） | Brain.Update |
| Data 即同步面（SO 引用将来换 id 注册表；CharacterTagSet 位掩码整包同步） | Data/ 全部 |
| 表现层事件驱动（HpChanged/Died/EquipmentChanged） | Vitals/SlotContainer |
| entityId 预留 | Entity.entityId |
| 随机将来 per-entity 注入 System.Random（本轮玩法无随机） | — |

## 十二、调试链路（PlayerInputSource，设备直读不走输入资源）

| 键 | 功能 |
|---|---|
| F1 / F2 | 切肩 / 切第一人称（CameraRig 输入） |
| F3 / F4 / F5 | 对自身施加 眩晕 / 急速 / 创伤（调试槽拖 ModifierData） |
| F6 | 全驱散 |
| F7 | 场景内其他实体施加眩晕（敌人侧回归） |
| F9 | 请求冠冕技能（双闸门 + 信心钟摆涨向冒烟；贴边 +67 后锁向拒绝） |
| F10 | 附身切换（IsPlayerControlled 翻转，双输入源重绑演示） |
| F11 | 请求潮汐技能（信心钟摆降向；贴边 -67 后锁向拒绝，两键互为回摆） |

---

## 附录：旧架构选型思想（重建前记录，快照见 git tag `pre-entity-rebuild`）

对任何一个"会动的东西"，依次问三个问题——架构跟着需求走，"用上了框架"本身不是目标，
**"删掉一个部件也能工作"才是框架分层正确的标志**：

| 问题 | 回答"是"需要 | 新架构归宿 |
|---|---|---|
| 它需要自己决定"做什么"吗？ | 决策层 | AITreeInputSource（Input 层，可插拔零件） |
| 它的行为有"阶段过程"吗？（前后摇/蓄力，需要 Enter/Exit） | 状态机 | EntityStateMachine（Logic 层） |
| 它的各部件间要共享运行数据吗？ | 黑板 | 已拆解：指令→CommandBuffer、运动→Motor、数值→Vitals/CharacterTagSet（黑板作为独立件消亡，职责未丢） |

不是所有会动的东西都要三件套；投射物/机关等轻量单位将来按需取用
（Motor + 自选 Logic，不挂 Entity 全家）。
