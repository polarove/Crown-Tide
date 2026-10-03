using Assets.Scripts.Entity.Data.Armor;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 护甲套装演示资产生成器（Editor 工具，不进运行时）：
/// 一键生成「一套护甲（3 件：头/胸/腿）+ 一档/二档效果」，用于在 Play 里肉眼验收套装功能。
/// 幂等：已存在的资产原样复用（不覆盖、不产生孤儿 .asset），可反复点击。
/// 缺第 4 件（足）是刻意的——留给你手动 Create 一件 ArmorSO 来验证「3 件 → 4 件」的档位跨跃。
/// </summary>
public static class CreateArmorSetDemoAssets
{
    private const string DemoFolder = "Assets/Scripts/Entity/Data/Armor/Demo";
    private const string SetPath = DemoFolder + "/ArmorSet_Demo.asset";
    private const string Tier2Path = DemoFolder + "/Modifier_DemoSet2.asset";
    private const string Tier4Path = DemoFolder + "/Modifier_DemoSet4.asset";

    private static readonly (string FileName, EnumArmorPart Part, string DisplayName)[] DemoPieces =
    {
        ("Armor_DemoHead.asset", EnumArmorPart.Head, "演示头盔"),
        ("Armor_DemoChest.asset", EnumArmorPart.Chest, "演示胸甲"),
        ("Armor_DemoLegs.asset", EnumArmorPart.Legs, "演示腿甲"),
    };

    /// <summary>演示角色配置存放处（场景装配器也要用）</summary>
    public const string DemoConfigFolder = "Assets/Scripts/Entity/Data/Character/Demo";
    public const string PlayerConfigPath = DemoConfigFolder + "/Config_DemoPlayer.asset";
    public const string EnemyConfigPath = DemoConfigFolder + "/Config_DemoEnemy.asset";

    [MenuItem("Crown Tide/生成护甲套装演示资产")]
    public static void Generate()
    {
        EnsureFolder(DemoFolder);

        // 1) 二件档：移速 ×1.15
        ModifierEffect tier2 = LoadOrCreate<ModifierEffect>(Tier2Path);
        tier2.Name = "演示套装·二件（移速 +15%）";
        tier2.Category = EnumModifierCategory.Buff | EnumModifierCategory.ArmorSet;
        tier2.Duration = 0f;                  // 永久：装备期间常驻，破套由 ArmorSetBonusList 摘除
        tier2.StatModifiers = new[]
        {
            new StatModifierEntry { Stat = EnumStatType.MoveSpeed, Multiplier = 1.15f },
        };

        // 2) 四件档：移速 ×1.05 + 受伤 ×0.8
        //    注意「逐档累加」：4 件时二件档与四件档相乘，实际移速 = 1.15 × 1.05 ≈ 1.21（不是替换）
        ModifierEffect tier4 = LoadOrCreate<ModifierEffect>(Tier4Path);
        tier4.Name = "演示套装·四件（移速再 +5%、受伤 -20%）";
        tier4.Category = EnumModifierCategory.Buff | EnumModifierCategory.ArmorSet;
        tier4.Duration = 0f;
        tier4.StatModifiers = new[]
        {
            new StatModifierEntry { Stat = EnumStatType.MoveSpeed, Multiplier = 1.05f },
            new StatModifierEntry { Stat = EnumStatType.DamageTaken, Multiplier = 0.8f },
        };

        // 3) 套装：档位表 = 2 件档 + 4 件档（穿满 4 件时两条同时生效）
        ArmorSetSO set = LoadOrCreate<ArmorSetSO>(SetPath);
        set.Name = "演示套装";
        set.Bonuses = new[]
        {
            new ArmorSetBonus { PieceCount = 2, Modifier = tier2 },
            new ArmorSetBonus { PieceCount = 4, Modifier = tier4 },
        };

        // 4) 三件护甲（头/胸/腿），都指向该套装
        foreach ((string fileName, EnumArmorPart part, string displayName) in DemoPieces)
        {
            ArmorSO piece = LoadOrCreate<ArmorSO>($"{DemoFolder}/{fileName}");
            piece.Name = displayName;
            piece.Part = part;
            piece.Set = set;
            EditorUtility.SetDirty(piece);
        }

        // 5) 演示角色配置（玩家 / 敌人数值；空 Config 会让 HUD 显示 float.MaxValue 血量）
        EnsureDemoConfigs();


        EditorUtility.SetDirty(tier2);
        EditorUtility.SetDirty(tier4);
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[演示资产] 已生成到 {DemoFolder} 与 {DemoConfigFolder}：\n"
            + "· ArmorSet_Demo（二件档：移速 ×1.15；四件档：移速 ×1.05、受伤 ×0.8）\n"
            + "· Armor_DemoHead / DemoChest / DemoLegs 三件护甲\n"
            + "· Config_DemoPlayer（100 血）/ Config_DemoEnemy（60 血）\n"
            + "用法：把三件护甲拖到 Player 的 Slots.Armor（头/胸/腿），Play 后看 HUD 的「护甲 …/4｜套装 …」；"
            + "再手动 Create 一件 Part=Feet 的 ArmorSO 并指向同一套装，拖进足部槽即可看到四件档叠加生效（移速 ≈ ×1.21）\n"
            + "场景装配：菜单 Crown Tide/装配 SampleScene 演示实体（玩家 + AI）");
    }

    /// <summary>生成/复用演示用角色配置（玩家与敌人各一份）。返回后可直接 Load 使用</summary>
    public static void EnsureDemoConfigs()
    {
        EnsureFolder(DemoConfigFolder);

        CharacterConfigSO player = LoadOrCreate<CharacterConfigSO>(PlayerConfigPath);
        player.MaxHealth = 100f;
        player.FaithCapacity = 67;
        player.WeaponCapacity = 5;
        EditorUtility.SetDirty(player);

        CharacterConfigSO enemy = LoadOrCreate<CharacterConfigSO>(EnemyConfigPath);
        enemy.MaxHealth = 60f;
        enemy.FaithCapacity = 67;
        enemy.WeaponCapacity = 5;
        EditorUtility.SetDirty(enemy);
    }

    /// <summary>取已有资产，没有就在 path 处新建（幂等：不覆盖已有内容，避免反复点击丢手工调整）</summary>
    internal static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null)
        {
            return asset;
        }

        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    /// <summary>确保文件夹存在（逐级创建 + AssetDatabase 刷新，避免 CreateAsset 到不存在的目录）</summary>
    internal static void EnsureFolder(string path)
    {
        AssetDatabase.Refresh();
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string[] segments = path.Split('/');
        string current = segments[0];   // "Assets"
        for (int i = 1; i < segments.Length; i++)
        {
            string next = current + "/" + segments[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, segments[i]);
            }
            current = next;
        }
    }
}
