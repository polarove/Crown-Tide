using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>仅为当前胶囊演示配置可调整的近战样本，不代表正式平衡值。</summary>
public static class CreateCombatDemoAssets
{
    public const string GraphPath = "Assets/Scripts/Entity/Data/Weapon/Demo/Combo_DemoMelee.asset";

    public static WeaponComboGraph EnsureGraph()
    {
        var graph = AssetDatabase.LoadAssetAtPath<WeaponComboGraph>(GraphPath);
        if (graph != null) return graph; // 已有样本不覆盖人工调整。
        const string folder = "Assets/Scripts/Entity/Data/Weapon/Demo";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Scripts/Entity/Data/Weapon", "Demo");
        graph = ScriptableObject.CreateInstance<WeaponComboGraph>();
        graph.ComboEntries = new[]
        {
            new ComboEntry
            {
                Name = "演示近战（临时测试值）", Windup = .15f, Hit = .1f, Recovery = .3f, NextEntry = -1,
                MeleeDamage = 3f, HitShape = EnumMeleeHitShape.Sector, HitRadius = 2.5f,
                HitAngle = 140f, HitHeight = 2f, HitOffset = Vector3.up, HitLayers = ~0,
            },
        };
        AssetDatabase.CreateAsset(graph, GraphPath);
        return graph;
    }

    /// <summary>仅装配指定的两名演示实体，不重建场景、材质、相机或输入。</summary>
    public static void ConfigureEntities(GameObject player, GameObject enemy)
    {
        WeaponComboGraph graph = EnsureGraph();
        ConfigureActor(player, graph, faction: 1);
        ConfigureActor(enemy, graph, faction: 2);
        AITreeInputSource ai = enemy.GetComponent<AITreeInputSource>();
        ai.Target = player.transform;
        ai.EnableMeleeAttack = true;
        ai.MeleeAttackRange = 1.5f;
        EditorUtility.SetDirty(ai);
    }

    private static void ConfigureActor(GameObject actor, WeaponComboGraph graph, int faction)
    {
        CharacterSlotContainer slots = actor.GetComponent<CharacterSlotContainer>();
        slots.UnarmedComboGraph = graph;
        EditorUtility.SetDirty(slots);
        Entity entity = actor.GetComponent<Entity>();
        if (entity.Config == null) throw new System.InvalidOperationException(actor.name + " 缺少角色配置");
        entity.Config.FactionId = faction;
        entity.Config.FaithGainPerEnemyHit = 5;
        entity.Config.FaithGainPerEnemyKill = 10;
        EditorUtility.SetDirty(entity.Config);
    }

    [MenuItem("Crown Tide/配置 SampleScene 近战演示（临时测试数值）")]
    public static void ConfigureSampleScene()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("请先停止 Play，再配置场景");
        const string path = "Assets/Scenes/SampleScene.unity";
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
        bool openedHere = !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            GameObject? player = null, enemy = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Player") player = root;
                if (root.name == "Enemy") enemy = root;
            }
            if (player == null || enemy == null) throw new System.InvalidOperationException("缺少 Player／Enemy");
            ConfigureEntities(player, enemy);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[近战演示] 配置完成：伤害3、命中信心+5、击杀额外+10；临时测试数值，可在配置中调整。");
        }
        finally
        {
            if (openedHere) EditorSceneManager.CloseScene(scene, removeScene: true);
        }
    }
}
