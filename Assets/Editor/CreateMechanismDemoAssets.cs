using Assets.Scripts.Entity.Data.Skill;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>设定验证用占位配置；复用普通近战，没有角色专属招式。</summary>
public static class CreateMechanismDemoAssets
{
    public const string Folder = "Assets/Scripts/Entity/Data/Skill/Demo";
    public const string CrownPath = Folder + "/Skill_TestCrown.asset";
    public const string TidePath = Folder + "/Skill_TestTide.asset";
    public const string StealPath = Folder + "/Effect_TestCrownLifeSteal.asset";

    public static void Configure(Scene scene, GameObject player, GameObject enemy)
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Scripts/Entity/Data/Skill", "Demo");
        ModifierEffect steal = AssetDatabase.LoadAssetAtPath<ModifierEffect>(StealPath);
        if (steal == null)
        {
            steal = ScriptableObject.CreateInstance<ModifierEffect>();
            steal.Name = "测试占位：潮汐后冠冕吸血";
            steal.Category = EnumModifierCategory.Buff;
            steal.Duration = 20f;
            steal.CrownLifeStealRatio = 1f;
            AssetDatabase.CreateAsset(steal, StealPath);
        }
        SkillSO crown = EnsureSkill(CrownPath, EnumSkillType.Crown, 15f, 30f);
        SkillSO tide = EnsureSkill(TidePath, EnumSkillType.Tide, 30f, 60f);
        if (tide.AfterTideEffects.Length == 0)
        {
            tide.AfterTideEffects = new[] { steal };
            EditorUtility.SetDirty(tide);
        }
        foreach (GameObject actor in new[] { player, enemy })
        {
            if (actor.GetComponent<EntityCombatVisual>() == null) actor.AddComponent<EntityCombatVisual>();
            CharacterSlotContainer slots = actor.GetComponent<CharacterSlotContainer>();
            slots.Skills.Crown = crown;
            slots.Skills.Tide = tide;
            EditorUtility.SetDirty(slots);
        }
        // 延长可观察的循环；不改变受击伤害、信心门槛或玩家体质。
        Entity enemyEntity = enemy.GetComponent<Entity>();
        AITreeInputSource enemyAi = enemy.GetComponent<AITreeInputSource>();
        enemyAi.MeleeAttackInterval = 1.5f; // 临时出招节奏，让命中与受击不会按近乎相同频率抵消。
        EditorUtility.SetDirty(enemyAi);
        enemyEntity.Config!.MaxHealth = 200f;
        EditorUtility.SetDirty(enemyEntity.Config);
        GameObject? host = null;
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == "Mechanism Demo") host = root;
        if (host == null)
        {
            host = new GameObject("Mechanism Demo");
            SceneManager.MoveGameObjectToScene(host, scene);
        }
        DemoLoopReset reset = host.GetComponent<DemoLoopReset>() ?? host.AddComponent<DemoLoopReset>();
        reset.Enemy = enemyEntity;
        if (host.GetComponent<DemoLoopInput>() == null) host.AddComponent<DemoLoopInput>();
        EditorUtility.SetDirty(reset);
        ConfigureInput();
    }

    private static SkillSO EnsureSkill(string path, EnumSkillType kind, float normalDamage, float burstDamage)
    {
        SkillSO skill = AssetDatabase.LoadAssetAtPath<SkillSO>(path);
        if (skill != null) return skill; // 保留后续人工调参。
        skill = ScriptableObject.CreateInstance<SkillSO>();
        skill.Name = "测试占位：" + (kind == EnumSkillType.Crown ? "冠冕" : "潮汐");
        skill.Kind = kind;
        skill.FaithThreshold = 33;
        skill.Cooldown = 1f;
        skill.HasAttack = true;
        skill.UseBurstAttack = true;
        ComboEntry entry = CreateCombatDemoAssets.EnsureGraph().ComboEntries[0];
        entry.Name = skill.Name;
        entry.MeleeDamage = normalDamage;
        skill.Attack = entry;
        entry.Name += "（强化）";
        entry.MeleeDamage = burstDamage;
        skill.BurstAttack = entry;
        AssetDatabase.CreateAsset(skill, path);
        return skill;
    }

    private static void ConfigureInput()
    {
        const string path = "Assets/Input/PlayerControls.inputactions";
        InputActionAsset input = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
        InputActionMap map = input.FindActionMap("Player", true);
        bool changed = AddAction(map, "CrownSkill", "<Keyboard>/q", "<Gamepad>/rightShoulder");
        changed |= AddAction(map, "TideSkill", "<Keyboard>/e", "<Gamepad>/buttonNorth");
        if (changed)
        {
            System.IO.File.WriteAllText(path, input.ToJson());
            AssetDatabase.ImportAsset(path);
        }
    }

    private static bool AddAction(InputActionMap map, string name, string keyboard, string gamepad)
    {
        if (map.FindAction(name) != null) return false;
        InputAction action = map.AddAction(name, InputActionType.Button);
        action.AddBinding(keyboard, groups: "Keyboard&Mouse");
        action.AddBinding(gamepad, groups: "Gamepad");
        return true;
    }

    [MenuItem("Crown Tide/配置 SampleScene 设定验证（测试占位）")]
    public static void ConfigureSampleScene()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("先停止 Play 再配置");
        const string path = "Assets/Scenes/SampleScene.unity";
        Scene scene = SceneManager.GetSceneByPath(path);
        bool openedHere = !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            GameObject[] roots = scene.GetRootGameObjects();
            GameObject player = System.Array.Find(roots, root => root.name == "Player");
            GameObject enemy = System.Array.Find(roots, root => root.name == "Enemy");
            if (player == null || enemy == null) throw new System.InvalidOperationException("缺少 Player／Enemy");
            Configure(scene, player, enemy);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }
        finally
        {
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
        }
    }
}
