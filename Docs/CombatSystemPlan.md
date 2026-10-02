# 战斗系统排期（七层重建后）

> 架构文档：`Docs/EntityArchitecture.md`（2026-10 七层重建后的新架构）。
> 重建把原 M1~M3 的"指令层/装备/连段"提前落地进角色系统，本计划只剩战斗侧增量。
> 每个里程碑结束时游戏可玩、可回归。

## 已落地（随七层重建，2026-10）

| 原里程碑 | 现归宿 |
|---|---|
| M1 指令层（QueuedSkillSlot 等指令语义） | `Input/CommandBuffer.cs`（电平/边沿两类，网络镜像就绪） |
| M2 装备数据（角色×武器组合查询） | `Data/` 六张 SO + SlotContainer 四槽（容量模型 handCost ≤ weaponCapacity） |
| M2.5 效果容器三成分 | `Logic/Modifier/`（ModifierList + ModifierData + 枚举族；失控呈现 EnumControlKind 注册表） |
| M3 连招数据表 + 通用攻击状态（前半） | `WeaponComboGraph.comboEntries` + `EntityAttackState`（连段推进/取消窗口/瞄准射击单发段） |
| 附身玩法（IsPlayerControlled 换输入源） | Entity.IsPlayerControlled setter → Brain.BindInputSource（F10 演示） |
| 决策树 AI 收编输入源 | `Input/AI/AITreeInputSource`（决策定时器 + 意图翻译） |
| 相机行为进 Presentation | `Presentation/CameraRig`（抬头90°/低头靠近/奔跑加速 FOV） |

## M3R：命中判定（下一个里程碑）

目标：攻击命中帧有判定——普攻/射击的 hit 窗口做球形 Overlap，命中走
`target.Brain.TakeDamage`（唯一伤害入口，入口修正链已就位）。

- 命中参数挂 `ComboEntry`（半径/偏移/伤害基础值——纯"加"型改动）
- 命中仲裁（霸体）：施加 CC 前查 `target.Brain.Capability.HasControlImmunity()`
  （状态声明式，框架已就位——造一个 GrantsSuperArmor=true 的重击状态即可验证拦截）
- 命中反馈写回连段：`EntityAttackState` 加 LastHitResult（Hit/Miss/Blocked/None），
  出招表"进入条件"字段（无条件/上段命中才续）扩展 ComboEntry
- `EnumStatType.DamageDealt` 乘数接入命中计算（现在只挂账）

验证：3 段连招三种路径（全按出全连/断按收招/上段命中才续的段打空断连）；
挥剑减伤、急速/虚弱乘数、眩晕打断全链路回归。

## M4：技能执行效果 + AI 选招（Utility）

- 技能执行：`SkillSO` 挂效果引用（位移/伤害/施加 Modifier/段位数据），
  释放起手进 Action 层（AttackState 的单发段模式已预留 BeginSingle）；
  冠冕/潮汐两技能各做一个演示闭环（faithDelta 冠冕配正·潮汐配负，钟摆随之摆动）
- 双闸门（已落地）：冷却（SkillSlot 剩余）+ 信心方向闸门（SkillResource.CanApply——
  信心 ∈ [-67, +67] 对称钟摆，增量 = (int)kind × faithDelta（枚举值即方向因子：
  冠冕 +1 涨/潮汐 -1 降，SO 只配正数幅度），贴边锁向防单一依赖，初始 0 居中）
- Utility 选招：`AI/Decision/UtilitySelector.cs`（与 Decision 同基类）：
  候选 = 技能槽（装什么会什么），打分（距离/冷却/信心方向余量/权重），最高分过阈值胜出
- 敌人决策树升级：Selector（Utility 选招 → 追击 → 兜底待机）
- 感知最小化：AITreeInputSource 加 TargetDistance / TargetInAttackRange（决策读）

验证：敌人进距离起手、冷却未转好或信心方向锁死不选、换技能槽 SO 行为变。

## M5：表现接入（动画/特效/UI）

- 动画：EntityVisual 按状态 Enter/Exit + locomotionStyle 接 Animator（架构不变，
  只动 Presentation）；出招表逐段动画引用届时挂 ComboEntry
- 特效：命中/施加 Modifier 的表现（事件驱动：HpChanged/Died/EquipmentChanged 已就位）
- UI：数值面板/技能冷却/信心钟摆条（-67 ↔ +67 居中指针，读 Data 层分布式面板）
- 正式感知系统替换 target 占位

## M6：Boss 阶段 + 池化

- BossPhaseController：血量阈值切 Phase（行为集 = 决策树 + 出招表 + 参数覆盖）
- SimplePool：杂兵/投射物通用池；归还重置面 = Vitals.Initialize + ModifierList 全驱散 +
  CharacterTagSet 清位 + 状态机回初始层

## M7：网络（NGO 落地）

装 NGO，按 `EntityArchitecture.md` §十一的接缝清单兑现：
NetworkInputRelay 顶替 PlayerInputSource（服务端权威）、Data 层 SO 引用换 id 注册表、
CharacterTagSet/CommandBuffer 整包同步、Brain 切 ServerTick、spawn 系统指派每玩家相机。

## 明确不做（防过度设计）

- **GOAP / 行为树**：出现"长期自主目标"玩法才评估，且只换 Input 层的决策件
- **统一 ISlot 接口**：UI 需要跨槽遍历时再提（单一实现配接口 = 投机通用性）
- **连招预规划承诺**：用派生表（进入条件分支）表达，不做动态规划
- **大而全 Stats 字典**：数值面板保持分布式（Vitals/Motor/SkillSlot/config）+ Entity 门面
- **动画选型**：M5 前全部计时器占位；接动画只动 Presentation 与 ComboEntry
