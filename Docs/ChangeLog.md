# 变更记录（本次接手：护甲套装 + 命名统一 + 场景装配）

> 记录人：AI 接手会话（2026-10-02）。起点状态见文末「交接起点」。
> 本文件只记录**实际落地的改动**与**验证证据**；设计语义见 `Docs/CharacterSystemDesign.md`，
> 架构细节（七层/管线/Modifier 三成分）见 `Docs/EntityArchitecture.md`。

## 0. 交接起点（重要）

原任务描述为「GLM 已修改代码实现护甲套装功能，继续」。勘察结论：**仓库里没有任何护甲套装代码**。

| 核查项 | 结果 |
|---|---|
| 工作树 | 干净（除 `ModifierEnums.cs` 一处用户未提交改动） |
| 全部提交（含 `pre-entity-rebuild` tag、`--all`、reflog） | 无 ArmorSet 相关文件、无相关删除记录 |
| `git fsck --lost-found` | 无悬挂对象 |
| `Data/Armor/` 文件史 | 只有 `ArmorSO`/`ArmorSlot`/`EnumArmorPart`，从无第 4 个文件 |
| 唯一留存成果 | `ModifierData.cs → ModifierEffect.cs` 改名（重命名提交 `a334a96`） |

因此护甲套装为**从零实现**，按用户当轮确认的两条决策执行：
① 套装资产持有 `ModifierEffect`；② 2 件激活 2 件档，4 件 = 2 件档 + 4 件档**累加**。

---

## 1. 护甲套装功能

### 新增文件

| 文件 | 职责 |
|---|---|
| `Assets/Scripts/Entity/Data/Armor/ArmorSetSO.cs` | 套装资产 = 名字 + 档位表；含 `[Serializable] ArmorSetBonus{PieceCount, Modifier}`、`DescribeTiers()`、`OnValidate` 校验（门槛越界/同门槛重复/空效果/同效果被两档共用，全部只告警不静默丢弃并顺带按件数升序排表） |
| `Assets/Scripts/Entity/Logic/Armor/ArmorSetBonusList.cs` | Logic 层纯 C# 套装引擎：`Sync()` 收集达标档位 → 差分 → 摘不再达标、挂新增；`ActiveTierCount`/`Clear`/`Describe` |
| `Assets/Editor/CreateArmorSetDemoAssets.cs` | 幂等生成演示资产（3 件护甲 + 二/四件档 + 玩家/敌人角色配置） |
| `Assets/Editor/CreateSceneDemo.cs` | SampleScene 装配器 + 只读自检（见 §3） |

### 修改文件

| 文件 | 改动 |
|---|---|
| `Data/Armor/ArmorSO.cs` | 加 `Set`（空 = 散件）；注释更正 `ModifierData` 口径 |
| `Data/Armor/ArmorSlot.cs` | 加 `EquippedCount`/`CountOf(set)`/`Get(index)`/`GetByPart`/`PartAt`/`PartCount`；`Equip(null)` 改为抛 `ArgumentNullException`；**部位遍历走 `PartOrder` 表**（`EnumArmorPart` 是 1 起算 `Head=1..Feet=4`，绝不能拿枚举值当数组下标） |
| `Data/Character/CharacterSlotContainer.cs` | 加 `TryEquipArmor`/`UnequipArmor`（成功后 `Sync()` → `EquipmentChanged`）；持有 `ArmorSetBonuses` 引用；`using ...Data.Armor` |
| `EntityBrain.cs` | 加 `ArmorSets` 字段；`Bootstrap` 构造引擎、注入容器、末尾 `Sync()` 覆盖 Inspector 预配装备 |
| `Logic/Modifier/ModifierList.cs` | 加 `Remove(ModifierEffect)` 精确摘除（按 SO 引用，与 `Dispel` 类别批量清分工）；`Dispel` 注释标注 `All` 不含 `ArmorSet` |
| `Presentation/EntityVisual.cs` | HUD 加护甲行 `护甲 头胸腿 3/4｜套装 名字 3/4·1档`；矩形高度 260→285 |
| `Data/Character/CharacterConfigSO.cs` | 注释 `config → Config` |
| `Logic/Modifier/EnumStatType.cs` | 注释同步新字段名 |
| `Logic/Modifier/ModifierEnums.cs` | **未碰**（用户的 `ArmorSet = 1 << 5` 保持原样，工作区改动不被覆盖） |

