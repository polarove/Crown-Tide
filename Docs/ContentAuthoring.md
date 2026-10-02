# 内容制作手册（怎么加新武器 / Buff / 角色 / 敌怪 / Boss）

> 本文件回答"我想加 XX，要动什么"。设计语义见 `Docs/CharacterSystemDesign.md`，
> 架构细节见 `Docs/EntityArchitecture.md`，本次落地改动见 `Docs/ChangeLog.md`。
> 结论基于 2026-10-02 对代码的实读（每条判断都附文件/行号级证据），不是照设计文档转述。

## 零、先看这张表：现在能做到什么程度

| 想加的东西 | 现状 | 卡点 |
|---|---|---|
| 新武器 | ✅ 完全数据驱动 | 但**武器不会造成伤害**（无命中判定、无伤害字段） |
| 新 buff / debuff | ✅ 完全数据驱动 | 只有 5 个 stat 可调；技能类效果需先补技能载荷 |
| 新角色 | ✅ 配置即可 | **无可视模型**，只能靠 HUD 区分 |
| 新敌怪 | ⚠️ 只能"更快/更肉" | AI 决策树写死在代码里，**AI 不会攻击** |
| Boss | ❌ 做不出来 | 无阶段、无技能效果、无伤害 |

**三个地基缺口**（卡住上面所有内容，详见 §五）：

1. **伤害管线未闭环**——`EnumStatType.DamageDealt` 在源码中只出现在注释里（从未被读）；
   `EntityAttackState` 头注释"命中判定本轮无（M3 挂球形 Overlap）"，`Tick` 收尾注释"命中帧无判定"；
   全项目 `OverlapSphere` 零命中。`EntityBrain.TakeDamage` 的唯一调用方是 `ModifierList` 的周期跳伤
   （`ModifierList.cs:120`）——**没有任何攻击能造成伤害**。
2. **AI 只会走**——`EnumAIIntent` 只有 `Idle/Chase`；`AITreeInputSource.GatherCommands` 只写 `MoveDirection`，
   从不写 `AttackQueued`/`SkillSlotQueued`；决策树硬编码在 `BuildDecisionTree()`（`AITreeInputSource.cs:80-85`）。
3. **技能没有效果**——`SkillSO` 只有 `Name/Kind/Faith/Cooldown`；`EntityBrain.TryConsumeAction`
   释放技能那段只扣冷却+信心值+`Debug.Log`，没有任何效果载荷。

---

## 一、新武器

### 步骤（纯数据，零代码）

1. `Create → Crown Tide/武器出招表` 建 `WeaponComboGraph`：（单持一张、双持一张，双持可省略 = 回退单持表）
   - `LocomotionStyle` 动画选型键、`MoveSpeedMultiplier` 持械移速修正；
   - `ComboEntries[]` 每段填 `Name/Windup/Hit/Recovery/NextEntry/CancelWindow`；
   - 可选 `HasCharge + ChargeEntry`（蓄力）、`HasShoot + ShootEntry`（瞄准射击，配合 `Commands.AimActive`）。
2. `Create → Crown Tide/武器` 建 `WeaponSO`：`Name`、`Cost`（占用容量）、`AttackSpeed`（1=不变）、
   `SwingDamageTakenMultiplier`（挥剑期间减伤，靠 `Swinging` 标签解释），两张表拖进去。
3. 挂到 Entity 的 `Slots.Weapons.MainHand` / `OffHand`。
   容量判定：`WeaponSlot.CanEquip` 要求 `这把的 Cost + 另一把的 Cost ≤ Config.WeaponCapacity`
   （默认 5：双匕首 2+2 可行、巨斧单持可行不可双持、圣剑 9 装不上）。

### 会造成的行为（都是既有代码在跑）

- 攻速：`EntityAttackState.DurationScale` = 段时长 ÷（武器 `AttackSpeed` × `AttackSpeed` 乘法链）。
- 移速：`WeaponComboGraph.MoveSpeedMultiplier` 在 `EntityState.ApplyLocomotion` 生效。
- 挥剑减伤：`Swinging` 标签（攻击状态 Enter 挂 / Exit 摘）→ `EntityBrain.TakeDamage` 读主手武器乘数。
- 瞄准射击翻译：`AimActive` 电平 + `AttackQueued` 边沿 + `HasShoot` → 走 `ShootEntry` 单发段。

