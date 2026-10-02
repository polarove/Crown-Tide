# 实体架构（七层分层 · Entity 统一）

> 适用范围：Crown Tide 角色系统（Assets/Scripts/Entity/ 全部 39 个文件）。
> 设计依据：`c:\Users\14414\Desktop\角色设计.md`（用户钦定架构文档）。
> 2026-10 按七层架构推倒重建；**旧架构（黑板+决策树+状态机三件套）的设计记录见文末附录
> 与 git tag `pre-entity-rebuild`**（分层状态机/电平边沿指令/效果三成分等思想平移保真进新系统）。
> 与 `Docs/CombatSystemPlan.md`（战斗排期）、`Docs/ChangeLog.md`（本次落地改动与验证证据）配合阅读。
> 内容制作（新武器/Buff/角色/敌怪/Boss 怎么加）见 `Docs/ContentAuthoring.md`；已知设计债见本文 §十五。

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
| Logic | `Logic/`（状态机三件 + Capabilities + 七状态 + `Modifier/` 五件 + `Armor/ArmorSetBonusList`） | 状态流转、动作执行、Buff/Debuff、护甲套装档位 |
| Data | `Data/`：`SlotContainer.cs`（四槽聚合根）+ `Enums.cs` 跨域共享留根；按域四夹：`Character/`（CharacterConfigSO/CharacterVitals/CharacterTagSet，均 Character 前缀）、`Weapon/`（WeaponSO/WeaponComboGraph/WeaponSlot）、`Skill/`（SkillSO/SkillSlot/SkillResource）、`Armor/`（ArmorSO/ArmorSetSO/ArmorSlot/EnumArmorPart）与 `Accessories/`（AccessorySO/AccessorySlot），一文件一类 | 槽位/资源/数值面板——Entity 有什么 |
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

**不占管线步骤的变更驱动件**：护甲套装档位重算（`ArmorSets.Sync`）只在装备/卸下护甲与
`Bootstrap` 时各跑一次——装备状态是离散事件，不需要每帧求值（见 §十三）。

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

**摘除两条路**：`Dispel(EnumModifierCategory)` 按类别位批量清（驱散/净化/Poison）；
`Remove(ModifierEffect)` 按 SO 引用精确摘单条（护甲套装破套/换档用）。
`EnumModifierCategory` 中的 `ArmorSet` 是**来源命名**位（不是驱散媒介）：`All` 刻意不含它——
装备来源的效果由装备状态派生，不该被"净化/全驱散"语义清掉（F6 全驱散后套装档位会在下次
装备变更重算时补回）。

## 八、Data 层数据模型（Entity 的内存 database）

**新增资源类型只在 Data 层加字段，不污染 Logic**；Logic 读它判断，Presentation 读它展示。

| 部件 | 说明 |
|---|---|
| `CharacterConfigSO` | maxHealth / faithCapacity=67 / weaponCapacity=5（活值直读） |
| `WeaponSO` | EnumWeaponType / handCost（巨斧5·匕首2·圣剑9）/ attackSpeed / swing 乘数 / 单持·双持两张出招表 |
| `WeaponComboGraph` | locomotionStyle 步态键 / moveSpeedMultiplier / comboEntries 连段 / hasCharge+chargeEntry 蓄力 / hasShoot+shootEntry 瞄准射击 |
| `SkillSO` | kind（冠冕/潮汐，枚举值 ±1 = **方向因子**）/ faithDelta（正数幅度）/ cooldown——**双闸门**：冷却（SkillSlot 记剩余）与信心方向闸门独立 |
| `ArmorSO` | 部位（头/胸/腿/足）+ 护甲值（本轮只挂账）+ 所属 `ArmorSetSO`（空 = 散件，不参与套装计数） |
| `ArmorSetSO` | 护甲套装（`CreateAssetMenu → Crown Tide/护甲套装`）：名字 + 档位表 `[ {件数门槛, ModifierEffect} ]`。Data 只存表不求值 |
| `AccessorySO` | 占位持有；Data 层不处理逻辑（饰品给 buff/绑快捷键是 Logic/Presentation 的事） |
| `SlotContainer` | 四槽聚合 + `TryEquipWeapon`/`UnequipWeapon` + `TryEquipArmor`/`UnequipArmor`（EnumEquipResult）+ `EquipmentChanged` 事件 + 持有 `ArmorSetBonusList` |
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