### 逐档累加的实现要点

- `Sync()` 收集**所有**「件数 ≥ 门槛」的档位（不取最高档）→ 4 件时二档与四档各自成为独立条目、各自走乘法链。
- 幂等：已在挂的档位不重复 `Apply`（避免搅动 `Refresh`/`Stack` 语义）。
- 自愈：登记过但已不在列表（被全驱散/到期）→ 下次 `Sync()` 重新 `Apply` 补回。
- 运行时状态全在引擎自身容器，**绝不写 SO**（模板/实例分层纪律）；`Sync` 复用容器，变更路径零 GC。

---

## 2. 字段命名统一（PascalCase）

用户要求「字段名统一 PascalCase（`Name`，不用 `DisplayName`），其他字段顺手改」。

| 文件 | 字段改动 |
|---|---|
| `Entity.cs` | `config → Config`（含 `EntityBrain`/`EntityVisual` 两处调用点 + 注释） |
| `Physics/EntityMotor.cs` | `walkSpeed/sprintMultiplier/turnSpeed/jumpHeight/gravity/apexHangGravityMultiplier/apexHangVerticalSpeedThreshold/fallingGravityMultiplier/groundCheckDistance/groundMask/controller` 全部 PascalCase；调用点见 `EntityBrain`/`LocomotionStates` |
| `Input/PlayerInputSource.cs` | `viewTransform/fallbackActions/sprintMode/sprintTapTime/entity→Host/playerInput→PlayerInput/sprintAction/aimAction/bound→Bound/warnedMissingModifiers/sprintPressing/sprintPressTimer/sprintToggled/sprintHoldActive` 全部 PascalCase；另修 `ModifierData` 过期注释 |
| `Data/Character/CharacterTagSet.cs` | 私有 `bits → BitMask`（避免与 `public ulong Bits` 遮蔽） |
| `Logic/States/EntityAttackState.cs` | `comboGraph/entries/single/isSingle/comboIndex/elapsed` → PascalCase |
| `Logic/Modifier/ModifierEffect.cs` | `StatModifierEntry.stat/multiplier → Stat/Multiplier` |
| `Data/Character/CharacterConfigSO.cs`、`Input/AI/AITreeInputSource.cs`、`Logic/Modifier/EnumStatType.cs` 等 | 注释口径同步 |

**范围界定**：只改**字段**。局部变量与参数本轮未动（项目现状「字段 PascalCase + 局部/参数 camelCase」本身已自洽；
`EntityState`/`LocomotionStates`/`AITreeInputSource` 里 `Entity` 字段与 `entity` 参数并存属正常写法）。
例外：`PlayerInputSource.moveInput` 保留小写（与 `Host` 同文件内的内部状态字段，命名统一时可一并处理）。

---

## 3. SampleScene（修复 + 装配）

### 3.1 YAML 修复

**症状**：Unity 打开报 `199: expecting a closing }`。

**根因**：`PlayerController` 的 `debugStyle`（GUIStyle）中 8 个字形状态（`m_Normal/m_Hover/m_Active/m_Focused/m_On*`）
被写成**跨行 flow 风格映射**且带游离 `}`，反序列化器断行后 `{` 配对丢失，一路找到第 199 行才报错。

**关键判定**：该文件与 `HEAD` **逐字节相同**（SHA256 一致），且 `git log --follow` 显示它**自最初提交 `0c023c3` 起再未改动**——
即坏 YAML 是**被提交进仓库的存量问题**，不是本次或用户近期改动造成（用户 Unity 本地能开，是因为编辑器内存里的场景对象还在）。

**修法**：8 段改回 Unity 标准块风格，语义不变（`m_Background: {fileID: 0}`
`m_ScaledBackgrounds: []` `m_TextColor: {r: 0, g: 0, b: 0, a: 1}`）。全文件仅此一处改动。

**验证**：让 Unity 批处理真正打开场景（临时校验器，用完已删）：`isLoaded=True isValid=True rootCount=5`，
根物体 `Main Camera | Directional Light | Global Volume | Platform | Player`，无解析错误。

### 3.2 装配玩家 + AI