### ⚠️ 加武器前必须先修的两个坑

1. **双份真相**：`EnumWeaponType` 的注释写"值是武器需要的容量（handCost）"且数值本身就是容量
   （`Dagger=2/GreatAxe=4/HolySword=9`），而 `WeaponSO.Cost` 的 Tooltip 也写"占用容量"。
   项目里目前**一个武器资产都没有**所以没爆，建第一把时两处填不一致就会出鬼。
   建议：**`Cost` 作唯一真相**，`EnumWeaponType` 仅保留为表现分类键（改注释与数值）。
2. **没有伤害字段**：`WeaponSO` 没有 `Damage`。要么在 `WeaponSO` 加 `Damage`（推荐，顺手），
   要么放到 `ComboEntry` 每段（同一武器不同段不同威力，更灵活但要改出招表结构）。
   无论哪种，都要先有 §五 的伤害管线才能生效。

---

## 二、新 buff / debuff（当前最快路径）

一条 `ModifierEffect` = 三个**可独立开关**的成分（`Create → Crown Tide/修饰效果`）：

| 成分 | 字段 | 说明 |
|---|---|---|
| 控制（失控） | `HasControl` / `ControlPriority` / `ControlKind` | 投影进 CC 层状态、压制其余层（冻结而非清除）；霸体在施加瞬间仲裁（免疫≠解控） |
| 数值 | `StatModifiers[]`（`{Stat, Multiplier}`） | 乘法链：无修饰=1，全部活跃条目连乘，Stack 按层数自乘 |
| 周期 | `HasPeriodic` / `TickInterval` / `DamagePerTick` | 持续伤害，走 `Brain.TakeDamage`（吃 `DamageTaken` 与挥剑减伤） |
| 标签 | `GrantedTag` | 纯命名（`EnumEntityTag`，32 位登记表在文件头注释） |

叠加策略 `EnumStackPolicy`：`Refresh` 刷新时长 / `Stack` 叠层（满层回落刷新）/ `Ignore` 忽略。
类别 `Category` 是 `[Flags]`（`Buff|Poison|ArmorSet`…），驱散 = `Dispel(类别)` 按位与非零过滤，
`Dispel(All)` 清全部（**`All` 不含 `ArmorSet`**：装备来源的效果不该被净化语义清掉）。

施加入口：`Brain.Modifiers.Apply(effect)`（命中入口/技能/饰品/调试键都走这里）。

### 可调数值只有 5 个（`EnumStatType`）

`MoveSpeed` / `JumpPower` / `AttackSpeed` / `DamageDealt` / `DamageTaken`。
`EnumStatType.cs` 文件头就是**消费读点登记表**（哪个 stat 被谁读、基础值在哪分裂）——加新 stat 必须同步登记。

### ⚠️ 两个语义限制

- **只有乘法链**：想表达"+10 攻击"这类加法型数值，现在只能配 1.2 这种乘数（乘数相对基础值）。
  要真加法得改 `ModifierList.GetStatMultiplier` 的聚合模型（加法项/乘法项分开）。
- **新 stat 要接消费读点**：加 `EnumStatType` 成员只是加了个枚举，`GetStatMultiplier` 的调用点必须在
  玩法里真正读它（否则又是"只挂账不读"的第二例，就像现在的 `DamageDealt`）。

### 新控制类型（冰冻/石化）

`EnumControlKind` 加成员 + 写一个新 `EntityState`（CC 层）+ `EntityBrain.controlStates` 注册一行。
未注册的 kind 会回落 `Stun`（`ResolveControlState` 的兜底），所以配错不会断链。

---

## 三、新角色（能加，但没身体）

