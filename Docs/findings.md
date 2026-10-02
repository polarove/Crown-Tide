# 发现与决策：附身模块

> 由Codex记录，2026-10-02。详细实现与验证见 [附身实现与验证](附身实现与验证.md)。
> 开发者：CCvTDD；Agent：Codex。

## 2026-10-02：非代码配置整理

开发者：CCvTDD；Agent：Codex。

现有玩家／敌人配置均为 FaithCapacity=67、FaithLossPerHit=5；当前没有冠冕／潮汐 SkillSO 资产。非代码阶段不能将旧 Faith 字段改填 33，或写入尚无序列化支持的阈值／强化字段。配置目标与后续验证要求已独立保存为 `信心与技能配置规范.md`，待代码支持后再通过 Unity 配置。附身交接仍有一处 F10 旧说明，本轮改为 V／LB；历史日志中的 F10 保留为历史事实。

## 2026-10-02：最新机制对照审查

开发者：CCvTDD；Agent：Codex。

重新读取设计与代码后，核心差距仍是旧钟摆技能语义：0 信心可释放、技能推动信心、满值锁向，和新阈值／强化／归零规则相反。ICrownEvent／ITideEvent、命中扣血、技能执行及吸血尚未实现；武器套装关联亦无配置。附身双 Buff、门禁、输入往返与护甲累加结构可保留。Buff／Debuff 事件频率、是否包括永久标记、满值自动或手动爆发、吸血参数仍需明确。详细当前对照见问题清单开头；不把旧测试通过当成新机制验收。

## 2026-10-02：附身返回后的视角输入故障

开发者：CCvTDD；Agent：Codex。

新增模拟鼠标／手柄输入回归测试，修改前 0/2 通过：均在第一次返回时无法转向，附身前正常。CameraRig 原先直接引用与角色共享的 InputActionAsset；PlayerInput 解绑会关闭动作表，而相机仅停止渲染，返回不会再次 OnEnable，导致移动恢复而视角输入停用。每台相机现在持有独立动作实例，并只读所属 PlayerInput 的设备范围；未激活的相机不响应切肩。

只拆开动作仍有失败：PlayerInput 重新启用会自动重新选择设备，原设备配对丢失。PlayerInputSource 因此在停用前保存设备与控制方案，启用后恢复仍在线的原设备；设备已经移除时不强制恢复。最终 PlayMode 41/41 通过，连续两次返回后鼠标／右摇杆转向、切肩、第一／第三人称切换及设备归属均通过；未进行物理手柄人工体验。

交接边界：本次修复涉及 `CameraRig.cs`、`PlayerInputSource.cs` 与新增 `PossessionCameraInputTests.cs`；没有借此改动场景物体或附身玩法规则。最新证据是 `camera-input-after.json`，此前 Debug 的 39/39 是历史阶段结果。日志与测试结果文件在本地忽略目录，其他开发者需自行运行测试；用户尚未确认人工试玩结果。

## 2026-10-02 补充：棋盘格地板与截图报错

开发者：CCvTDD；Agent：Codex。

- 平台与角色原先引用同一份默认材质；为避免改变胶囊体外观，创建独立 `PlatformChecker.mat` 和重复纹理 `PlatformChecker.png`，只替换平台 Renderer 的材质。每格约 2 米，尺寸与碰撞边界保持原样。
- `capture_game_view` 的 `No Game view render target to capture` 表示截图来源没有可用渲染目标。本次改用 `source=camera` 指定 Main Camera 后成功，已检查 `Logs/PossessionValidation/checker-floor.png`。
- Console 中可能仍保留先前的红色错误记录；这是已处理的截图工具错误，不能据此判断游戏运行故障。用户可以点 Clear 清理历史显示，本轮没有代替用户清空 Console。
- 本次记录重新核对 Debug 最终结果为 completed、39/39 通过，三张截图文件存在；未重新执行测试。证据在本地忽略目录，不会自动随 Git 传给其他开发者。

## 2026-10-02 补充：演示场景出生位置

开发者：CCvTDD；Agent：Codex。

装配器原来将角色中心放在 Y=0.1，胶囊底端进入平台内部，会出现向下脱离碰撞体的情况。现在按平台顶面与 CharacterController 半高计算出生位置；当前双方中心为 Y=1.55。新增保存场景落地回归测试，和已有附身场景测试一起通过（2/2）。这次检查覆盖实际重力与 AI 追击，不代表平台边缘寻路或多人设备配对已经实现。

## 已确认需求

### 2026-10-02：附身输入与 Debug 面板检查

开发者：CCvTDD；Agent：Codex。

原附身是 PlayerInputSource 直读 F10，未进入 Input Actions，手柄没有绑定。EntityVisual 使用固定左上角 OnGUI 矩形，且装配器只打开玩家 HUD。改为 Possess 动作统一提交指令，所有实体通过 Debug System 统一开关显示头顶只读面板。Debug 模式默认关闭，暂定 F12 开关；附身作为玩法输入不受 Debug 模式限制。

