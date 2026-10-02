# Unity 角色系统项目架构

## 一、总体分层

- **Input Layer**：定义：玩家输入 / AI 决策树，回答Entity要做什么
- **Brain Layer**：定义：输入源抽象 + 角色大脑，回答Entity怎么做
- **Logic Layer**：定义：从 Data Layer 获取数据进行判断与执行，并修改Data Layer。
   * 能不能：状态机能不能流转，动作 / 技能能不能执行，Modifier 能不能套到 Entity 上（由 Capability 统一查询）
   * 执行后写什么：把 Modifier 写入 Logic 的 ModifierList；把技能冷却、技能资源写回 Data 的 对应字段

- **Data Layer**： 定义：槽位 ISlot、运行时资源、可读数值面板。回答“装备的武器、技能、出招表、饰品、数值是什么，也就是Entity有什么？”，不负责展示，读取方：Logic 读它做判断，Presentation 读它做展示。
   * IWeaponSlot 武器容量 + 双持武器（由Entity传入最大容量，Entity装备武器时要检测容量，如当前Entity武器容量为5，巨斧需要5，那么巨斧就无法双持，如匕首需要2，2+2=4<5，所以这个entity可以双持匕首，如圣剑需要9，那么这个Entity就无法装备这个武器）
   * IWeaponSlot.Sheet 出招表 （由Weapon传入），不同 Weapon 有不同出招表，且单持双持出招表不一致，出招表决定了Entity的Idle、Walk、Sprint、Dodge、普通攻击、蓄力攻击的动画，可以双持不同武器，动画使用主手武器的，若只有一把武器则使用该武器的出招表动画，主手副手有不同的出招动画；动画不用实现。再举个例子，以射箭为例，需要具备瞄准+射击两个技能，只有射击，命中率大幅下降，只有瞄准，则无法射击，无法造成伤害；而修改数据又可以调整只有射击（盲射）的命中率，这样瞄准+射击就是一个连招，这样一来瞄准后按攻击键就是射击，而不是其他行为
   * ISkillSlot 冠冕技能和潮汐技能（由Enitty传入）
   * ISkillResource 信心值（int 整数，初始值由Enitty传入）能增减，由调用ISkillSlot的技能后返回delta，调用ISkillResource.Update(int delta) 来更新
   * IArmorSlot 护甲，能装备、卸下
   * IAccessorySlot 饰品，，能装备、卸下，如佩戴时获得指定buff的饰品，或佩戴后绑定快捷键实现某个功能（如打开背包，不要在data层处理逻辑）
   * 玩家具体数值，如生命值，移动速度，技能冷却时间等，用于玩家可看懂的数据面板
- **Physics Layer**：移动 / 重力 / 地面检测
- **Network Layer**：NGO 同步 / RPC / 预测
- **Presentation Layer**：动画 / 外观 / 特效 / UI / 相机，只读 Data / Logic / Physics 的状态，把武器、技能、饰品、数值呈现给玩家，不修改数据

Input → Brain → Logic → Physics → Presentation
                  ↑                   ↑
                Data ─────────────────┘
             （Logic / Presentation 都读）
                  ↑
                Network（横切，同步结果）

## 好处

* 新增资源类型，只在 Data 层加字段即可，不污染 Logic，说白了就把Data Layer当一个内存中的，属于特定Entity的database用

* “数据是什么”归 Data，“数据给谁看、怎么看”归 Presentation，边界不混
---

## 二、核心命名约定

- 所有角色统一叫 **Entity**，不再区分 Entity / NPC / Character 三层
- `Entity` 上有一个 `bool IsPlayerControlled`：
  - `true` → Player Character
  - `false` → NPC
- 该 bool 的**唯一职责**是决定 `EntityBrain` 绑定哪个 `InputSource`：
  - `true` → 绑定 `PlayerInputSource`
  - `false` → 绑定 `AITreeInputSource`
