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
        SkillSO fist = EnsureWeaponSkill(Assets.Scripts.Entity.Data.Weapon.EnumWeaponType.Fist);
        WeaponSO stick = EnsureWoodenStick();
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
            slots.UnarmedWeaponSkill = fist;
            slots.Skills.SetWeaponSkill(fist);
            EditorUtility.SetDirty(slots);
        }
        // 延长可观察的循环；不改变受击伤害、信心门槛或玩家体质。
        Entity enemyEntity = enemy.GetComponent<Entity>();
        CharacterSlotContainer enemySlots = enemy.GetComponent<CharacterSlotContainer>();
        enemySlots.TryEquipWeapon(stick, Assets.Scripts.Entity.Data.EnumHandSlotType.MainHand,
            enemyEntity.Config != null ? enemyEntity.Config.WeaponCapacity : 5);
        SkillSO? equipped = enemySlots.Weapons.MainHand != null ? enemySlots.Weapons.MainHand.SpecialSkill : null;
        enemySlots.Skills.SetWeaponSkill(equipped != null ? equipped : fist);
        EditorUtility.SetDirty(enemySlots);
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
        DemoLoopReset reset = host.GetComponent<DemoLoopReset>();
        if (reset == null) reset = host.AddComponent<DemoLoopReset>();
        reset.Enemy = enemyEntity;
        if (host.GetComponent<DemoLoopInput>() == null) host.AddComponent<DemoLoopInput>();
        EditorUtility.SetDirty(reset);
        ConfigureInput();
    }

    private static SkillSO EnsureWeaponSkill(Assets.Scripts.Entity.Data.Weapon.EnumWeaponType type)
    {
        string path = Folder + "/Skill_TestWeapon_" + type + ".asset";
        SkillSO skill = AssetDatabase.LoadAssetAtPath<SkillSO>(path);
        if (skill != null)
        {
            skill.PossessionOnKill = EnsurePossessionProfile();
            EditorUtility.SetDirty(skill);
            return skill;
        }
        skill = ScriptableObject.CreateInstance<SkillSO>();
        skill.Name = "测试武器技能：" + type;
        skill.Kind = EnumSkillType.Weapon;
        skill.Cooldown = 4f;
        skill.HasAttack = true;
        ComboEntry attack = CreateCombatDemoAssets.EnsureGraph().ComboEntries[0];
        attack.Name = skill.Name;
        attack.MeleeDamage = 10f;
        skill.Attack = attack;
        skill.PossessionOnKill = EnsurePossessionProfile();
        AssetDatabase.CreateAsset(skill, path);
        return skill;
    }

    private static PossessionProfileSO EnsurePossessionProfile()
    {
        string path = Folder + "/Possession_KillProfile.asset";
        PossessionProfileSO profile = AssetDatabase.LoadAssetAtPath<PossessionProfileSO>(path);
        if (profile != null) return profile;
        ModifierEffect carrier = EnsureMarker("Effect_Possessed", "被附身", 10f);
        ModifierEffect marker = EnsureMarker("Effect_SoulOut", "附身中", 0f);
        profile = ScriptableObject.CreateInstance<PossessionProfileSO>();
        profile.PossessedEffect = carrier;
        profile.SoulOutEffect = marker;
        AssetDatabase.CreateAsset(profile, path);
        return profile;
    }

    private static ModifierEffect EnsureMarker(string filename, string label, float duration)
    {
        string path = Folder + "/" + filename + ".asset";
        ModifierEffect effect = AssetDatabase.LoadAssetAtPath<ModifierEffect>(path);
        if (effect != null) return effect;
        effect = ScriptableObject.CreateInstance<ModifierEffect>();
        effect.Name = label;
        effect.Duration = duration;
        effect.Category = EnumModifierCategory.Buff;
        AssetDatabase.CreateAsset(effect, path);
        return effect;
    }

    private static WeaponSO EnsureWoodenStick()
    {
        string path = Folder + "/Weapon_TestWoodenStick.asset";
        WeaponSO weapon = AssetDatabase.LoadAssetAtPath<WeaponSO>(path);
        if (weapon != null) return weapon;
        weapon = ScriptableObject.CreateInstance<WeaponSO>();
        weapon.Name = "测试木棍";
        weapon.Type = Assets.Scripts.Entity.Data.Weapon.EnumWeaponType.WoodenStick;
        weapon.Cost = 1;
        weapon.ComboGraphSingle = CreateCombatDemoAssets.EnsureGraph();
        weapon.SpecialSkill = EnsureWeaponSkill(weapon.Type);
        AssetDatabase.CreateAsset(weapon, path);
        return weapon;
    }

    /// <summary>迁移当前场景与演示数据；不重建角色、不修改其他技能数值。</summary>
    [MenuItem("Crown Tide/迁移 V 武器技能")]
    public static void MigrateWeaponSkill()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("先停止 Play 再迁移");
        ConfigureSampleScene();
        foreach (string guid in AssetDatabase.FindAssets("t:WeaponSO", new[] { "Assets" }))
        {
            WeaponSO weapon = AssetDatabase.LoadAssetAtPath<WeaponSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (weapon == null || weapon.SpecialSkill != null) continue;
            weapon.SpecialSkill = EnsureWeaponSkill(weapon.Type);
            EditorUtility.SetDirty(weapon);
        }
        foreach (string path in new[]
        {
            "Assets/Scripts/Entity/Data/Armor/Demo/Possession_Possessed.asset",
            "Assets/Scripts/Entity/Data/Armor/Demo/Possession_SoulOut.asset",
            "Assets/Scripts/Entity/Data/Character/Demo/PossessionEffect.asset",
        }) AssetDatabase.DeleteAsset(path);
        AssetDatabase.SaveAssets();
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
        InputAction? old = map.FindAction("Possess");
        if (old != null)
        {
            old.Rename("WeaponSkill"); // 保留动作与绑定 GUID，旧自定义键位继续用于第三槽。
            changed = true;
        }
        changed |= AddAction(map, "WeaponSkill", "<Keyboard>/v", "<Gamepad>/leftShoulder");
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