## 十三、护甲套装（逐档累加 + 变更驱动）

护甲共 4 件（头/胸/腿/足），一件护甲可属一个套装（`ArmorSO.Set`）。套装资产
（`ArmorSetSO`）配一张**档位表**：每条 = `{件数门槛, ModifierEffect}`。

**档位语义（需求钦定，逐档累加）**：门槛含义 = 「已穿件数 ≥ PieceCount 即激活」——
穿 2 件激活二件档；**穿满 4 件时二件档与四件档同时生效**（两条各自作为独立条目进
`ModifierList`，各自走乘法链），不是"高档替换低档"。数值配置注意：两档若改同一 stat 会相乘，
所以高档通常配"增量"（如四件档配 1.05 而不是 1.25）。

| 环节 | 归属 |
|---|---|
| 件数与套装查询 | `ArmorSlot`（Data，只读）：`EquippedCount` / `CountOf(set)` / `Get(index)` |
| 求值与挂摘 | `ArmorSetBonusList`（Logic，纯 C#，Brain 构造持有）：`Sync()` 收集达标档位 → 与已挂集合差分 → 摘不再达标的、挂新增的 |
| 触发点 | 变更驱动：`SlotContainer.TryEquipArmor` / `UnequipArmor` 成功后调 `Sync()`；`Bootstrap` 末尾再调一次（覆盖 Inspector 预配的初始装备）。**不占每帧管线** |
| 数据流 | 装备写入点 ⇒ 套装重算成对（容器持有引擎引用 `ArmorSets`/`ArmorSetBonusList`），无事件反向订阅、无隐藏顺序 |
| 幂等与自愈 | 同档位已在挂则不重复 `Apply`（不搅动 Refresh/Stack 语义）；带 `Buff` 位的档位被全驱散清掉后，仍达标的下一次 `Sync()` 会补回 |
| 呈现 | `EntityVisual` HUD 一行：`护甲 头胸腿 3/4｜套装 名字 3/4·1档` |

**测试与演示**：`Assets/TestAssemblies/`（EditMode 测 `ArmorSlot`/档位表，PlayMode 测真 Entity 全链路：
1/2/3/4 件四态、换套、破套、幂等、`ModifierList.Remove`）；
菜单 `Crown Tide/生成护甲套装演示资产` 一键生成演示套（二件档移速 ×1.15、四件档移速 ×1.05 + 受伤 ×0.8，
穿满 4 件实际移速 ≈ ×1.21）。

---

## 十四、SampleScene 演示场景（玩家 + AI）

场景由 Editor 装配器生成，**不要手改场景 YAML**（31 个带 fileID 交叉引用的文档，手改极易写坏——
2026-10-02 修过一次被写坏的 GUIStyle 块；用 Editor API 装配则由 Unity 自己产出合法 YAML）。

| 菜单（`Crown Tide/`） | 作用 |
|---|---|
| 生成护甲套装演示资产 | 幂等生成演示套（3 件护甲 + 二/四件档 + 玩家/敌人角色配置） |
| 装配 SampleScene 演示实体（玩家 + AI） | 幂等重建 `Player`/`Enemy`（重跑不产生重复物体）、清旧架构组件与 Missing Script、接相机与 AI 感知、保存场景 |
| 校验 SampleScene 演示实体 | 只读自检（组件齐备/件数正确/接线正确/无旧组件残渣）；批处理经 `-executeMethod CreateSceneDemo.Verify` 调用，失败退出码 1 |

场景内容（装配器：`Assets/Editor/CreateSceneDemo.cs`）：