去掉旧架构残留（`PlayerController`/`CharacterEquipment`/`CameraFollow` 的 **Missing Script**），换上：

- **Player**：新架构组件齐备；`IsPlayerControlled = true`；穿演示头盔+胸甲（二件档 ×1.15 开局生效）；HUD 开。
- **Enemy**：`IsPlayerControlled = false`；`AITreeInputSource.Target` 指向玩家（追击）；只戴头盔；HUD 关。
- **Main Camera**：挂 `CameraRig` 跟随玩家，输入资源换成含 `Look/MouseLook/SwitchShoulder/ToggleView` 的
  `PlayerControls.inputactions`（原指向 `InputSystem_Actions`）。

场景规模：24,512 → 31,603 字节；`git diff` 633 行变化；Missing Script 残留 0。

**装配器踩坑与规避**（代码内已注释）：

1. `Entity.Slots` 是 `Awake` 在**运行时**注入的只读门面，编辑期不跑 Awake → 装配/校验必须直接
   `GetComponent<CharacterSlotContainer>()`；走 `entity.Slots` 会空引用。
2. 旧架构类已删除 → 其组件是 **Missing Script**，`GetComponent(string)` 看不见，
   只有 `GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go)` 能摘。
3. `PlayerInput.defaultMap` 不是公开属性（只有序列化字段 `m_DefaultActionMap`）→ 用 `SerializedObject` 落值。
4. 改私有序列化字段（`Entity.isPlayerControlled`）同理只能走 `SerializedObject`——属性 setter 依赖运行时 `Brain`，编辑期不落值。
   为此加了 `RequiredProperty(so, "config", "Config")` 别名查找：字段改名后**报错而不是静默不落值**（本次就靠它抓到 `config → Config`）。

---

## 4. 顺带修掉的三个存量 Bug

均为测试暴露、与护甲功能无关，但不修则场景/测试跑不起来：

| # | 位置 | 问题 | 修法 |
|---|---|---|---|
| 1 | `Logic/EntityCapabilities.cs`（4 处）、`Logic/Modifier/ModifierList.cs`（1 处） | 调用 `Brain.Machine`，但属性早已改名 `StateMachine` → **HEAD 状态本来就编译不过**（Unity 报 5 个 CS1061） | 改为 `Brain.StateMachine` |
| 2 | `Data/Character/CharacterSlotContainer.cs` | `UnarmedComboGraph = new()` 对 `ScriptableObject` 调 `new()` → 运行时必抛 `UnityException`（一挂 `Entity` 就报） | 去掉初始化器，允许空引用（空手/缺表由 `AttackState` 内置默认节奏兜底，代码注释原本就这么写） |
| 3 | `Logic/EntityState.cs` | `ApplyLocomotion` 直接解引用可能为空的出招表 → 空手时每帧 NRE | 出招表缺失时移速修正按 ×1 兜底 |

---

## 5. 测试与工程结构

| 新增 | 说明 |
|---|---|
| `Assets/Scripts/Entity/Runtime.asmdef`（`Entity.Runtime`） | 游戏代码单独成程序集。**必须**：Unity 明确限制「asmdef 不能引用预定义程序集 `Assembly-CSharp`」，测试要引用游戏代码只能如此。`autoReferenced: true` → 现有 Editor/场景代码照常可见 |
| `Assets/TestAssemblies/EditMode/`（asmdef + `ArmorSlotTests.cs`） | 10 例：件数计数/两套混合/散件不计/同部位替换/越界/空引用抛异常/`Get` 顺序/档位表可读输出 |
| `Assets/TestAssemblies/PlayMode/`（asmdef + `ArmorSetRuntimeTests.cs`） | 13 例：真 `Entity` 全链路——1/2/3/4 件四态、**4 件两档同时在挂**、逐件卸下阶梯回落、换套、幂等、精确摘除、全驱散与自愈 |

测试程序集配置要点：`references` 需显式列 `UnityEngine.TestRunner`/`UnityEditor.TestRunner`/`Entity.Runtime`，
`overrideReferences: true` + `precompiledReferences: ["nunit.framework.dll"]`（NUnit 来自 `com.unity.ext.nunit` 包）。

---

## 6. 验证证据（最终，均为最新产物）

