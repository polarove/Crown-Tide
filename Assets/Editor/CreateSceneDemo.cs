using Assets.Scripts.Entity.Data;
using Assets.Scripts.Entity.Data.Armor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 场景演示实体装配器（Editor 工具，不进运行时）：把 SampleScene 里的旧架构残留物体
/// 换成新架构的玩家 + AI，并装上护甲套装演示装备。
///
/// 为什么用脚本而不是手改 YAML：场景文件是 31 个带 fileID 交叉引用的文档，手改极易再次写坏
/// （2026-10-02 修过一次被写坏的 GUIStyle 块）；用 Editor API 装配则由 Unity 自己产出合法 YAML。
///
/// 菜单两项：
/// · 「装配 SampleScene 演示实体（玩家 + AI）」——幂等重建玩家/敌人（重跑不产生重复物体），保存场景；
/// · 「校验 SampleScene 演示实体」——只读自检（批处理/CI 也可用，通过 -executeMethod 调 Verify）。
/// </summary>
public static class CreateSceneDemo
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string InputActionsPath = "Assets/Input/PlayerControls.inputactions";
    private const string ArmorFolder = "Assets/Scripts/Entity/Data/Armor/Demo";

    private static readonly Vector3 PlayerSpawn = new(0f, 0.1f, 0f);
    private static readonly Vector3 EnemySpawn = new(0f, 0.1f, 7f);

    /// <summary>旧架构/无关的 MonoBehaviour，装配时清掉（不在表里的一律留着，避免误删新人加的东西）</summary>
    private static readonly string[] StaleBehaviourTypes =
    {
        "PlayerController", "CameraFollow", "EntityController", "EntityBlackboard",
    };

    [MenuItem("Crown Tide/装配 SampleScene 演示实体（玩家 + AI）")]
    public static void BuildSceneDemo()
    {
        // 依赖：演示资产（护甲 + 套装 + 数值 + 角色配置）——没有就先生成，保证场景引用非空
        CreateArmorSetDemoAssets.Generate();

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        GameObject player = BuildPlayer();
        GameObject enemy = BuildEnemy(player.transform);
        CreateCombatDemoAssets.ConfigureEntities(player, enemy);
        PlaceOnPlatform(scene, player, enemy);
        // 附身资产先建好：下面给玩家调试槽接线要用（幂等）
        CreateArmorSetDemoAssets.EnsurePossessionEffects();
        // 相机各属实体：Main Camera 跟玩家、Enemy Camera 跟敌人——附身切换（V / LB）靠各自亮灭互换，不挪相机
        CameraRig playerCamera = ConfigureCameraRig("Main Camera", player);
        CameraRig enemyCamera = ConfigureCameraRig("Enemy Camera", enemy);
        ConfigurePlayerInput(player, isPlayer: true, playerCamera);
        ConfigurePlayerInput(enemy, isPlayer: false, enemyCamera);
        CreateMechanismDemoAssets.Configure(scene, player, enemy);

        // 附身不再需要场景级管理器：会话状态就是 buff——V / LB 时给目标挂「被附身」载体、
        // 给自己挂「灵魂出窍」标记，EntityBrain 感知 buff 换绑、Duration 到期自动换回
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log($"[场景装配] 完成：\n"
            + $"· 玩家「{player.name}」位置 {player.transform.position}（自己操控；已穿演示头盔+胸甲 → 二件档移速 ×1.15）\n"
            + $"· AI「{enemy.name}」位置 {enemy.transform.position}（AITreeInputSource 追击玩家；只戴头盔，1 件不激活档位）\n"
            + "· 相机各属实体：Main Camera 跟玩家、Enemy Camera 跟敌人（F1 切肩 / F2 切第一人称；"
            + "附身时旧相机熄灭、接管者的相机亮起，不挪相机）\n"
            + "· 附身（buff 驱动，无管理器）：V / LB 挂「被附身」buff 到目标 + 「灵魂出窍」到自己，"
            + "Duration 到期自动摘 buff = 自动换回；期间不能中途退出\n"
            + "操作：WASD 移动、Shift 加速、空格跳；调试键 F3~F11 见 PlayerInputSource 头注释");
    }

    [MenuItem("Crown Tide/校验 SampleScene 演示实体")]
    public static void VerifyInteractive()
    {
        // 菜单入口：只报告不退出（Verify 带 Exit 是给 CI 的 -executeMethod 用的，手动跑会关编辑器）
        Verify(exitEditor: false);
    }

    /// <summary>批处理/CI 入口（-executeMethod）：失败以退出码 1 结束进程</summary>
    public static void Verify()
    {
        Verify(exitEditor: true);
    }

    private static void Verify(bool exitEditor)
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int failures = 0;

        GameObject? player = FindInScene(scene, "Player");
        GameObject? enemy = FindInScene(scene, "Enemy");
        GameObject? camera = FindInScene(scene, "Main Camera");
        GameObject? enemyCamera = FindInScene(scene, "Enemy Camera");

        failures += CheckEntity(player, "Player", expectPlayerControlled: true, expectedArmorPieces: 2);
        failures += CheckEntity(enemy, "Enemy", expectPlayerControlled: false, expectedArmorPieces: 1);

        // 相机接线（各属实体：Main Camera 跟玩家、Enemy Camera 跟敌人——附身切换靠亮灭互换）
        CameraRig? rig = camera != null ? camera.GetComponent<CameraRig>() : null;
        if (rig == null || rig.FollowEntity != player?.GetComponent<Entity>())
        {
            Debug.LogError("[场景校验] Main Camera 的 CameraRig 未指向玩家 Entity（或组件缺失）");
            failures++;
        }
        CameraRig? enemyRig = enemyCamera != null ? enemyCamera.GetComponent<CameraRig>() : null;
        if (enemyRig == null || enemyRig.FollowEntity != enemy?.GetComponent<Entity>())
        {
            Debug.LogError("[场景校验] Enemy Camera 的 CameraRig 未指向敌人 Entity（或组件缺失）");
            failures++;
        }

        // 附身接线（buff 驱动，无需场景级管理器）：玩家调试槽要挂上两份附身资产，
        // 否则 V / LB 无从发起。资产由「生成护甲套装演示资产」创建
        PlayerInputSource? playerInput = player != null ? player.GetComponent<PlayerInputSource>() : null;
        if (playerInput == null || playerInput.DebugPossessionEffect == null
            || playerInput.DebugSoulOutEffect == null
            || playerInput.DebugPossessionEffect.PossessionRole != EnumPossessionRole.Possessed
            || playerInput.DebugSoulOutEffect.PossessionRole != EnumPossessionRole.SoulOut
            || playerInput.DebugPossessionEffect.Duration <= 0f || playerInput.DebugSoulOutEffect.Duration > 0f)
        {
            Debug.LogError("[场景校验] 玩家 PlayerInputSource 的 DebugPossessionEffect 未配（V / LB 附身不可用）");
            failures++;
        }

        // AI 感知接线
        AITreeInputSource? ai = enemy != null ? enemy.GetComponent<AITreeInputSource>() : null;
        if (ai == null || ai.Target != player?.transform)
        {
            Debug.LogError("[场景校验] Enemy 的 AITreeInputSource.Target 未指向玩家");
            failures++;
        }

        // 残渣检查：旧架构组件不该还在
        foreach (GameObject? go in new GameObject?[] { player, enemy, camera, enemyCamera })
        {
            if (go == null)
            {
                continue;
            }
            foreach (string stale in StaleBehaviourTypes)
            {
                var behaviour = go.GetComponent(stale);
                if (behaviour != null)
                {
                    Debug.LogError($"[场景校验] {go.name} 上仍有旧组件 {stale}");
                    failures++;
                }
            }
        }

        Debug.Log(failures == 0
            ? "[场景校验] 全部通过：玩家/AI 组件齐备、护甲件数正确、相机与 AI 接线正确、无旧组件残渣"
            : $"[场景校验] 失败 {failures} 项（见上方 LogError）");
        if (exitEditor)
        {
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }

    // ---- 装配 ----

    [MenuItem("Crown Tide/修复 SampleScene 平台出生位置")]
    public static void FixPlatformSpawns()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = !scene.isLoaded;
        if (openedHere)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        }
        try
        {
            GameObject player = FindInScene(scene, "Player")
                ?? throw new System.InvalidOperationException("SampleScene 缺少 Player");
            GameObject enemy = FindInScene(scene, "Enemy")
                ?? throw new System.InvalidOperationException("SampleScene 缺少 Enemy");
            PlaceOnPlatform(scene, player, enemy);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[平台出生修复] Player={player.transform.position}, Enemy={enemy.transform.position}");
        }
        finally
        {
            if (openedHere)
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }
    }

    private static void PlaceOnPlatform(UnityEngine.SceneManagement.Scene scene, params GameObject[] actors)
    {
        GameObject platform = FindInScene(scene, "Platform")
            ?? throw new System.InvalidOperationException("SampleScene 缺少 Platform");
        Collider ground = platform.GetComponent<Collider>();
        if (ground == null || !ground.enabled || ground.isTrigger)
        {
            throw new System.InvalidOperationException("Platform 需要启用的非 Trigger 碰撞体");
        }
        Physics.SyncTransforms();
        foreach (GameObject actor in actors)
        {
            CharacterController controller = actor.GetComponent<CharacterController>();
            Vector3 position = actor.transform.position;
            float bottom = actor.transform.TransformPoint(controller.center).y
                - controller.height * Mathf.Abs(actor.transform.lossyScale.y) / 2f;
            // 胶囊底端放到平台上方留少量间隙，避免出生时穿插后被推出平台底面。
            position.y += ground.bounds.max.y + 0.05f - bottom;
            actor.transform.position = position;
            EditorUtility.SetDirty(actor.transform);
        }
    }

    private static GameObject BuildPlayer()
    {
        GameObject player = NewEntityObject("Player", PlayerSpawn);

        SetSerialized(player.GetComponent<Entity>(), so =>
        {
            RequiredProperty(so, "config", "Config").objectReferenceValue = LoadConfig(CreateArmorSetDemoAssets.PlayerConfigPath);
            RequiredProperty(so, "startPlayerControlled").boolValue = true;   // 开局绑玩家输入源（此后控制状态以 Brain.InputSource 为准）
        });
        ConfigureMotor(player.GetComponent<EntityMotor>(), walkSpeed: 5f);
        ConfigureArmor(player.GetComponent<CharacterSlotContainer>(), "Armor_DemoHead", "Armor_DemoChest");

        return player;
    }

    private static GameObject BuildEnemy(Transform playerTransform)
    {
        GameObject enemy = NewEntityObject("Enemy", EnemySpawn);

        SetSerialized(enemy.GetComponent<Entity>(), so =>
        {
            RequiredProperty(so, "config", "Config").objectReferenceValue = LoadConfig(CreateArmorSetDemoAssets.EnemyConfigPath);
            RequiredProperty(so, "startPlayerControlled").boolValue = false;   // 开局绑 AI 输入源（V / LB 可附身接管）
        });
        ConfigureMotor(enemy.GetComponent<EntityMotor>(), walkSpeed: 4f);   // 比玩家慢一点，追得上但不瞬移
        ConfigureArmor(enemy.GetComponent<CharacterSlotContainer>(), "Armor_DemoHead");   // 只 1 件：不激活档位

        // 感知：直接赋 Transform（正式感知系统后置；AI 追击决策消费它）
        AITreeInputSource aiSource = enemy.GetComponent<AITreeInputSource>();
        aiSource.Target = playerTransform;        EditorUtility.SetDirty(aiSource);

        return enemy;
    }

    /// <summary>建实体物体：清旧组件 → 建新组件（Entity 的 RequireComponent 会补齐依赖五件套）</summary>
    private static GameObject NewEntityObject(string name, Vector3 position)
    {
        Transform? existing = FindInScene(EditorSceneManager.GetActiveScene(), name)?.transform;
        GameObject go = existing != null ? existing.gameObject : new GameObject(name);

        go.name = name;
        go.transform.SetPositionAndRotation(position, Quaternion.identity);

        CleanStaleBehaviours(go);

        // 顺序：CharacterController 先行（Motor/Entity 都依赖它），其余由 RequireComponent 补齐
        if (go.GetComponent<CharacterController>() == null)
        {
            go.AddComponent<CharacterController>();
        }
        if (go.GetComponent<Entity>() == null)
        {
            go.AddComponent<Entity>();
        }
        EnsureComponent<EntityVisual>(go);          // Entity 的依赖里没有它（表现层单独挂）
        EnsureComponent<PlayerInput>(go);           // EntityBrain 要求（AI 实体也挂着，只是禁用）
        EnsureComponent<PlayerInputSource>(go);     // 玩家输入源
        EnsureComponent<AITreeInputSource>(go);     // AI 输入源（附身切换时两者都要求存在）

        return go;
    }

    private static void ConfigureMotor(EntityMotor motor, float walkSpeed)
    {
        motor.WalkSpeed = walkSpeed;
        motor.SprintMultiplier = 1.6f;
        motor.TurnSpeed = 10f;
        motor.JumpHeight = 1.2f;
        motor.Gravity = -20f;
        EditorUtility.SetDirty(motor);
    }


    /// <summary>玩家输入源接线：视角基准（本实体的相机）、输入资源；瞄准/加速动作由 PlayerInput 的 Actions 提供。
    /// 敌人也接（附身时 ViewTransform/FallbackActions 已就位）</summary>
    private static void ConfigurePlayerInput(GameObject go, bool isPlayer, CameraRig rig)
    {
        PlayerInputSource inputSource = go.GetComponent<PlayerInputSource>();
        inputSource.ViewTransform = rig.transform;   // 视角基准 = 本实体自己的相机（附身后移动投影随相机走）
        inputSource.FallbackActions = LoadInputActions();
        if (isPlayer)
        {
            // 附身调试槽（buff 驱动，无需场景级管理器）：V / LB 用它们挂 buff
            inputSource.DebugPossessionEffect =
                AssetDatabase.LoadAssetAtPath<PossessionEffect>(CreateArmorSetDemoAssets.PossessedEffectPath);
            inputSource.DebugSoulOutEffect =
                AssetDatabase.LoadAssetAtPath<PossessionEffect>(CreateArmorSetDemoAssets.SoulOutEffectPath);
        }
        EditorUtility.SetDirty(inputSource);

        // AI 实体被附身后也必须具有完整 Actions、消息回调与默认动作表。
        PlayerInput playerInput = go.GetComponent<PlayerInput>();
        InputActionAsset? actions = LoadInputActions();
        if (actions != null)
        {
            playerInput.actions = actions;
        }
        playerInput.notificationBehavior = PlayerNotifications.SendMessages;   // PlayerInputSource 的 OnMove/OnJump/OnAttack 回调依赖它
        // defaultMap 不是公开属性（只有序列化字段 m_DefaultActionMap），用 SerializedObject 落值
        SetSerialized(playerInput, so => RequiredProperty(so, "m_DefaultActionMap").stringValue = "Player");
        EditorUtility.SetDirty(playerInput);
    }

    /// <summary>装上演示护甲件（按 assetNames 顺序取，件数即套装有效件数）</summary>
    private static void ConfigureArmor(CharacterSlotContainer slots, params string[] assetNames)
    {
        slots.Armor.Unequip(EnumArmorPart.Head);
        slots.Armor.Unequip(EnumArmorPart.Chest);
        slots.Armor.Unequip(EnumArmorPart.Legs);
        slots.Armor.Unequip(EnumArmorPart.Feet);
        foreach (string assetName in assetNames)
        {
            ArmorSO piece = AssetDatabase.LoadAssetAtPath<ArmorSO>($"{ArmorFolder}/{assetName}.asset");
            if (piece == null)
            {
                Debug.LogWarning($"[场景装配] 找不到演示护甲 {assetName}（先跑「生成护甲套装演示资产」）");
                continue;
            }
            slots.Armor.Equip(piece);
        }
        EditorUtility.SetDirty(slots);
    }

    /// <summary>相机接线（幂等）：场景里找同名相机，没有就新建（Camera + AudioListener）。
    /// 相机属于实体——Main Camera 跟玩家、Enemy Camera 跟敌人；附身切换靠各自亮灭，不重指跟随</summary>
    private static CameraRig ConfigureCameraRig(string cameraName, GameObject follow)
    {
        GameObject? cameraObject = FindInScene(EditorSceneManager.GetActiveScene(), cameraName);
        if (cameraObject == null)
        {
            cameraObject = new GameObject(cameraName);
        }

        CleanStaleBehaviours(cameraObject);   // 旧 CameraFollow 清掉
        EnsureComponent<Camera>(cameraObject);
        EnsureComponent<AudioListener>(cameraObject);   // 与 Camera 同开同关（CameraRig 亮灭感知管）
        CameraRig rig = EnsureComponent<CameraRig>(cameraObject);
        rig.FollowEntity = follow.GetComponent<Entity>();
        rig.InputActionAsset = LoadInputActions();
        rig.LockAndHideCursor = true;
        EditorUtility.SetDirty(rig);
        return rig;
    }

    // ---- 工具 ----

    private static T EnsureComponent<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        return component != null ? component : go.AddComponent<T>();
    }

    /// <summary>清掉表内旧架构组件（在编辑器里直接销毁，不进 Undo——装配是幂等脚本操作），
    /// 再清掉 Missing Script：旧架构的类已被删除，GetComponent(string) 根本看不见它们，
    /// 只有 Unity 的专用接口能摘（否则场景里会一直挂着红字空组件）</summary>
    private static void CleanStaleBehaviours(GameObject go)
    {
        foreach (string typeName in StaleBehaviourTypes)
        {
            Component stale = go.GetComponent(typeName);
            if (stale != null)
            {
                Debug.Log($"[场景装配] 移除 {go.name} 上的旧组件 {stale.GetType().Name}");
                Object.DestroyImmediate(stale, allowDestroyingAssets: false);
            }
        }

        int removedMissing = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
        if (removedMissing > 0)
        {
            Debug.Log($"[场景装配] 移除 {go.name} 上 {removedMissing} 个 Missing Script 组件");
        }
    }

    private static GameObject? FindInScene(UnityEngine.SceneManagement.Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == name)
            {
                return root;
            }
        }
        return null;
    }

    private static CharacterConfigSO? LoadConfig(string path)
    {
        CharacterConfigSO? config = AssetDatabase.LoadAssetAtPath<CharacterConfigSO>(path);
        if (config == null)
        {
            Debug.LogWarning($"[场景装配] 找不到角色配置 {path}");
        }
        return config;
    }

    private static InputActionAsset? LoadInputActions()
    {
        return AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
    }

    /// <summary>改私有 [SerializeField] 字段的唯一可靠途径（属性 setter 依赖运行时 Brain，编辑期不生效）</summary>
    private static void SetSerialized(Component component, System.Action<SerializedObject> mutate)
    {
        using (var so = new SerializedObjectScope(component))
        {
            mutate(so.Object);
        }
    }

    /// <summary>取序列化字段（按别名依次尝试，兼容 camelCase/PascalCase 改名）；找不到就报错并抛，
    /// 防"字段改名后静默不落值"（本项目刚做过一轮命名统一，这类坑必须炸出来而不是吞掉）</summary>
    private static SerializedProperty RequiredProperty(SerializedObject so, params string[] names)
    {
        foreach (string name in names)
        {
            SerializedProperty property = so.FindProperty(name);
            if (property != null)
            {
                return property;
            }
        }
        throw new System.MissingFieldException(
            $"{so.targetObject.GetType().Name} 上找不到序列化字段 {string.Join("/", names)}——字段可能已改名，请同步本装配器");
    }

    private sealed class SerializedObjectScope : System.IDisposable
    {
        private readonly SerializedObject Serialized;

        public SerializedObjectScope(Object target)
        {
            Serialized = new SerializedObject(target);
        }

        public SerializedObject Object => Serialized;

        public void Dispose()
        {
            Serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    // ---- 校验 ----

    private static int CheckEntity(GameObject? go, string label, bool expectPlayerControlled, int expectedArmorPieces)
    {
        int failures = 0;
        if (go == null)
        {
            Debug.LogError($"[场景校验] 场景里没有 {label}");
            return 1;
        }

        Entity entity = go.GetComponent<Entity>();
        if (entity == null)
        {
            Debug.LogError($"[场景校验] {label} 缺 Entity 组件");
            return 1;
        }

        string[] required =
        {
            "CharacterController", nameof(EntityBrain), nameof(EntityMotor), nameof(CharacterVitals),
            nameof(CharacterSlotContainer), nameof(EntityVisual), nameof(PlayerInputSource), nameof(AITreeInputSource),
        };
        foreach (string typeName in required)
        {
            if (go.GetComponent(typeName) == null)
            {
                Debug.LogError($"[场景校验] {label} 缺组件 {typeName}");
                failures++;
            }
        }

        // 玩家/AI 归属（私有序列化字段，读 SerializedObject）
        using (var so = new SerializedObjectScope(entity))
        {
            bool isPlayerControlled = RequiredProperty(so.Object, "startPlayerControlled").boolValue;
            if (isPlayerControlled != expectPlayerControlled)
            {
                Debug.LogError($"[场景校验] {label} startPlayerControlled={isPlayerControlled}，期望 {expectPlayerControlled}");
                failures++;
            }
            if (RequiredProperty(so.Object, "config", "Config").objectReferenceValue == null)
            {
                Debug.LogError($"[场景校验] {label} 未配 CharacterConfigSO（HUD 会显示 float.MaxValue 血量）");
                failures++;
            }
        }

        // 护甲件数（= 套装有效件数）。
        // 注意：Entity.Slots 是 Entity.Awake 在运行时才注入的只读门面，编辑期不跑 Awake——
        // 校验必须直接 GetComponent，不能走 entity.Slots（否则空引用）
        CharacterSlotContainer slotContainer = go.GetComponent<CharacterSlotContainer>();
        ArmorSlot armor = slotContainer.Armor;
        if (armor.EquippedCount != expectedArmorPieces)
        {
            Debug.LogError($"[场景校验] {label} 护甲件数 {armor.EquippedCount}，期望 {expectedArmorPieces}");
            failures++;
        }
        foreach (ArmorSO? piece in new[] { armor.Head, armor.Chest, armor.Legs, armor.Feet })
        {
            if (piece != null && piece.Set == null)
            {
                Debug.LogError($"[场景校验] {label} 身上的 {piece.Name} 未指向套装（不参与套装计数）");
                failures++;
            }
        }

        return failures;
    }
}
