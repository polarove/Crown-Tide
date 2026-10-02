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
        ConfigureCameraRig(player);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log($"[场景装配] 完成：\n"
            + $"· 玩家「{player.name}」位置 {player.transform.position}（自己操控；已穿演示头盔+胸甲 → 二件档移速 ×1.15）\n"
            + $"· AI「{enemy.name}」位置 {enemy.transform.position}（AITreeInputSource 追击玩家；只戴头盔，1 件不激活档位）\n"
            + "· Main Camera 已挂 CameraRig 跟随玩家（F1 切肩 / F2 切第一人称，走 PlayerControls.inputactions）\n"
            + "操作：WASD 移动、Shift 加速、空格跳；调试键 F3~F11 见 PlayerInputSource 头注释");
    }

    [MenuItem("Crown Tide/校验 SampleScene 演示实体")]
    public static void Verify()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int failures = 0;

        GameObject? player = FindInScene(scene, "Player");
        GameObject? enemy = FindInScene(scene, "Enemy");
        GameObject? camera = FindInScene(scene, "Main Camera");

        failures += CheckEntity(player, "Player", expectPlayerControlled: true, expectedArmorPieces: 2);
        failures += CheckEntity(enemy, "Enemy", expectPlayerControlled: false, expectedArmorPieces: 1);

        // 相机跟随接线
        CameraRig? rig = camera != null ? camera.GetComponent<CameraRig>() : null;
        if (rig == null || rig.FollowEntity != player?.GetComponent<Entity>())
        {
            Debug.LogError("[场景校验] Main Camera 的 CameraRig 未指向玩家 Entity（或组件缺失）");
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
        foreach (GameObject? go in new GameObject?[] { player, enemy, camera })
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
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    // ---- 装配 ----

    private static GameObject BuildPlayer()
    {
        GameObject player = NewEntityObject("Player", PlayerSpawn);

        SetSerialized(player.GetComponent<Entity>(), so =>
        {
            RequiredProperty(so, "config", "Config").objectReferenceValue = LoadConfig(CreateArmorSetDemoAssets.PlayerConfigPath);
            RequiredProperty(so, "isPlayerControlled").boolValue = true;   // 玩家操控：Brain 绑 PlayerInputSource
        });
        ConfigureMotor(player.GetComponent<EntityMotor>(), walkSpeed: 5f);
        ConfigureVisual(player.GetComponent<EntityVisual>(), showDebugHud: true);
        ConfigureArmor(player.GetComponent<CharacterSlotContainer>(), "Armor_DemoHead", "Armor_DemoChest");
        ConfigurePlayerInput(player, isPlayer: true);

        return player;
    }

    private static GameObject BuildEnemy(Transform playerTransform)
    {
        GameObject enemy = NewEntityObject("Enemy", EnemySpawn);

        SetSerialized(enemy.GetComponent<Entity>(), so =>
        {
            RequiredProperty(so, "config", "Config").objectReferenceValue = LoadConfig(CreateArmorSetDemoAssets.EnemyConfigPath);
            RequiredProperty(so, "isPlayerControlled").boolValue = false;   // AI 操控：Brain 绑 AITreeInputSource
        });
        ConfigureMotor(enemy.GetComponent<EntityMotor>(), walkSpeed: 4f);   // 比玩家慢一点，追得上但不瞬移
        ConfigureVisual(enemy.GetComponent<EntityVisual>(), showDebugHud: false);   // 靠玩家 HUD 看即可，少挡画面
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

    private static void ConfigureVisual(EntityVisual visual, bool showDebugHud)
    {
        visual.ShowDebugHud = showDebugHud;
        EditorUtility.SetDirty(visual);
    }

    /// <summary>玩家输入源接线：视角基准（相机）、输入资源；瞄准/加速动作由 PlayerInput 的 Actions 提供</summary>
    private static void ConfigurePlayerInput(GameObject go, bool isPlayer)
    {
        PlayerInputSource inputSource = go.GetComponent<PlayerInputSource>();
        inputSource.ViewTransform = Camera.main != null ? Camera.main.transform : null;
        inputSource.FallbackActions = LoadInputActions();
        EditorUtility.SetDirty(inputSource);

        if (!isPlayer)
        {
            return;   // AI 实体不需要 PlayerInput 的资源（组件为满足 RequireComponent 存在即可）
        }

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

    private static void ConfigureCameraRig(GameObject player)
    {
        GameObject? cameraObject = FindInScene(EditorSceneManager.GetActiveScene(), "Main Camera");
        if (cameraObject == null)
        {
            Debug.LogWarning("[场景装配] 场景里没有 Main Camera，跳过相机跟随接线");
            return;
        }

        CleanStaleBehaviours(cameraObject);   // 旧 CameraFollow 清掉
        CameraRig rig = EnsureComponent<CameraRig>(cameraObject);
        rig.FollowEntity = player.GetComponent<Entity>();
        rig.InputActionAsset = LoadInputActions();
        rig.LockAndHideCursor = true;
        EditorUtility.SetDirty(rig);
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
            bool isPlayerControlled = RequiredProperty(so.Object, "isPlayerControlled").boolValue;
            if (isPlayerControlled != expectPlayerControlled)
            {
                Debug.LogError($"[场景校验] {label} IsPlayerControlled={isPlayerControlled}，期望 {expectPlayerControlled}");
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
