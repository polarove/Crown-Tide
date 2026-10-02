# 单位与投射物架构选型指南

> 适用范围：Crown Tide 实体架构（BlackBoard / Decision / StateMachine / Entity 分层）。
> 对应代码：`Assets/Scripts/BlackBoard`（黑板、TagSet）、`Assets/Scripts/Decision`（决策树）、
> `Assets/Scripts/StateMachine`（状态机）、`Assets/Scripts/Entity`（实体层：EntityController
> 运动能力 + NpcController 统一角色层；玩家与敌人是它的两个输入源叶子，七状态在 NPC/States/ 共享；
> 状态效果三件在 StatusEffect/、命名标签 EntityTag/TagSet——三种机制见 §二末节）。
> 本文档回答：每类"会动的东西"该用什么架构，以及为什么。
> 战斗系统（指令层/装备/连招/选招/阶段）的落地排期见 `Docs/CombatSystemPlan.md`。

## 一、总原则：三个问题决定架构

对任何一个单位，依次问：

| 问题 | 回答"是"需要 | 对应部件 |
|---|---|---|
| 它需要自己决定"做什么"吗？（感知目标、选意图） | 决策层 | 决策树 / 行为树 / Utility |
| 它的行为有"阶段过程"吗？（前后摇、蓄力、变轨；需要 Enter/Exit 挂动画事件） | 状态机 | StateMachine/State |
| 它的各部件间要共享运行数据吗？ | 黑板 | BlackBoard/Blackboard |

三个部件互相独立，按需组合。**不是所有会动的东西都要三件套**——架构跟着需求走，
"用上了框架"本身不是目标，"删掉一个部件也能工作"才是框架分层正确的标志。

## 二、角色层（NpcController）：状态机 + 黑板 + 可替换的输入源

玩家角色与 NPC 在 NpcController 上同构：同一套分层状态机、同一块黑板（NpcBlackboard）、
同一条运动管线（Update 唯一声明在 NpcController）。**输入源是唯一差异**——
玩家叶子（PlayerController）把输入设备翻译成指令写黑板，NPC 叶子（如 EnemyController）
跑决策树把意图翻译成指令写同一块黑板；状态机只消费指令，不关心指令来自谁：

```
玩家角色：输入设备 ────────────┐
                              ├→ NpcBlackboard 指令区 → 状态机（七状态共享） → 运动能力
敌对角色：感知 → 决策树 ───────┘
```

黑板指令区分两类：**电平型**（MoveDirection/SprintActive——控制器帧首重置、输入源每帧重写，
"输入源沉默 = 站桩"是框架默认语义，附身切换帧也安全）与**边沿型**
（JumpQueued/QueuedSkillSlot——置位后由管线消费清空，无缓冲，不清在帧首否则丢输入）。

### 输入源与附身（"玩家操作 NPC"）

附身 = 给实体换输入源叶子。演示级操作序列（三条前置都满足才不穿帮）：
①目标实体需要有预配置的 PlayerInput（actions 资产 + Default Map=Player + Send Messages）——
运行时裸 AddComponent 的 PlayerInput 收不到消息、FindAction 为空，需预制体或工厂代码赋好；
②CameraFollow.followTarget 同步指向新身体，否则镜头留在旧身体上；
③先 Disable 旧叶子（如 EnemyController）再挂新叶子——同帧两者都活跃会双重 Move / 双重重力。
角色配置（walkSpeed/jumpHeight 等）在实体组件上，附身后照用；状态机从 Idle 重建
（接管瞬间站一帧，无手感影响）。此为演示级临时路径；正式方案是抽 IInputSource 组件
（附身 = 启用切换，控制器与状态机不动），等附身玩法立项再做——当前"叶子覆写
UpdateCommands"的形态不排斥该演进。

### 分层状态机（同层互斥、异层叠加）

一个实体可同时处于多个正交层的状态——奔跑时跳跃 = Locomotion 层的 Sprint + Aerial 层的
Air 并存，空中保持跑速、落地无缝续跑（落地转回 Grounded 不经过 Locomotion，不闪断）。

