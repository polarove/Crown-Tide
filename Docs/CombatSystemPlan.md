# 战斗系统实现计划：指令层 + 装备驱动 + 连招数据表 + Utility 选招 + Boss 阶段

> 定案出处：`EntityArchitecture.md` 第四节。本计划把定案铺到文件级设计，按里程碑推进，
> 每个里程碑结束时游戏可玩、可回归。
> 总原则：玩家现有管线（移动/跳跃/加速，黑板直写模式）在指令层成熟前**不动**；
> 指令层先只覆盖"技能施放"，避免过早抽象。

## 现状（M0 起点）

| 已有 | 位置 |
|---|---|
| 状态机 + 黑板框架 | `Assets/Scripts/StateMachine/`、`Assets/Scripts/BlackBoard/` |
| 决策树框架（Selector/Leaf/Branch；decisionInterval 定时评估收在 EnemyController） | `Assets/Scripts/Decision/`、`Assets/Scripts/Entity/NPC/Enemy/EnemyController.cs` |
| 统一角色层（NpcController 管线 + 七状态；玩家=输入设备输入源，NPC=决策树输入源） | `Assets/Scripts/Entity/EntityController.cs`、`Assets/Scripts/Entity/NPC/` |
| 玩家与 NPC 共用 Idle/Walk/Sprint/Grounded/Air/Attack/Stun | `Assets/Scripts/Entity/NPC/States/` |
| 效果容器 + 标签 + HP/伤害入口（M2.5，见下节） | `Assets/Scripts/Entity/StatusEffect/`、`Assets/Scripts/BlackBoard/TagSet.cs`、`Assets/Scripts/Entity/EntityTag.cs` |

## M1：技能指令最小闭环（玩家侧）

目标：玩家按攻击键 → 进入攻击状态 → 播完回 Idle。动画未接入前用计时器模拟
前摇/命中/后摇三段。

- 输入：`PlayerControls.inputactions` 加 `Attack` 动作（鼠标左键 + 手柄 West 键），
  走消息回调 `OnAttack`（与 OnJump 同模式）
- 指令落黑板：`PlayerBlackboard` 加 `public int QueuedSkillSlot = -1;`
  （与 JumpQueued 同模式：输入写、状态机消费后清空；-1 = 无排队技能。
  InputSource 抽象已随统一角色层落地一半：输入源 = NpcController 叶子覆写 UpdateCommands；
  组件化的 IInputSource 留给附身玩法立项时再抽）
- 新状态 `PlayerAttackState`（挂 **Action 层**，与 Locomotion 叠加：攻击是否锁移动由该状态
  自己读配置决定）：前摇（锁移动）→ 命中帧（占位，暂无判定）→ 后摇 → 回 Idle
- `PlayerController`：`TryConsumeSkill()`（与 TryConsumeJump 同模式，攻击期间不重复排队）

验证：按左键进攻击状态（调试面板状态名可见）、期间不移动、结束回 Idle；
连按不叠加、不卡死。

## M2：装备与角色数据（玩家 NPC 共用）——前半已落地

已落地：攻击节奏从 PlayerController 抽成"角色 × 武器"数据组合，多角色=多份资产。

- `Entity/Combat/CharacterDefinition.cs`（SO）：角色基础攻击节奏（前摇/命中/后摇），
  后续角色差异（基础移速/体重/跳跃力）也挂这
- `Entity/Combat/WeaponDefinition.cs`（SO）：攻速修正（÷ 语义：1.2=快 20%），
  M3 起伤害与连招表引用挂进来
- `Entity/Combat/CharacterEquipment.cs`：装备组件，实体必挂（EntityController
  RequireComponent）；字段空时回退内置默认，渐进接入
- PlayerAttackState 读 `Controller.Equipment.AttackWindup` 等组合查询

剩余（随 M3 一起）：`SkillDefinition`（判定参数/消耗/冷却/使用条件/动画引用）、
饰品槽与技能槽数组挂 CharacterEquipment。

验证：建两份角色 SO（快/慢）+ 两份武器 SO（攻速 0.8/1.2），
四种组合的攻击节奏 = 基础 ÷ 攻速，Play 模式换引用即时生效。

## M2.5：效果容器（Buff / Debuff 统一）——已落地