- 它只决定**输入来源**，不影响 Entity 本身的组件结构，也不参与任何内部逻辑裁决：
  - 能不能移动 / 攻击 / 跳跃 → 由 `Capability` 决定
  - 血量 / 伤害 / 霸体 → 由 `ModifierList` / `CharacterVitals` 决定
  - 槽位 / 装备 / 技能 → 由 `SlotContainer` 决定
  - 外观 / 动画 → 由 `EntityVisual` 决定
- 它还影响 **UI / 相机 / 存档等外围系统**，但这些系统只读取它，不修改它
- 一句话：**它管“谁来下指令”，不管“指令能不能执行”**
- 玩家操控 AI = 服务器把目标 Entity 的 `IsPlayerControlled` 置为 `true`，
  并把它的 `InputSource` 换成该玩家的 `PlayerInputSource`；原角色置为 `false`，
  换回 `AITreeInputSource`
- 强度差异来自 **槽位内容 + 决策树 + Config**，不来自类继承

```text
Entity (统一)
├── IsPlayerControlled => InputSource is PlayerInputSource
├── InputSource        (Player / AITree / Replay)
└── 其余组件完全一致   （IWeaponSolot / ISkillSolt）
```

### 代码命名约定（2026-10 统一）

字段一律 **PascalCase**（`Entity.Config`、`EntityMotor.WalkSpeed`、`ArmorSO.Name`）；
局部变量与参数用 camelCase；组件私有字段同为 PascalCase（`ModifierList.Entity`、`PlayerInputSource.Bound`）。
序列化字段直接以 PascalCase 命名，Inspector 显示名与代码名一致，不再维护两套写法。

---

## 三、护甲套装（实装）

护甲共 4 件（头/胸/腿/足，`EnumArmorPart`），一件护甲可属一个套装（`ArmorSO.Set`，空 = 散件不参与计数）。

### 3.1 档位模型

套装资产 `ArmorSetSO`（`CreateAssetMenu → Crown Tide/护甲套装`）配一张**档位表**，每条 = `{件数门槛, ModifierEffect}`：

| 已穿件数 | 生效档位 | 效果 |
|---|---|---|
| 0~1 | 无 | — |
| 2 | 2 件档 | 2 件套效果 |
| 3 | 2 件档 | 2 件套效果 |
| **4** | **2 件档 + 4 件档** | 两条**独立条目同时生效** |

门槛语义 = 「已穿件数 ≥ PieceCount 即激活」——**逐档累加，不是高档替换低档**。
数值配置注意：两档若改同一 stat 会走乘法链相乘，所以高档通常配"增量"
（演示套：二件档移速 ×1.15、四件档移速 ×1.05 + 受伤 ×0.8，穿满 4 件实际移速 ≈ ×1.21）。

### 3.2 分层归属（严格贴本文件第一、二节的分层）

| 环节 | 归属 | 说明 |
|---|---|---|
| 件数与套装查询 | `ArmorSlot`（Data） | `EquippedCount` / `CountOf(set)` / `Get(index)` / `GetByPart(part)`；纯只读，不处理逻辑 |
| 档位求值与挂摘 | `ArmorSetBonusList`（Logic，纯 C#） | 收集达标档位 → 与已挂集合差分 → 摘不再达标的、挂新增的。Brain 构造持有 |
| 触发点 | 变更驱动 | `SlotContainer.TryEquipArmor`/`UnequipArmor` 成功后调 `Sync()`；`Bootstrap` 末尾再调一次（覆盖 Inspector 预配的初始装备）。**不占每帧管线** |
| 数据流 | 装备写入点 ⇒ 套装重算成对 | 容器持有引擎引用 `ArmorSetBonuses`；无事件反向订阅、无隐藏顺序 |
| 幂等与自愈 | — | 同档位已在挂则不重复 `Apply`（不搅动 Refresh/Stack 语义）；被全驱散清掉但仍达标的下一次 `Sync()` 补回 |
| 呈现 | `EntityVisual`（Presentation） | HUD 一行：`护甲 头胸腿 3/4｜套装 名字 3/4·1档`，只读 |