实现采用屏幕叠加 Canvas，位置投影自胶囊体顶部，每帧只读刷新。字号按画面高度缩放，相邻面板避让并用连线指向自己的头顶；相机背后／视口外实体隐藏。Debug System 独立于实体控制权，切换附身或角色死亡不会丢失开关。F3～F7、F9、F11 调试操作只在 Debug 模式生效；V／LB 是正常玩法输入。手柄 View／Back（Input System select）也可切换 Debug。

- 遵循数据驱动；Input 提交意图，Brain 编排，Logic 读取／修改 Data，Data 不调用 Logic，Presentation 对玩法数据只读。
- 所有技能统一检查释放条件，持续效果由 Effect 管理，行为与互斥交给状态机。
- 附身是每个 Entity 固有的主动技能，不占冠冕／潮汐槽位。
- 默认被控制时不能释放；保留解控等技能的配置例外。
- 原角色交给 AI，目标交给玩家；结束后归还控制权。
- 目标限时“被附身”Buff、原角色无效果永久标记；目标单侧计时，持续期间可延长。
- 原角色死亡时返回视角并清理双方，不能继续正常操作死亡角色。
- 后续信心设定覆盖旧规则：事件增减信心，两类技能释放后均归零，先试玩再调整。本次未实现信心新规则。

## 修改前发现（历史快照，已修复项不能当成当前 bug）

| 原问题 | 本次处理 |
|---|---|
| 两种附身标记均绑定玩家输入，到期均切 AI | 通过双方关联区分身份，并恢复各自原输入。 |
| 出窍标记阻止原角色 AI 采集指令 | 限定目标玩家驱动的防重复输入判定。 |
| 无双方关联，结束只摘本侧效果 | 联动清理、归还；结束入口幂等。 |
| 发起附身未检查控制状态 | 共用 `Capability.CanUseSkill`。 |
| 原角色标记也计时 10 秒 | 配为永久，施加时按永久实例覆盖旧模板配置。 |
| 无真实死亡自动收尾，测试手动结束 | 订阅死亡、处理周期伤害中的延迟安全清理。 |
| 演示敌人输入未完整配置，装配保留旧护甲 | 配置 Actions、消息回调与默认表；装配先清槽。 |

## 实现位置

| 文件 | 职责 |
|---|---|
| `Assets/Scripts/Entity/EntityBrain.cs` | 每次双方关联、释放、延长、结束、死亡及生命周期收尾、视角归属。 |
| `Assets/Scripts/Entity/Logic/Modifier/ModifierList.cs` | 实例时长快照、永久覆盖、实例延长与遍历状态。 |
| `Assets/Scripts/Entity/Logic/EntityCapabilities.cs` | `CanUseSkill` 共用门禁与控制状态例外。 |
| `Assets/Scripts/Entity/Data/Skill/SkillSO.cs`、`Logic/Modifier/PossessionEffect.cs` | 显式配置被控制期间的释放例外。 |
| `Assets/Scripts/Entity/Input/CommandBuffer.cs`、`PlayerInputSource.cs` | F10 写请求，Brain 消费；切换清理旧输入。 |
| `Assets/Scripts/Entity/Input/AI/AITreeInputSource.cs` | 出窍角色仍可接受 AI 指令。 |
| `Assets/Scripts/Entity/Presentation/CameraRig.cs`、`EntityVisual.cs` | 只读相机与状态展示，死亡视角与操作分离。 |
| `Assets/Editor/CreateArmorSetDemoAssets.cs`、`CreateSceneDemo.cs` | 配置两种效果、演示输入／相机、场景生成与校验。 |
| `Assets/TestAssemblies/PlayMode/` | 附身与场景回归测试，补充 Input System 引用。 |

## 运行时原则

- `TryBeginPossession` 要求两个角色正确的纯标记效果；缺失／配错则拒绝，已有会话禁止嵌套。
- `ExtendPossession(seconds)` 只增加目标本次实例的剩余时间，不修改 SO。
- `EndPossession()` 可从任一侧调用，F10 不提供主动退出。
- 标记被移除、到期、死亡、禁用、销毁都使关联结束。
- 原角色死亡只保留返回视角；完整死亡 UI、复活、观战尚未实现。
- AI 可发起固有附身，但不会因此凭空获得本地玩家输入；未新增 AI 决策策略。

## 证据与边界

- 当前复核归档 XML：PlayMode 34/34；EditMode 10/10；场景校验日志通过。
- 最终 PlayMode 包含护甲 13、附身逻辑 20、保存场景往返 1。
- 两组附身共享模板的测试验证关联隔离，不等于实际多人网络或设备配对验收。
- 自动相机测试验证启用状态，不等于人工画面、镜头手感验收。
- 当前仍在 `dev`，有未提交改动。原有五份旧文档删除及新设定／指南文件是本次实现前已有状态，没有恢复或提交。

## 待确认设计

原角色 AI 是否使用冠冕／潮汐、吸血是否覆盖普攻、满值是否自动释放、附身驱散规则、友伤资源平衡及信心事件结算频率，仍见 [问题清单](玩法执行规则与待确认问题.md)。不得将建议直接当作用户定案。

## 资源