增益与减益是**同一套系统的正负两半**（急速=攻速×1.3，虚弱=伤害×0.7），不建第二套
BuffSystem：机制同构（时长/来源/叠加/优先级）、正负要在同一处聚合抵消（虚弱-30% 与
狂暴+30% 同挂）、驱散按类别位过滤（清毒=`Dispel(Poison)`、净化=`Dispel(Debuff)`、
全清=`Dispel(All)`、腐蚀偷增益=M3 命中系统读 `IsHolding` 再反向 Apply）。
增益/减益的真正区别只在 UI 图标颜色与净效果正负——表现层的事。

**条目 = 三种成分**（可并存于同一个效果，冰冻可既控人又掉冰伤又降防）：

| 成分 | 例子 | 归宿（已落地） |
|---|---|---|
| 控制 | 眩晕/冰冻/击倒 | 容器投影 CC 层（霸体在 Apply 入口仲裁，施加时刻定死 `controlActive`） |
| 数值 | 虚弱/急速/减速/跳跃强化 | `GetStatMultiplier(StatType)` 乘法链，消费读点见下 |
| 周期 | 中毒/燃烧 | 容器统一 tick 跳伤，走 `TakeDamage` 统一入口 |

**多挂载，单表达**：实体可同时挂任意多条目（含多个失控型），各自独立倒计时；
失控型的行为呈现取 controlPriority 最高者投影 CC 层（映射走 `ResolveControlState`，
默认眩晕，冰冻/石化由角色覆写），更高者接管、逐个解除自动降级、全部消失由 StunState
轮询 `HasControlActive` 清层。同优先级先挂保持防抖。数据层多挂、行为层单表达——
两个失控并跑会打架（眩晕要站桩、混乱要乱走），身体只能听一个。

**落地清单**：

| 部件 | 位置 |
|---|---|
| StatType / StatusEffectData（SO：时长、叠加策略 Refresh/Stack/Ignore、三成分数值、Category 位、优先级）/ StatusEffectContainer | `Entity/StatusEffect/` |
| TagSet（框架位运算）+ EntityTag（64 位分配登记表） | `BlackBoard/TagSet.cs`、`Entity/EntityTag.cs` |
| 容器与标签挂黑板；HP：CurrentHealth 黑板 + MaxHealth 装备活属性 + `TakeDamage`/占位 `Die`（NpcController） | `Entity/NPC/` |
| 挥剑标记解释器：攻击状态挂摘 Swinging，武器 `swingDamageTakenMultiplier` 读它减免 | `NpcAttackState` + `WeaponDefinition` |
| 消费读点 | MoveSpeed→ApplyLocomotion 乘；JumpPower→TryConsumeJump 乘；AttackSpeed→AttackState 时长除（钳 0.05 防除零）；DamageTaken→TakeDamage 乘；DamageDealt→M3 命中（挂账） |
| 按次参数覆盖 | `Apply(target, effect, durationOverride, damagePerTickOverride)`：覆盖值定格在条目上不改资产（SO=全场共享模板，条目=本次施加的实例——改资产会全场生效且 Editor 下 Play 修改持久化写脏）。同一份流血资产，NPC1 施加 10s、NPC2 施加 3s；重复施加时参数以本次为准更新；乘数覆盖等出现需求再加 |
| 调试链路 | PlayerController 槽拖演示 SO：F3 眩晕 / F4 急速 / F5 创伤 / F6 全驱散 / F7 眩晕场景其他角色（敌人侧回归） |

**留待**：霸体拦截路径暂无消费者（还没有 GrantsSuperArmor=true 的状态），逻辑已就位，
M3 技能演出状态接入时验证；死亡为占位（冻结实体），演出/复活后置。

**特殊形态备忘**：护盾=池不是乘数（伤害入口先扣盾）；隐身持续型=容器条目+grantedTag、
敌方感知查标签，瞬发型=状态声明（GrantsInvincibility 同模式）；强制行为型（血怒）=NPC 走
决策树最高优先级分支、玩家走 CC 层（混乱同款双侧投影）。

## M3：连招数据表 + 通用攻击状态

目标：连招=数据。玩家连按攻击键打出多段，段间有取消窗口，打空收招。