**摘除两条路**：`ModifierList.Dispel(EnumModifierCategory)` 按类别位批量清（驱散/净化）；
`Remove(ModifierEffect)` 按 SO 引用精确摘单条（套装破套/换档用）。
`EnumModifierCategory.ArmorSet` 是**来源命名**位，`All` 刻意不含它——装备来源的效果由装备状态派生，
不该被"净化/全驱散"语义清掉。

### 3.3 边界与已知取舍

- 护甲值减伤（`ArmorSO.Value` 何时进 `EntityBrain.TakeDamage`）**未实装**，`Value` 目前只挂账。
- 同一 `ModifierEffect` 资产不要既做套装档位又被手动施加：破套按引用摘除会连带摘掉外部那条。
- 套装档位若配了控制成分：压制时机落在 `Sync()` 调用点（与 `Apply` 的"帧末施加次帧压制"不同帧）。

---

## 四、演示场景（实装）

SampleScene 由 Editor 装配器生成，**不要手改场景 YAML**：该文件是 31 个带 fileID 交叉引用的文档，
手改极易写坏（2026-10-02 修过一次被写坏的 GUIStyle 块）；用 Editor API 装配则由 Unity 自己产出合法 YAML。

| 菜单（`Crown Tide/`） | 作用 |
|---|---|
| 生成护甲套装演示资产 | 幂等生成演示套（3 件护甲 + 二/四件档 + 玩家/敌人角色配置） |
| 装配 SampleScene 演示实体（玩家 + AI） | 幂等重建 `Player`/`Enemy`、清旧架构组件与 Missing Script、接相机与 AI 感知、保存场景 |
| 校验 SampleScene 演示实体 | 只读自检（组件齐备/件数正确/接线正确/无旧组件残渣）；批处理经 `-executeMethod CreateSceneDemo.Verify`，失败退出码 1 |

场景内容（装配器：`Assets/Editor/CreateSceneDemo.cs`）：

- **Player**：`IsPlayerControlled = true`（Brain 绑 `PlayerInputSource`），穿演示头盔+胸甲 → 开局即吃二件档移速 ×1.15；DEBUG HUD 开。
- **Enemy**：`IsPlayerControlled = false`（Brain 绑 `AITreeInputSource`），`Target` 指向玩家 → 追击；只戴头盔（1 件不激活档位）。
- **Main Camera**：挂 `CameraRig` 跟随玩家（F1 切肩 / F2 切第一人称），输入走 `Assets/Input/PlayerControls.inputactions`。
- 操作：WASD 移动、Shift 加速、空格跳；F3~F11 调试键见 `PlayerInputSource` 头注释（**F10 可附身切换玩家↔AI**）。

**装配器踩过的两个坑（代码内已注释）**：

1. `Entity.Slots` 是 `Awake` 在**运行时**注入的门面，编辑期不跑 Awake → 装配与校验必须直接
   `GetComponent<CharacterSlotContainer>()`，走 `entity.Slots` 会空引用。
2. 旧架构的类已删除，它们的组件在场景里是 **Missing Script**，`GetComponent(string)` 看不见；
   只有 `GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go)` 能摘。

---

## 五、测试

`Assets/TestAssemblies/`（游戏代码因 Unity「asmdef 不能引用预定义程序集」的限制单独成 `Entity.Runtime` 程序集）：

- **EditMode（10 例）**：`ArmorSlot` 计数/查询/替换/越界/空引用纪律；`ArmorSetSO` 档位表可读输出。
- **PlayMode（13 例，真 Entity 全链路）**：1/2/3/4 件四态、逐件卸下阶梯回落、换套、散件不计入、
  未配效果的档位、幂等、精确摘除、全驱散与自愈。

验证入口（批处理）：

```text
Unity.exe -batchmode -nographics -projectPath <项目> -runTests -testPlatform EditMode -testResults <xml> -logFile <log>
Unity.exe -batchmode -nographics -projectPath <项目> -runTests -testPlatform PlayMode -testResults <xml> -logFile <log>
```

注意：`-runTests` **不要配 `-quit`**——`TestStarter.Init()` 在编辑器仍处于编译状态时会直接返回，
`-quit` 会把进程带走导致一个测试都不跑；测试跑完框架会自己退出。
