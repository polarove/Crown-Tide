# 战斗系统实现计划：指令层 + 装备驱动 + 连招数据表 + Utility 选招 + Boss 阶段

> 定案出处：`EntityArchitecture.md` 第四节。本计划把定案铺到文件级设计，按里程碑推进，
> 每个里程碑结束时游戏可玩、可回归。
> 总原则：玩家现有管线（移动/跳跃/加速，黑板直写模式）在指令层成熟前**不动**；
> 指令层先只覆盖"技能施放"，避免过早抽象。

## 现状（M0 起点）

| 已有 | 位置 |
|---|---|
| 状态机 + 黑板框架 | `Assets/Scripts/Core/StateMachine.cs` 等 |
| 决策树框架（Selector/Leaf/Branch + decisionInterval 定时评估） | `Assets/Scripts/Core/Decision*.cs`、`Assets/Scripts/Npc/NpcController.cs` |
| 实体运动能力（CharacterController 每帧管线） | `Assets/Scripts/Entity/EntityController.cs` |
| 玩家 Idle/Walk/Sprint/Air；敌人 Idle/Chase（target 指派） | `Assets/Scripts/Player/`、`Assets/Scripts/Enemy/` |

## M1：技能指令最小闭环（玩家侧）

目标：玩家按攻击键 → 进入攻击状态 → 播完回 Idle。动画未接入前用计时器模拟
前摇/命中/后摇三段。

- 输入：`PlayerControls.inputactions` 加 `Attack` 动作（鼠标左键 + 手柄 West 键），
  走消息回调 `OnAttack`（与 OnJump 同模式）
- 指令落黑板：`PlayerBlackboard` 加 `public int QueuedSkillSlot = -1;`
  （与 JumpQueued 同模式：输入写、状态机消费后清空；-1 = 无排队技能。
  正式的 InputSource 抽象推迟到 M4 玩家/AI 指令汇流时再抽）
- 新状态 `PlayerAttackState`：前摇（锁移动）→ 命中帧（占位，暂无判定）→ 后摇 →
  回 Idle；转换优先级高于移动状态
- `PlayerController`：`TryConsumeSkill()`（与 TryConsumeJump 同模式，攻击期间不重复排队）

验证：按左键进攻击状态（调试面板状态名可见）、期间不移动、结束回 Idle；
连按不叠加、不卡死。

## M2：装备与技能数据（玩家 NPC 共用）

目标：技能从硬编码参数变成 ScriptableObject；角色挂装备组件。

- `Assets/Scripts/Combat/SkillDefinition.cs`（SO）：前摇/命中帧/后摇时长、
  判定参数（范围/角度/位移）、消耗、冷却、使用条件（最短距离等）、动画引用（可空）
- `Assets/Scripts/Combat/CharacterEquipment.cs`（MonoBehaviour，玩家与 NPC 同挂）：
  武器槽（决定普攻连招表引用，M3 用）、饰品槽数组、技能槽数组（长度可拓展）
- `PlayerAttackState` 改读 SkillDefinition 驱动各段时长

验证：改 SO 数值（前后摇）Play 模式即时生效。

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

验证：3 段连招表，三种路径正确——全按出全连、断按收招、
"上段命中才续"的段在打空时正确断连。

## M4：AI 选招接入（指令汇流 + Utility）

目标：敌人从"追到就撞"变成"进距离起手连招，冷却期拉开/等待"。

- 指令汇流：`NpcBlackboard` 加 `QueuedSkillSlot`（与玩家黑板同名字段）——
  AttackState 不关心指令来自玩家还是 AI，指令层在此正式成型
- Utility 选招：`Assets/Scripts/Core/UtilitySelector.cs`（与 Decision 同基类）：
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
- 杂兵池：`Assets/Scripts/Core/SimplePool.cs`（通用对象池，投射物将来同用）：
  预热、取出/归还、禁用期零 Update；归还时重置黑板与状态机到 Idle（防状态残留）

验证：Boss 100%/60%/30% 三阶段换行为集（换连招表肉眼可辨）；
杂兵批量生成/回收无 GC、无状态残留。

## 明确不做（防过度设计）

- **GOAP / 行为树**：直到出现"长期自主目标"玩法（自主包抄、任务链）才评估，
  且只换决策层，状态机/黑板/连招表照用
- **移动指令统一**：玩家移动管线保持黑板直写，只有技能走指令层
- **连招预规划承诺**（"记住 3 步后接处决"）：用派生表（进入条件分支）表达，
  不做动态规划
- **动画系统选型**：M1~M3 用计时器占位，接动画时只动 AttackState 的"段执行"内部，
  架构不变