1. `Create → Crown Tide/角色配置` 建 `CharacterConfigSO`：`MaxHealth` / `FaithCapacity`(默认 67) / `WeaponCapacity`(默认 5)。
2. 场景建 GameObject → 挂 `Entity`（`RequireComponent` 会自动补齐 `CharacterController`/`EntityBrain`/`EntityMotor`/
   `CharacterVitals`/`CharacterSlotContainer`）+ `EntityVisual` + `PlayerInput` + `PlayerInputSource` + `AITreeInputSource`。
3. 填 `Entity.Config`（**必须填**：空配置会让 `CharacterVitals.MaxHp` 兜底成 `float.MaxValue`，HUD 显示荒谬血量）。
4. `IsPlayerControlled` 勾/不勾——它唯一的职责是让 `EntityBrain.BindInputSource` 绑 `PlayerInputSource` 还是 `AITreeInputSource`。
5. 槽位填武器 / 技能 / 护甲 / 饰品。

### 现有限制

- **没有可视模型**：`Entity` 上只有 `CharacterController` 与逻辑组件，画面靠 `EntityVisual` 的 `OnGUI` HUD。
  要做外观差异需接 `EntityVisual`（接入点已留：状态 Enter/Exit + `locomotionStyle` 选型键 + `HpChanged`/`Died`/`EquipmentChanged` 事件）。
- 多人要各自相机：`CameraRig` 持 `FollowEntity` 引用（**不要用 `Camera.main`/`Find*`**，项目全局无这些调用，
  装配器里用了一次 `Camera.main` 取视角基准，属工具脚本、不在运行时路径）。

### 场景装配别手改 YAML

用 `Crown Tide/装配 SampleScene 演示实体（玩家 + AI）` 的套路（`Assets/Editor/CreateSceneDemo.cs`）：
场景文件是几十个带 fileID 交叉引用的文档，手改极易写坏（2026-10-02 修过一次被写坏的 GUIStyle 块）。
装配器已封装两个坑：编辑期不能走 `entity.Slots`（`Awake` 才注入）、Missing Script 只能用
`GameObjectUtility.RemoveMonoBehavioursWithMissingScript` 摘。

---

## 四、新敌怪 / Boss

### 现在能做到的（不改代码）

建 Entity + `IsPlayerControlled=false` + 填不同 `Config`（血量）/`EntityMotor.WalkSpeed`（速度）+
`AITreeInputSource.Target` 拖玩家 → "参数不同的追击怪"（SampleScene 的 Enemy 就是这么放的，只戴 1 件护甲、移速 4）。

### 需要改代码的能力（按代价从小到大）

| 想要 | 代价 | 做法 |
|---|---|---|
| 会攻击 | 小（依赖 §五①） | `EnumAIIntent` 加 `Attack`；决策加"距离 ≤ 攻击距离"；意图翻译写 `Commands.AttackQueued = true`（加冷却累积器防狂按） |
| 会放技能 | 小 | 加 `CastCrown`/`CastTide` 意图 → 写 `SkillSlotQueued`（注意 0 是"无请求"哨兵） |
| 走位/拉开距离 | 中 | 加 `Retreat`/`Strafe` 意图（写反方向/侧向）；真绕障要接 NavMesh（`com.unity.ai.navigation` 已装） |
| 感知（看到才追） | 中 | `AITreeInputSource.Target` 目前是 Inspector 手拖（它自己注释为"占位"）；要距离/视线判断得写感知层 |
| **Boss（阶段 / 技能循环）** | **大** | 决策树硬编码在 `BuildDecisionTree()` → 必须抽成**数据驱动**：新增 `AIPatternSO`（条件 + 意图 + 阶段切换）由 SO 装配决策树；否则每加一个 Boss/敌怪类型都要改代码并重编译，违背项目"新内容 = 数据"的既定原则 |

**判断依据**：`AITreeInputSource` 的决策机械（`DecisionSelector`/`DecisionBranch`/`DecisionLeaf`）本身是泛型可复用的
（`Decision<TContext,TIntent>` 构造上与 Entity 解耦），缺的只是"从 SO 读规则来装配树"这一层。

---

## 五、地基缺口清单（要加内容就得先补）