| 项目 | 结果 | 证据 |
|---|---|---|
| 场景自检 `CreateSceneDemo.Verify` | **全部通过**：组件齐备 / 护甲件数 2:1 正确 / 相机与 AI 接线正确 / 无旧组件残渣 | `Logs/unity-scenecheck.log` |
| EditMode | **10 / 10 通过，0 失败** | `Logs/EditMode.xml` |
| PlayMode | **13 / 13 通过，0 失败** | `Logs/PlayMode.xml` |
| 编译 | 0 错误（`Entity.Runtime` / 测试程序集 / Editor 程序集） | `Logs/unity-*.log`，`dotnet build` |

**批处理实测的两个坑（供后续参考）**：

- `-runTests` **不能配 `-quit`**：`TestStarter.Init()` 在 `EditorApplication.isCompiling` 时会直接 `return`，
  `-quit` 会把进程带走 → 一个测试都不跑且无报错。测试跑完框架自己退出。
- Unity 批量进程偶发**保存完场景后卡在退出流程**（仍占 `Temp/UnityLockfile`），会挡住后续所有 Unity 调用；
  处理：确认场景已存盘后杀进程树（`Unity.exe`/`CrashHandler`/`AutoQuitter`/`ILPP`）并清 lockfile。
  注意**不要杀 `Unity.Licensing.Client.exe`**（杀了会让下一轮 Unity 启动即中断）。

---

## 7. 与原始计划的偏差（如实登记）

| 偏差 | 原因 |
|---|---|
| 新增 `Entity.Runtime.asmdef`（计划里没有） | Unity 硬限制：测试 asmdef 无法引用 `Assembly-CSharp`，不改结构则测试无法编译 |
| 修改 `CharacterSlotContainer.UnarmedComboGraph`、`EntityState.ApplyLocomotion`、`EntityCapabilities`/`ModifierList` 的 `Brain.Machine` | 三者都是**存量缺陷**，不修则场景跑不起来/测试无法通过 |
| `ModifierEnums.cs` 未改 | 用户已在工作区把 `Armor = 1 << 5` 改成 `ArmorSet = 1 << 5`，按「不抢改」处理 |
| 新增 `Assets/Editor/CreateSceneDemo.cs`、`CreateArmorSetDemoAssets.cs` | 场景 YAML 手改风险过高（刚修过一次），改为 Editor API 装配 + 幂等菜单 + 自检 |
| 加了角色配置演示资产 | 空 `Config` 会让 HUD 血量显示 `float.MaxValue`（`CharacterVitals.MaxHp` 的兜底），必须给场景配 `CharacterConfigSO` |

---

## 8. 未做 / 待定夺

- **护甲值减伤**：`ArmorSO.Value` 未接入 `EntityBrain.TakeDamage`（原需求注明"将来由 Logic 层实现"，属 M3）。
- **AI 不会攻击玩家**：命中判定（球形 Overlap）属 M3；当前 AI 只会贴近。
- **nullable 警告噪声**：`Directory.Build.props` 开着 `<Nullable>enable</Nullable>`，但 Unity 不读该设置 →
  `Entity?` 之类标注在 Unity 里是 97 条 `CS8632`。根治需开 Unity 的 nullable context（会一次性涌出大量
  `CS8618/CS8602`），**未动**，属既有决定。
- **局部变量/参数命名**：本轮只统一字段；如需连同局部与参数一并处理，另开一趟。
- **`Docs/CharacterSystemDesign.md`**：用户自有文件，本次仅**追加**护甲套装/演示场景/测试/命名约定章节，
  原有内容未删改（含把 `EntityHealth` 更正为 `CharacterVitals` 一处）。
- 所有改动均**在工作区**，未执行任何 `git commit`；命名统一与功能实现是两个独立逻辑 diff，可分开提交或回退。

---

## 9. 追加：文档整理 + 项目评估（同日，无代码改动）

本段**不含任何 `.cs` 或场景改动**，只新增/更新文档。

### 新增文档

| 文件 | 内容 |
|---|---|
| `Docs/ContentAuthoring.md` | **内容制作手册**：新武器 / 新 Buff-Debuff / 新角色 / 新敌怪-Boss 的逐步操作 + 卡点；§五 地基缺口清单（含建议实现）；§六 设计债节选；§七 推进顺序 |
| `Docs/ChangeLog.md` | 本文件 |