- **Player**：`IsPlayerControlled = true`（Brain 绑 `PlayerInputSource`），穿演示头盔+胸甲 → 二件档移速 ×1.15 开局即生效；DEBUG HUD 开。
- **Enemy**：`IsPlayerControlled = false`（Brain 绑 `AITreeInputSource`），`Target` 指向玩家 → 追着玩家跑；
  只戴头盔（1 件不激活档位）；DEBUG HUD 关（少挡画面）。
- **Main Camera**：挂 `CameraRig` 跟随玩家（F1 切肩 / F2 切第一人称），输入走 `Assets/Input/PlayerControls.inputactions`。
- 操作：WASD 移动、Shift 加速、空格跳；F3~F11 调试键见 `PlayerInputSource` 头注释（F10 可附身切换：玩家↔AI）。

**编辑器装配的两个坑（已在装配器里注释）**：
1. `Entity.Slots` 是 `Awake` 在**运行时**注入的门面，编辑期不跑 Awake → 装配与校验都必须直接
   `GetComponent<CharacterSlotContainer>()`，不能走 `entity.Slots`。
2. 旧架构的类已被删除，它们的组件在场景里是 **Missing Script**，`GetComponent(string)` 看不见；
   只有 `GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go)` 能摘。

---

## 十五、已知设计债与风险（2026-10-02 评估）

> 评估方式：实读代码（不是照设计文档转述），逐条附证据。内容制作的操作手册见 `Docs/ContentAuthoring.md`。

### 15.1 地基缺口（卡住"加内容"，优先补）

| # | 缺口 | 证据 | 影响 |
|---|---|---|---|
| 1 | **伤害管线未闭环** | `EnumStatType.DamageDealt` 只出现在注释（从未被读）；`EntityAttackState` 头注释与 `Tick` 注释均写明"命中判定本轮无"；全项目无 `OverlapSphere`/`OverlapBox`；`EntityBrain.TakeDamage` 唯一调用方是 `ModifierList` 周期跳伤 | 打不动人 → 护甲减伤/挥剑减伤/`DamageTaken` 全在空转；新武器/新敌怪/Boss 都没有意义 |
| 2 | **AI 只会走** | `EnumAIIntent` 只有 `Idle/Chase`；`AITreeInputSource.GatherCommands` 只写 `MoveDirection`，从不写 `AttackQueued` | 敌人不会攻击，Boss 无从谈起 |
| 3 | **技能没有效果载荷** | `SkillSO` 只有 `Name/Kind/Faith/Cooldown`；`EntityBrain.TryConsumeAction` 技能分支只扣闸门 + `Debug.Log` | 技能只是"扣冷却 + 钟摆涨落" |
| 4 | **AI 决策树硬编码** | `AITreeInputSource.BuildDecisionTree()` 里写死两条叶子 | 每加一个敌怪/Boss 类型都要改代码重编译，违背"新内容 = 数据"既定原则（决策机械本身是泛型可复用的，缺的只是"从 SO 读规则装配"） |

**建议顺序**：① 伤害管线 →（② AI 攻击 / 武器伤害字段 / ③ 技能载荷）→ ④ AI 数据驱动 → ⑤ 表现层。
详解见 `Docs/ContentAuthoring.md` §五。

### 15.2 值得保留的优点（改这些地基时别破坏）

- **依赖方向严格**：Data 层零引用 Logic 玩法计算（`ArmorSlot`/`WeaponSlot` 纯持有+纯判定）；`TakeDamage` 唯一伤害入口、`Vitals.ApplyDamage` 唯一扣血写口。
- **模板/实例分层纪律**：SO 是共享模板，运行时状态全在引擎自身容器（`ModifierList`/`ArmorSetBonusList` 都不写 SO）——避开"Play 模式改 SO 持久化写脏"。
- **单 Awake / 单 Update**：装配唯一入口 `Entity.Awake → Brain.Bootstrap`；仿真唯一节拍 `EntityBrain.Update`（切 ServerTick 只搬一处）。
- **时间纪律落地**：`deltaTime` 全链传参、全用累积器，无 `Time.time` 差值。
- **继承仅 1 处**：`abstract EntityState → 7 sealed 状态`，理由（统一句柄 + Enter/Exit + 共享移动助手）充分；组件层零继承。
- **可批处理验证**：`-executeMethod` 自检 + 两套测试在无 GUI 下可跑。