### ① 伤害管线闭环（最高优先，其他三件都依赖）

| 缺口 | 证据 | 建议实现 |
|---|---|---|
| 无命中判定 | `EntityAttackState` 头注释与 `Tick` 注释均写明本轮无；全项目无 `OverlapSphere`/`OverlapBox` | 在攻击状态的"命中帧"窗口做一次范围查询（`Physics.OverlapSphere` + 目标层掩码），命中列表交给伤害入口 |
| 无出手方伤害入口 | `DamageDealt` 只出现在 `EnumStatType` 注释；`TakeDamage` 只被周期跳伤调用 | 新增 `EntityBrain.TryDealDamage(Entity target, float baseDamage)`：出口 = `baseDamage × Modifiers.GetStatMultiplier(DamageDealt)`，入口 = `target.Brain.TakeDamage(...)`（已吃 `DamageTaken` × 挥剑减伤） |
| 武器无伤害数值 | `WeaponSO` 无 `Damage` 字段 | 加 `WeaponSO.Damage` 或 `ComboEntry.Power` |
| 命中反馈无 | 无受击事件 | 复用 `CharacterVitals.HpChanged/Died` + `EnumEntityTag` 加 `Hurt` 之类，供表现层读 |

### ② AI 会攻击（①之后立刻有用）

`EnumAIIntent` 加 `Attack`；意图翻译写 `AttackQueued`；决策条件加距离；加攻击冷却累积器（网络时间纪律：用累积器不用 `Time.time`）。

### ③ 技能效果载荷

`SkillSO` 加效果引用（一组 `ModifierEffect` + 位移/召唤等类型键），在 `EntityBrain.TryConsumeAction`
的技能分支里结算（现在是"只扣闸门 + `Debug.Log`"）。

### ④ AI 数据驱动（要做 Boss 就必须）

新增 `AIPatternSO` 装配决策树，替代 `BuildDecisionTree()` 硬编码。

### ⑤ 表现层

`EntityVisual` 接模型/动画（状态 Enter/Exit 与 `locomotionStyle` 的接入点已留）。

---

## 六、已知设计债与坑（新写内容前先看这 6 条）

> 本表是节选（与内容制作直接相关的）。**完整清单（按优先级 + 评估结论）见 `Docs/EntityArchitecture.md` §十五「已知设计债与风险」。**

| # | 问题 | 影响 | 位置 |
|---|---|---|---|
| 1 | `EnumWeaponType` 与 `WeaponSO.Cost` 双份真相 | 加第一把武器时必踩 | `EnumWeaponType.cs` / `WeaponSO.cs:24` |
| 2 | `CameraRig.Awake` 里 `InputActionAsset.FindAction` 无空检查 | 忘拖资产 → NRE（项目其他 IO 都做了 null 早退） | `CameraRig.cs:99-102` |
| 3 | `ModifierList.Remove/Apply` 按 SO 引用归一 | 同一 `ModifierEffect` 既做套装档位又被手动施加 → 破套会连带摘掉外部那条（已写进 `ChangeLog.md` §8 与本文件） | `ModifierList.cs` |
| 4 | `Entity.Slots` 只在 `Awake` 后可用 | 所有 Editor 工具都得绕过（本次装配器踩过） | `Entity.cs:38` |
| 5 | 只有乘法链的数值聚合 | 加法型数值表达不了 | `ModifierList.GetStatMultiplier` |
| 6 | `DamagePerTick` 等伤害数值无钳制 | 配错就是秒杀/秒死；`TickInterval <= 0` 已有防死循环，但伤害值没有上下界 | `ModifierEffect.cs` / `ModifierList.cs:113-121` |

## 七、建议推进顺序（依赖关系决定）

```
① 伤害管线闭环 ──┬─→ ② AI 会攻击 ──→ ④ AI 数据驱动（Boss）
                ├─→ 武器伤害字段（顺手修双份真相）
                └─→ ③ 技能效果载荷 ──→ ⑤ 表现层
```

① 不做，②③ 都无法验证（打不动人）；④ 不做，Boss 只能靠改代码。