### 更新文档

| 文件 | 改动 |
|---|---|
| `Docs/EntityArchitecture.md` | ① 顶部加 `ContentAuthoring.md` 与 §十五 指引；② 新增 **§十五「已知设计债与风险」**：15.1 地基缺口（4 条，附证据）、15.2 值得保留的优点（改地基时别破坏）、15.3 设计债清单（按优先级，含建议处理）、15.4 未实装但已留接缝 |
| `Docs/CharacterSystemDesign.md` | 追加三/四/五章（护甲套装、演示场景、测试）与命名约定；`EntityHealth → CharacterVitals` 更正 |

### 评估结论摘要（依据全部来自实读代码）

> 架构骨架合格、纪律严格（依赖方向/模板实例分层/单 Awake 单 Update/时间纪律都落实），
> 但**玩法闭环缺三块地基**：① 伤害管线（`DamageDealt` 只挂账、无命中判定、武器无伤害字段）
> → ② AI 不会攻击（`EnumAIIntent` 只有 Idle/Chase）→ ③ 技能无效果载荷（`SkillSO` 只有闸门数据）。
> 另 ④ AI 决策树硬编码，Boss 必须抽成数据驱动才能做。
>
> 因此当前的内容扩展能力：**武器/buff 可纯数据加**（武器无威力）、**角色可加但无身体**、
> **敌怪只能改参数**、**Boss 做不了**——瓶颈在地基链，不在"数据驱动框架不够用"。

### 顺带发现

- `EnumWeaponType` 与 `WeaponSO.Cost` 双份真相（项目尚无武器资产，建第一把时必踩）——**未修**，已进 `EntityArchitecture.md` §15.3。
- ~~`CameraRig.Awake` 的 `InputActionAsset.FindAction` 无空检查~~ **已于 §10 修掉**。

---

## 10. nullable 注解策略落地（同日，**有代码改动**）

用户定调：**"ArmorSO 为 null 是期望结果，因此不关 null check，改为把需要处理的 null 做空判断"**。
据此把全项目的可空注解与空判断改成一致（判据与分类见 `EntityArchitecture.md` §15.5）。

### 改动要点

| 类别 | 处理 |
|---|---|
| **null 是合法状态 → `?`** | `ArmorSlot.Head/Chest/Legs/Feet`、`ArmorSlot.Get(int)`/`Equip`/`Unequip` 返回值、`ArmorSO.Set`、`WeaponSO?` 主/副手、`SkillSO?` 冠冕/潮汐与 `SkillSlot.TryGet` 的 out、`AccessorySO?[] Accessories`、`CharacterVitals.Config/Faith`、`CharacterSlotContainer.ArmorSetBonuses/EquipmentChanged/UnequipWeapon`、`ArmorSetSO?`（不在本轮）、`EntityBrain.InputSource`、`EntityStateMachine` 层槽（`EntityState?[]` + `GetActive` 返回可空）、`PlayerInputSource.ViewTransform/FallbackActions`、`CameraRig.InputActionAsset` |
| **装配/序列化保证非空 → `= null!`** | `EntityBrain.Commands/StateMachine/Modifiers/ArmorSets/Capability` 与七个状态实例、`Entity.Brain/Motor/Vitals/Slots`、`EntityMotor.Controller`、`PlayerInputSource.Host/PlayerInput/SprintAction/AimAction`、`EntityVisual.Entity/HudStyle`、`ModifierList.Entity` 与其内部 `Modifier.Effect`、`AITreeInputSource.Entity/DecisionTree`、`EntityAttackState.ComboGraph/Entries`、`CameraRig` 四个 InputAction + `EntityMeshRenderers`、`ArmorSetRuntimeTests` 的 `Host/TestEntity` |
| **读点补空判断** | `EntityBrain.Update` 开头把可空属性收窄成局部并**统一早退**（唯一装配门），`TryConsumeAction/TryConsumeJump` 改为接收 `host/commands/machine/modifiers` 参数；`TakeDamage` 同样先收窄；`EntityCapabilities` 用单点 `Machine` 属性（`Entity.Brain?.StateMachine`）收敛四处解引用；`EntityStateMachine.TwoPassTick` 用 `is { } x` 模式；`EntityVisual.DescribeWeapon` 处理空手/缺表（原来会 NRE）；`CameraRig.Awake` 补 `InputActionAsset` 空检查（**修掉 §15.3 登记的"忘拖资产就 NRE"**） |
| 接口签名一致 | `IInputSource.GatherCommands(CommandBuffer commands)`（原声明 `?` 而实现要非空，CS8767）；`ModifierList.Remove` 的 null 早退保留 |