### 15.3 设计债清单（按优先级）

| 级别 | 问题 | 位置 | 处理建议 |
|---|---|---|---|
| 高 | `EnumWeaponType`（注释与数值都表示容量）与 `WeaponSO.Cost` **双份真相** | `EnumWeaponType.cs` / `WeaponSO.cs:24` | `Cost` 作唯一真相，`EnumWeaponType` 降为表现分类键 |
| 高 | ~~`CameraRig.Awake` 对 `InputActionAsset.FindAction` 无空检查~~ **已修（2026-10-02）** | `CameraRig.cs:97-107` | 改为：空则报一次错 + `enabled = false` 早退（配置事故早暴露而非 NRE） |
| 中 | `ModifierList.Apply/Remove` **按 SO 引用归一/摘除** | `ModifierList.cs` | 同一 `ModifierEffect` 既做套装档位又被手动施加 → 破套会连带摘掉外部那条；现靠"注释纪律"约束。彻底解法 = 引入施加来源标识 |
| 中 | `Entity.Slots` 只在 `Awake` 后可用 | `Entity.cs:38` | 所有 Editor 工具都须绕过（本次装配器踩过）；可加 Editor 期取件辅助或把槽位改为 `[SerializeField]` 直读 |
| 中 | 数值聚合**只有乘法链** | `ModifierList.GetStatMultiplier` | "+10 攻击"这类加法型表达不了；需要就拆加法项/乘法项 |
| 低 | 伤害数值无上下界钳制 | `ModifierEffect.cs`（`DamagePerTick`） | 配错即秒杀/秒死；`TickInterval <= 0` 已有防死循环，伤害值建议同规格 |
| 低 | ~~`dotnet build` 97 条 `CS8632`~~ **已处理（2026-10-02）** | `Directory.Build.props` / 各文件 | 结论：**不关 nullable，改为把注解与空判断写对**。`Entity.Runtime` 从 **186 条 → 0 条**（全项目五个程序集 0 警告 0 错误）。要点见下方 §15.5 |
| 低 | HUD 用 `OnGUI` 每帧拼串 | `EntityVisual.cs` | 每帧字符串分配；HUD 可关，正式 UI 时替换 |
| 低 | 集合操作用 `List.Contains` 线性查 | `ArmorSetBonusList.Sync` / `ModifierList` | 条目数少时够用；buff 上百再优化 |

### 15.4 未实装但已留接缝

| 项 | 接缝位置 |
|---|---|
| 护甲值减伤 | `ArmorSO.Value` + `EnumModifierCategory.Armor` 位（`ArmorSet` 占了 `1 << 5`，减伤需另加位） |
| 饰品给 buff / 绑快捷键 | `AccessorySO.GrantedModifier` / `HotkeyAction`（Data 只存引用，Logic/Presentation 消费） |
| 蓄力 / 瞄准射击状态机 | `WeaponComboGraph.HasCharge/ChargeEntry`、`HasShoot/ShootEntry`（翻译逻辑已在 `EntityBrain.TryConsumeAction`） |
| 骑乘 | `EnumEntityTag.Riding` 位预留 |
| 网络同步 | `CommandBuffer` 扁平字段 = 线上输入格式；`IInputSource` 即网络接缝；`deltaTime` 全链传参 |

### 15.5 nullable 注解策略（2026-10-02 定案并落地）

**结论：保留 `<Nullable>enable</Nullable>`，把注解写成事实，读点补空判断。** 判据是**"null 在这个字段是不是合法状态"**：