- `Assets/Scripts/Combat/ComboData.cs`（SO）：`List<ComboStep>`；ComboStep =
  技能引用（SkillDefinition）+ 进入条件（enum：无条件 / 上段命中 / 上段被格挡）+
  取消窗口时长
- 黑板加连招上下文（放 EntityBlackboard 或 CombatBlackboard 中间层，实现时定）：
  `ComboIndex`、`LastHitResult`（enum Hit/Miss/Blocked/None）、`ComboWindowEndTime`
- `PlayerAttackState` 泛化为 `AttackState`：读连招表执行当前段；
  窗口期内 QueuedSkillSlot 有值（玩家按键或 AI 决策）且下段进入条件满足 → 续段，
  否则收招回 Idle
- 命中反馈：命中判定（先占位球形 Overlap）写 `LastHitResult`——续段条件的数据源
- 命中仲裁（霸体）：施加 CC 前查 `target.HasSuperArmor`（状态声明式，框架已就位：
  `State.GrantsSuperArmor` + `StateMachine.HasSuperArmor()`）；伤害照算，只挡打断。
  削韧（Poise）数据位后续挂装备/技能配置，Boss 阶段（PhaseDef）可覆盖 MaxPoise

验证：3 段连招表，三种路径正确——全按出全连、断按收招、
"上段命中才续"的段在打空时正确断连。

## M4：AI 选招接入（Utility）

目标：敌人从"追到就撞"变成"进距离起手连招，冷却期拉开/等待"。

- ~~指令汇流~~ **已随统一角色层重构提前落地**：玩家与 NPC 共用 NpcBlackboard 指令区
  （QueuedSkillSlot / MoveDirection / SprintActive），AttackState 不关心指令来自玩家还是 AI
- Utility 选招：`Assets/Scripts/Decision/UtilitySelector.cs`（与 Decision 同基类）：
  候选 = 装备技能库（**装什么会什么**），每个候选带打分函数
  （距离适配/冷却就绪/消耗足够/目标状态加成/权重随机），最高分且过阈值胜出，
  否则返回 null 维持现状（防抖语义与决策树一致）
- 敌人决策树升级：Selector（玩家指令分支（召唤物预留）→ Utility 选招 → 追击 → 兜底待机）
- 感知最小化：黑板加 `TargetDistance` / `TargetInAttackRange`（每帧算，决策读）

验证：敌人进攻击距离起手连招、打空收招、冷却中不再起手；
改敌人技能槽 SO（换装备）行为随之改变。

## M5：Boss 阶段层 + 杂兵池化

目标：同一框架上叠阶段，不换架构；杂兵可批量生产回收。

- `Assets/Scripts/Combat/BossPhaseController.cs`：血量阈值切 Phase；
  Phase = 行为集（决策树引用 + 连招表引用 + 参数覆盖如 walkSpeed/aggression）。
  实现倾向轻量（阈值事件 + 换引用）；阶段间需要 Enter/Exit 演出时再考虑复用 StateMachine
- 演出节点（转阶段演出、处决 QTE）= 普通状态，进 NPC 状态机
- 杂兵池：`Assets/Scripts/Pool/SimplePool.cs`（通用对象池，投射物将来同用）：
  预热、取出/归还、禁用期零 Update；归还时重置黑板与状态机到 Idle（防状态残留），
  重置面覆盖效果容器（清条目/驱散）、标签（清位）、CurrentHealth（回 MaxHealth）与
  死亡标志（NpcController 复活入口届时一并给）

验证：Boss 100%/60%/30% 三阶段换行为集（换连招表肉眼可辨）；
杂兵批量生成/回收无 GC、无状态残留。

## 明确不做（防过度设计）

- **GOAP / 行为树**：直到出现"长期自主目标"玩法（自主包抄、任务链）才评估，
  且只换决策层，状态机/黑板/连招表照用
- **移动指令统一**：~~玩家移动管线保持黑板直写~~——已随 Player/NPC 统一角色层重构落地
  （移动也是指令，玩家与 AI 写同一指令区）；组件化 IInputSource 仍推迟到附身玩法立项
- **连招预规划承诺**（"记住 3 步后接处决"）：用派生表（进入条件分支）表达，
  不做动态规划
- **动画系统选型**：M1~M3 用计时器占位，接动画时只动 AttackState 的"段执行"内部，
  架构不变