### 验证

| 项目 | 结果 |
|---|---|
| `dotnet build -t:Rebuild`（Entity.Runtime / Assembly-CSharp / -Editor / 两个测试程序集） | **全部 0 警告 0 错误**（`Entity.Runtime` 由 186 → 0） |
| EditMode | 10/10 通过 |
| PlayMode | 13/13 通过 |
| 行为变化 | 无（注解是纯元数据；管线改动只是把判空提前收窄） |

### 附带修正

- `EntityVisual` 武器行原本在空手时会 NRE（`slot.MainHand.Name`），现在显示"空手/无表"。
- `CameraRig` 未拖 `InputActionAsset` 时：报一次错 + 禁用自身，不再 NRE。
- `TutorialInfo`（Unity 自带教程模板）5 条警告按其语义修掉（`Section` 四个序列化字符串用 `null!`，`SelectReadme` 返回 `Readme?`），保持全项目零警告。

---

## 11. Unity 侧也启用 nullable 分析（同日，**有代码/配置改动**）

用户要求："把 unity 的 null 也 enable 了"。

### 落地方式（实测，两个坑）

新增 `Assets/csc.rsp`：

```text
-nullable:enable
-langversion:9.0
```

| 尝试 | 结果 |
|---|---|
| `ProjectSettings.asset` 的 `additionalCompilerArguments: { Standalone: -nullable:enable }` | ❌ 不生效（该字段属 `PlayerSettings`，构建期；Unity 生成的 `Entity.Runtime.rsp` 里没有它） |
| `additionalCompilerArguments: { '': -nullable:enable }` | ❌ 同样不生效（键被 Unity 保留但没进 rsp） |
| `Assets/csc.rsp` 只写 `-nullable:enable` | ⚠️ rsp 里**有**该参数，但 CS8632 仍 73 条——Unity 默认语言版本把 `-nullable:` 忽略了 |
| **`Assets/csc.rsp` 写 `-nullable:enable` + `-langversion:9.0`** | ✅ **生效**：CS8632 归零，开始真正做可空分析 |

> 已回滚 `ProjectSettings.asset`（现与 HEAD 一致，`git diff` 为空），只保留 `Assets/csc.rsp`。

### 开启后 Unity 额外捞出的真问题（13 处字段注解写错）

dotnet 侧看不到这批（Unity 编译器此前无 nullable 上下文）；它们的读点本来就有空判断或"空 = 未配置"语义，
说明**"不要关 nullable"的判断是对的**——一开就在自家代码里抓到 13 个错标：

| 文件 | 字段 |
|---|---|
| `AccessorySO` | `GrantedModifier`、`HotkeyAction` |
| `WeaponSO` | `ComboGraphSingle`、`ComboGraphDual` |
| `Entity` | `Config` |
| `AITreeInputSource` | `Target` |
| `CharacterSlotContainer` | `UnarmedComboGraph` |
| `ArmorSetSO` | `Bonuses` |
| `CameraRig` | `FollowEntity` |
| `PlayerInputSource` | `DebugStunModifier`、`DebugHasteModifier`、`DebugWoundModifier` |
| `TutorialInfo/Readme` | 6 个模板字段（含 `icon/title/sections`） |

连带放宽的签名：`CharacterVitals.Initialize(CharacterConfigSO?)`、`PlayerInputSource.ApplyDebugModifier(Entity, ModifierEffect?, string)`；
`EntityVisual.DescribeHandCost` 补 `Config == null` 兜底（显示容量 0，与 `CharacterVitals` 的兜底口径一致）。

### 验证

| 项目 | 结果 |
|---|---|
| Unity 编译（Editor） | **0 条 CS 诊断**（`Logs/unity-nullable6.log`，含 nullable 分析） |
| `dotnet build -t:Rebuild`（五个程序集） | 全部 0 警告 0 错误 |
| EditMode / PlayMode | 10/10、13/13 通过 |