| 层 | 职责 | 玩家状态示例 |
|---|---|---|
| Locomotion | 水平移动（空中照常执行） | Idle / Walk / Sprint（玩家与 NPC 共用；敌人追击=决策写方向走 Walk） |
| Aerial | 竖直姿态（起跳/离地/落地时机） | Grounded / Air |
| Action | 主动动作（战斗系统 M1 起） | 攻击 / 闪避 |
| CrowdControl | 失控，**压制其余全部层** | Stun（眩晕）/ 击倒 |

- **同层互斥**：`ChangeState` 只替换目标状态所属层的活跃状态，其他层不受影响。
- **压制 = 冻结而非清除**：眩晕期间其余层的状态保留但不执行（Locomotion 停在 Sprint），
  解除后自动恢复（输入仍在就继续跑）；物理不受影响（重力/地面检测在控制器管线）。
  冲量类效果（跳跃/击退/闪避）在控制器侧还要过眩晕门禁，否则会绕过压制。
- 压制规则集中在 `StateLayerRules`；Tick 惰性求值压制，解除当帧即恢复。

### 霸体等"修饰"：状态声明能力，入口仲裁

霸体/无敌帧/隐身这类**修饰**不是层也不是状态（没有行为、不是正交维度），
统一模式：**状态声明能力，外部系统在施加入口仲裁**。

- `State.GrantsSuperArmor`（虚属性，默认 false）：重击/技能演出等状态覆写为 true，
  随状态生命周期自动生效/失效（不会忘关）。`StateMachine.HasSuperArmor()` 扫活跃层；
  `EntityController.HasSuperArmor` 是给命中系统用的多态入口（玩家/NPC 控制器已覆写）。
- 仲裁时机：战斗命中入口——`if (!target.HasSuperArmor) 施加CC`，伤害照算。
- 语义细则：**霸体是免疫不是解控**（已生效的 CC 不清除，解除 CC 是 `ClearState` 的净化操作）；
  通常只挡打断不挡伤害。
- 两个来源合成：动作型（状态虚属性）+ 装备/Buff 型（黑板标志，装备系统实现时与上面做或运算）。
- Boss 削韧（Poise）= 霸体的资源化：黑板挂 Poise/MaxPoise，命中先扣韧性、韧尽才施加 CC；
  数值挂装备/技能配置（装什么会什么），Boss 阶层（PhaseDef）可覆盖 MaxPoise。
- 同族扩展：无敌帧 `GrantsInvincibility`（命中入口跳过伤害与 CC）同模式加一行虚属性即可。

### 命名状态的三种机制：状态机 / 效果容器 / 标签

"角色现在处于什么状态"有三类来源，边界准则：**有行为（阶段过程、意图驱动转换）才是状态；
纯修饰（数值/时长/叠加）归效果容器；纯命名（零效果）归标签**。判例：闪避有阶段过程
（前闪/无敌帧/收势）→ Action 层状态，不是"闪避 Buff"；"攻击时赋予自身的挥剑标记"无行为
→ 标签，效果由读方解释（见下）。Buff/Debuff 不是两种机制，是同一效果容器的正负两半。

| 机制 | 载体 | 语义 | 典型例 | 生命周期 |
|---|---|---|---|---|
| 状态机 | StateLayer + State | 有行为：每帧 Tick 做事、意图驱动转换、层内互斥 | 走/跑/跳/攻击/闪避/眩晕态 | Enter/Exit 随转换 |
| 效果容器 | StatusEffectContainer（黑板持有） | 纯修饰：控制/数值/周期三成分可并存、自由叠加、带时长 | 急速/冰冻/创伤/中毒 | Apply → Tick → 到期/驱散 |
| 标签 | TagSet（黑板持有） | 纯命名：`Has(tag)` 真/假即全部语义，谁读谁解释 | 挥剑中/失控中/骑乘中 | 状态直写或容器投影 |