- 设定：`Docs/Crown Tide设计.md`。
- 实现交接：`Docs/附身实现与验证.md`。
- 验证证据：`Logs/PossessionValidation/{playmode.xml,editmode.xml,playmode.log,scene-verify.log}`；Logs 被 Git 忽略，仅本地保留。
- Unity：`C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe`。
- 场景：`Assets/Scenes/SampleScene.unity`；Play 后 F10，默认 10 秒返回。

## 2026-10-02｜游戏机制讨论：附身的技能定位与双 Buff 生命周期

开发者：CCvTDD；Agent：Codex（/root）。由Codex记录。

来源：本聊天用户对附身机制的描述及 Agent 的原理说明。本节记录设计，不代表本次已检查或修改实现；历史实现和测试结果保留原有时间与验证范围。

### 用户本次明确确定的机制

- 附身是每个角色固有的主动技能，与其他技能一样检查释放条件；不放在技能栏里，也不是被动技能。具体释放条件本次未列举。
- 附身开始后，用两个 Buff 管理持续过程，通过 Buff 计算持续时间和结束条件。
- 被附身者持有负责计时和结束条件的 Buff；计时只放在被附身者身上。
- 原本操控的角色持有一个无效果的永久 Buff，作为正在附身其他角色的状态标记，不另设倒计时。
- 多人环境下，通过对应 Buff 的有无，区分正常状态、正在附身其他角色、被附身中。
- 附身期间原角色交给 AI，敌怪目标交给玩家；倒计时结束后反向切换。

| 角色身上的附身 Buff | 表达的状态 | 是否负责计时 |
|---|---|---|
| 无对应 Buff | 正常状态（用户描述的“本人”） | 否 |
| 原角色的永久标记 Buff | 正在附身其他角色 | 否 |
| 目标的被附身 Buff | 被附身中 | 是 |

```text
主动释放附身 → 检查释放条件 → 双方获得对应 Buff
    ↓
玩家原角色：PlayerInputSource → AIInputSource
敌怪目标：  AIInputSource → PlayerInputSource
    ↓
目标身上的 Buff 倒计时并检查结束条件
    ↓
敌怪目标：  PlayerInputSource → AIInputSource
玩家原角色：AIInputSource → PlayerInputSource
```

### Agent 的原理解释与建议（记录不代表用户已定案）

- “永久”理解为没有自然到期时间；附身结束时仍应移除双方 Buff。无效果标记不改变属性，但承担状态标记职责。
- 目标单侧计时让持续时间只有一处负责，避免双方倒计时不一致。
- Buff 的有无可以区分状态，不能独自确认具体是谁在控制谁；多人关联建议记录原角色、目标、控制玩家及本次附身关联标识。“本人”若涉及玩家身份，还需检查控制权归属。
- 恢复双方输入源与清理双方 Buff 建议走同一结束流程；多人开始和结束建议由服务器统一确认并同步。
- 死亡、消失、驱散、断线、提前退出等具体结束规则及联机关联数据归属，本次未新增定案；此前文档中的已确定规则与待确认项仍按各自状态保留。

本聊天用于游戏机制原理讨论。本次仅保存讨论，不将联机建议标为已实现，也未重新验证此前实现记录。

## 2026-10-03｜新信心与技能代码核查

开发者：CCvTDD；Agent：Codex。

- 已确认满值需按键释放强化版本，不自动释放；归零前保存本次强化判定。
- 原逻辑是方向空间门禁与技能信心增量，已开始替换为阈值与成功归零。
- 当前没有命中检测与具体技能数值，事件接口与统一 Effect 接口不能宣称完整战斗已实现。
- 工具错误：首份补丁同一路径同时删除与新增被拒，未写入；改为直接更新内容。两处推测路径不存在，已用文件列表定位。记录补丁匹配标题失败，改为追加。

- 2026-10-03 验证：数据规则 EditMode 24/24。首轮 PlayMode 50/51；唯一失败是新增测试把护甲部位枚举误当作从 0 连续排列，触发 ArgumentOutOfRangeException，已改为显式 Head／Chest／Legs／Feet，待复测。
- 工具错误追加：套装补丁重复声明 EntityBrain 更新段而被拒，未写入；合并同文件更新段后成功。ModifierEffect 与枚举迁移保留原 .meta GUID。

- 最终核对：EditMode 24/24、PlayMode 51/51。套装按永久实例施加（覆盖模板的实例时长但不写模板），原脚本 GUID 保留。满值按键规则与已实现／未实现边界已同步全部当前说明。

## 2026-10-03｜交接证据复核

开发者：CCvTDD；Agent：Codex。由Codex记录。

- 归档 editmode.json 为 completed，24/24；playmode-final.json 为 completed，51/51，均无失败／跳过。此处是读取既有证据，不是新一轮测试。
- 已实现的是基础框架，场景尚不能完成战斗循环；正式技能与事件数值仍未配置。测试里的吸血比例、时长与命中收益仅为测试样本。
- 当前配置、实现与边界统一见 [信心与技能实现](信心与技能实现.md) 和 [配置规范](信心与技能配置规范.md)。后续接手应优先阅读这两份文件，旧机制对照保留为历史。