| 判据 | 写法 | 本项目的例子 |
|---|---|---|
| **null 是合法状态** | `Type? 字段` + 读点判空/`?.` | `ArmorSlot.Head`（未穿戴）、`WeaponSlot.MainHand`（空手）、`SkillSlot.Crown`（未装配）、`ArmorSO.Set`（散件）、`CharacterVitals.Config`（未配置）、`PlayerInputSource.ViewTransform`（相机未注入）、`EntityStateMachine` 层槽（未激活）、`EntityBrain.InputSource`（绑定失败 = 站桩） |
| **非空由序列化/装配保证** | `Type 字段 = null!;`（**断言，不是赋 null，零运行时开销**） | `EntityMotor.Controller`、`PlayerInputSource.Host/PlayerInput`、`EntityVisual.Entity/HudStyle`、`ModifierList.Entity`、`CameraRig` 的四个 InputAction、`EntityAttackState` 快照字段 |
| **装配一次、之后永不为空** | 直接声明**非空不变量** | `EntityBrain.Commands/StateMachine/Modifiers/ArmorSets/Capability` 与七个状态实例；`Entity.Brain/Motor/Vitals/Slots` |
| **纯 C# 数据类** | `= new();`（本来就对） | `CharacterSlotContainer.Armor/Weapons/Skills/Accessories`、`AccessorySlot.Accessories` |

**关键设计（避免满屏 `!`）**：把"装配一次、之后永不为空"的一批按不变量声明后，
真正的"装配门"只剩一处：`EntityBrain.Update` 开头把可空属性收窄成局部并统一早退——
状态转换、JumpPower 读取、伤害入口等调用点都不再需要 `!`（`TryConsumeAction`/`TryConsumeJump`/`TakeDamage` 改为接收收窄后的局部/参数）。

**副作用（正向）**：这一遍真揪出两处"注解说谎" —— `ArmorSlot.Get` 声明非空却 `_ => null`；
`IInputSource.GatherCommands` 声明 `CommandBuffer?` 而两个实现要非空（CS8767，接口与实现签名不一致，已统一）。

**验证**：五个程序集 `dotnet build -t:Rebuild` **全 0 警告 0 错误**（`Entity.Runtime` 从 186 条降为 0）；
EditMode 10/10、PlayMode 13/13 通过——注解是纯元数据，运行行为零变化。

**Unity 侧也已启用（2026-10-02 实测落地）**：`Assets/csc.rsp` 内容

```text
-nullable:enable
-langversion:9.0
```

两个坑（都实测过，避免走弯路）：

1. **`ProjectSettings` 的 `additionalCompilerArguments` 对 Editor 编译无效**——它属 `PlayerSettings`（构建期），
   实测加了 `Standalone:` 键或空键 `''` 都不会出现在 Unity 生成的编译响应文件 `Library/Bee/**/*.rsp` 里。
   真正生效的是 `Assets/csc.rsp`（Unity 会把它整份并进每个程序集的 rsp）。
2. **必须同时给 `-langversion:9.0`**：Unity 默认语言版本会把 `-nullable:` 忽略掉（表现为 `?` 注解仍报 CS8632）。
   只写 `-nullable:enable` 时 `rsp` 里参数确实在，但 CS8632 照旧 73 条；补上语言版本后归零。

**验证**：Unity 编译 **0 条 CS 诊断**（`Logs/unity-*.log`），EditMode 10/10、PlayMode 13/13 通过。

**开着 nullable 后 Unity 额外捞出来的真问题**（dotnet 侧看不到，因为 Unity 编译器此前没有 nullable 上下文；
这印证了"不要关"的判断）：13 个 SerializeField/资产引用字段被错标为非空 ——
`AccessorySO.GrantedModifier/HotkeyAction`、`WeaponSO.ComboGraphSingle/Dual`、`Entity.Config`、
`AITreeInputSource.Target`、`CharacterSlotContainer.UnarmedComboGraph`、`ArmorSetSO.Bonuses`、
`CameraRig.FollowEntity`、`PlayerInputSource` 三个调试槽。它们的读点本来就有空判断或"空 = 未配置"语义，
已全部改为 `?`（`CharacterVitals.Initialize` 参数与 `ApplyDebugModifier` 参数同步放宽为可空）。

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