三机制对业务代码的唯一汇合点是**统一查询门**：消费方不问"你在什么状态"，问投影后的语义
——`Tags.Has((ulong)EntityTag.Swinging)` 与 `GetStatMultiplier(StatType.X)`。
"冰冻"因此可以是容器条目（数值乘数 + 控制投影 + Controlled 标签）的组合，而移动/攻击/受伤
的消费代码一行不改。

**效果容器**（`StatusEffectData` SO + `StatusEffectContainer`，`Entity/StatusEffect/`）要点：

- 一条效果 = **三成分**：控制（hasControl → 投影 CC 层）、数值（statModifiers 乘法链）、
  周期（tickInterval 跳伤走 TakeDamage）；可只开其一——急速=纯数值、眩晕=纯控制、创伤=纯周期。
- **失控多挂载单表达**：多条失控条目活跃时只呈现 controlPriority 最高者的映射状态
  （`ResolveControlState`，默认眩晕；冰冻等由角色覆写映射）；更高者接管、逐个解除自动降级，
  同强度先挂者保持。
- **霸体仲裁在施加时刻定死**：`controlActive = hasControl && !HasSuperArmor`——免疫≠解控：
  条目照挂、数值/周期照跑，失控成分被拦下（判"这发钉住没有"查 `HasControlActive`）。
- 失控起手会**打断主动动作**：容器 ClearState(Action)，攻击 Exit 顺带摘挥剑标记——
  攻击不会在眩晕后"续播"。
- 驱散按类别位与过滤：`Dispel(Debuff)` 净化、`Dispel(Poison)` 驱毒、`Dispel(All)` 全清；
  叠加策略 Refresh / Stack（满层回落为刷新时长）/ Ignore；Stack 条目层数 = 数值乘数指数
  （1.5×3 层 ≈ 3.4）。

**标签的读写解耦**（标记+解释器模式）：写方只命名——攻击状态 Enter/Exit 挂摘 Swinging；
读方赋义——武器 SO 的 `swingDamageTakenMultiplier` 把"挥剑中"解释成受伤乘数，默认 1 =
纯标记无效果，武器重写即获得效果。将来吸血/破甲/处决条件走同一模式，攻击状态零改动。
标签写入者也分域：状态直写（Add/Remove）与容器投影（SyncOwned 只动自己域的位）互不踩脚。

**数值乘数消费读点**（StatType；新乘数先登记读点再接效果，别处不得绕过读点直读容器）：

| 乘数 | 读点 |
|---|---|
| MoveSpeed | NpcStateBase.ApplyLocomotion（全姿态生效） |
| JumpPower | NpcController.TryConsumeJump（×2 乘数 ≈ 跳 1.41 倍高） |
| AttackSpeed | NpcAttackState 攻击时长（÷ 语义，乘数钳下限 0.05 防除零） |
| DamageTaken | NpcController.TakeDamage（统一伤害入口） |
| DamageDealt | M3 命中入口（已挂账未读） |

伤害与生命：**唯一入口 `TakeDamage(amount)`**（周期跳伤/将来命中/环境都走这里），
入口修正 = DamageTaken 乘数 × 挥剑减伤，扣血钳 [0, MaxHealth]；归零走占位 Die
（清指令 + 停管线，正式死亡演出/复活后置）。MaxHealth 是装备组合活属性，不进黑板。

**术语映射**（讨论口径）："水平姿态"= Locomotion 层、"垂直姿态"= Aerial 层；
"影响行动的 buff/debuff"= 控制投影与数值乘数；"不影响行动的"= 周期成分与纯标记；
骑乘 = Riding 标签（+ 将来需要坐骑行为集时按 Boss Phase 同构处理：行为集切换）。

## 三、NPC（决策树输入源）：决策树 + 状态机 + 黑板（项目定案，统一使用）

决策层替代玩家大脑，但角色层与玩家完全同构（§二）。本项目定案：**所有 NPC 统一
决策树 + 状态机 + 黑板**——简单 NPC 就是"小决策树"（两三条规则），不按复杂度分档，
结构一致便于扩展与阅读。

**决策层与状态机的分工**（职责不重叠，都不可省）：

- **决策树回答"做什么"**：无状态、从根整体评估、低频跑（`decisionInterval` 默认 0.2s，
  省性能 + 条件抖动不会让行为闪烁）；产出**意图**（如 EnemyBehavior.Chase，叶子私有字段，不进黑板）。
- **意图翻译回答"把意图变成指令"**：输入源每帧执行（UpdateCommands）——低频决策的产物
  被连续翻译成黑板指令（追击 → MoveDirection 朝目标）。玩家的"手指"与 AI 的"翻译"在这里同构。
- **状态机回答"怎么做"**：有状态记忆、每帧跑、承载阶段过程（攻击前摇→命中→后摇等），
  转换条件读指令（HasMoveInput / SprintActive / QueuedSkillSlot）。
  决策树没有记忆表达不了过程，状态机散落的转换条件表达不好"为什么打"。

防抖设计：`Decision.Decide` 返回 null = 本次无结论，维持原意图；迟滞条件
（如"目标丢失超过 2s 才放弃追击"）写在决策树的条件里。

**召唤物 = 玩家间接控制的 NPC**：玩家指令作为决策树里**最高优先级的一条分支**进入
（与感知输入同级），而不是绕过决策层直接改状态——否则玩家指令和 NPC 自主行为会打架。

数据流：

```
感知（每帧/事件）→ 黑板 → 决策树（低频）产出意图 → 意图翻译（每帧，在输入源内部）写指令区
→ 黑板 → 状态机（每帧）→ 运动能力
```

## 四、招式执行：指令层 + 连招数据表 + 选招（战斗 AI 定案）

实体设计定案（§二已在代码落地）：**玩家操作的角色与可攻击的敌对角色继承同一角色类**
（NpcController，输入源叶子是唯一差异）——武器/饰品/技能槽位一致或可拓展，NPC 强度取决于
生成时装配的装备。"角色能做什么"（装备/技能）与"谁在操作"已解耦，AI 只是另一个输入源：

```
玩家角色：输入设备（鼠标/手柄）──────────┐
                                          ├→ 指令（用技能槽 i / 移动 / 格挡 / 闪避）→ 角色状态机 → 装备/技能执行
敌对角色：感知 → 决策树 / Utility 选招 ───┘
```

**连招 = 数据表**（不是代码，更不是状态堆叠）：连招表（ScriptableObject）描述段序列，
每段 = 技能 + 进入条件（上段命中/被格挡/无条件）+ 取消窗口时长。通用 `AttackState`
读表执行"前摇 → 命中 → 后摇 → 窗口"，黑板记录 `ComboIndex / LastHitResult /
ComboWindowEndTime`。玩家在窗口内按攻击键 = 续段；NPC 决策树在窗口内评估 = 续段——
与玩家操作完全同构，这就是"决策树代替玩家思考"能贯彻到底的原因。

**选招分层**（同一框架加深，不换架构）：

| 层级 | 选招方式 | 深度 |
|---|---|---|
| 杂兵（池化批量生产） | 小决策树（2~4 条规则） | 单段/短连招，即当前 Enemy 模式 |
| 精英 | Utility 加权选招：对装备技能库逐个打分（距离适配/冷却/消耗/目标状态/权重），最高分过阈值胜出 | 连招表 2~4 段 |
| Boss | Utility + 阶段层：血量阈值切 Phase，Phase = 行为集（换决策树/连招表/参数覆盖） | 演出节点（转阶段/处决）做成状态 |

**何时才需要 GOAP / 行为树**：出现"长期自主目标"玩法（NPC 自主绕后包抄、权衡先喝药
还是先追击、大世界任务链）时，把**该类 NPC 的决策层**换成 planner，状态机/黑板/连招表
照用。战斗内的"智能感"由 Utility 选招 + 连招派生提供，不需要规划器——动作游戏 Boss
需要导演感（可控、可调、可读），规划器产出的序列恰恰不可预测、难调难度。

## 五、投射物（子弹、魔法球、剑气）：数据驱动 + 生命周期 + 对象池

**默认不使用状态机，也不使用黑板，更不需要决策层。**

三个判断问题全部答"否"：不感知、不选意图（追踪弹的"追踪"是转向行为不是决策）、
大多数也没有阶段过程。它们之间的差异全是**参数**：方向/初速/重力/寿命/伤害/穿透数/命中效果。
**子弹、魔法球、剑气应该是同一个类的不同配置，不是不同的状态机。**

组成建议：

| 部件 | 职责 |
|---|---|
| `ProjectileConfig`（ScriptableObject） | 数值与表现配置，策划可调、可热更 |
| `ProjectileController`（精简 MonoBehaviour） | 生成 → 飞行 → 命中/超时 → 回收 |
| 对象池 | 投射物是大批量消耗品：必须池化、零 GC、禁用期不 Update |
| 命中与伤害解耦 | 碰撞检测只产出命中事件；伤害计算/特效/音效由监听方处理 |

投射物**不继承 EntityController**：没有 CharacterController 运动需求（位移用 Rigidbody
或手动积分），且是批量实例，不该背上实体层的每帧管线成本。

**例外：阶段型投射物**（变轨导弹、蓄力箭、引导光束）有真实的过程阶段，可以复用
StateMachine/State（如 `Spawn → Lock → Homing → Detonate`）；但只有两三个阶段时，
一个 enum + 计时器就够了，**不必为了架构统一而强上框架**。

纯表现（弹道拖尾、命中爆闪）归 VFX/粒子系统，不进任何逻辑架构。

## 六、总表

| 单位类型 | 决策层 | 状态机 | 黑板 | 关键手段 |
|---|---|---|---|---|
| 玩家角色 | ✗（玩家输入） | ✓ | ✓ | 输入源叶子写指令区；技能走指令层 |
| NPC 杂兵/精英（统一） | ✓ 决策树 → Utility 选招 | ✓ | ✓ | 意图翻译写指令区；连招=数据表 |
| Boss | ✓ Utility + 阶段层 | ✓ | ✓ | 血量切 Phase（行为集） |
| 召唤物 | ✓（含玩家指令分支） | ✓ | ✓ | 同 NPC，指令最高优先级 |
| 直线投射物 | ✗ | ✗ | ✗ | 配置驱动 + 对象池 |
| 阶段型投射物 | ✗ | 可选（复用 Core 或 enum+计时器） | ✗ | 导弹/蓄力/引导 |
| 纯 VFX | ✗ | ✗ | ✗ | 粒子/动画 |

## 七、本项目扩展路径

1. **新 NPC / 新输入源**：继承 `NpcController`，照 `Assets/Scripts/Entity/NPC/Enemy/` 模板写叶子：
   override InitEntity（先 base 建状态机，再建决策树）+ override UpdateCommands（跑决策 +
   把意图翻译成指令）；行为枚举与意图字段放叶子私有（不进黑板）。感知字段（Target 等）已在
   NpcBlackboard。不要声明 Update（管线唯一在 NpcController，误写会收到编译器隐藏警告）。
2. **决策树组装**：`DecisionSelector`（优先级选择，恒真兜底收尾）为主力，`DecisionBranch`
   做二叉细分；全部纯 C#、构造一次、零每帧分配。
3. **加投射物**：新建 `Assets/Scripts/Projectile/`（Config + Controller + 对象池），不继承 EntityController。
4. **加状态效果/标签**：Create → Crown Tide → 状态效果（StatusEffectData），按需勾三成分；
   新标签先在 `EntityTag` 的 64 位分配登记表加行；新乘数先在 `StatType` 登记读点（见 §二末节表格）。
5. **战斗系统**（指令层、装备驱动、连招数据表、Utility 选招、Boss 阶段、杂兵池化）：
   按 `Docs/CombatSystemPlan.md` 里程碑顺序推进。
