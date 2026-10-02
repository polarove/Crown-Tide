using Assets.Scripts.Entity.Data.Armor;
using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// 护甲套装全链路测试（PlayMode，真 Entity）：装备变更 → 档位重算 → ModifierList 挂/摘 → 乘法链读数。
/// 档位语义（需求钦定，逐档累加）：2 件 → 二件档；**4 件 → 二件档 + 四件档同时生效**。
/// 这是本功能的冒烟验收：数值一旦被写成"替换"而不是"累加"，这里就会红。
/// </summary>
public sealed class ArmorSetRuntimeTests
{
    // 由 [UnitySetUp] 赋值（NUnit 不在构造期跑 SetUp，故用 null! 断言）
    private GameObject Host = null!;
    private Entity TestEntity = null!;
    private readonly List<ScriptableObject> Created = new();

    // 固定的乘数：超出浮点严格相等仍留容差
    private const float Epsilon = 1e-4f;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        Host = new GameObject("ArmorSetTestHost");
        TestEntity = Host.AddComponent<Entity>();   // RequireComponent 拉起 CharacterController/Brain/Motor/Vitals/Slots
        yield return null;                          // 等 Awake 跑完（Bootstrap 装配）

        // 兜底：极端情况下 Awake 未跑（不是预期路径），显式装配一次让测试可读失败而不是空引用
        if (TestEntity.Brain == null || TestEntity.Brain.Modifiers == null)
        {
            TestEntity.Brain!.Bootstrap(TestEntity);
        }

        Assert.IsNotNull(TestEntity.Brain.Modifiers, "Bootstrap 应装配 ModifierList");
        Assert.IsNotNull(TestEntity.Brain.ArmorSets, "Bootstrap 应装配 ArmorSetBonusList");
        // 后续装备测试验证变更事件的同步；Data 不再反向持有 Logic 引擎。
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (Host != null)
        {
            Object.Destroy(Host);
        }
        yield return null;

        foreach (ScriptableObject asset in Created)
        {
            Object.DestroyImmediate(asset);
        }
        Created.Clear();
    }

    // ---- 测试桩（不走资产数据库）----

    private T CreateAsset<T>() where T : ScriptableObject
    {
        T asset = ScriptableObject.CreateInstance<T>();
        Created.Add(asset);
        return asset;
    }

    private ModifierEffect CreateModifier(string name, params (EnumStatType Stat, float Multiplier)[] entries)
    {
        ModifierEffect effect = CreateAsset<ModifierEffect>();
        effect.Name = name;
        effect.Category = EnumModifierCategory.Buff | EnumModifierCategory.ArmorSet;
        effect.Duration = 0f;   // 永久：装备期间常驻，破套由 ArmorSetBonusList 摘除
        StatModifierEntry[] modifiers = new StatModifierEntry[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            modifiers[i] = new StatModifierEntry { Stat = entries[i].Stat, Multiplier = entries[i].Multiplier };
        }
        effect.StatModifiers = modifiers;
        return effect;
    }

    /// <summary>造套装；档位效果可空（= 该档只计数不生效）</summary>
    private ArmorSetSO CreateSet(string name, ModifierEffect? tier2, ModifierEffect? tier4)
    {
        ArmorSetSO set = CreateAsset<ArmorSetSO>();
        set.Name = name;
        set.Bonuses = new[]
        {
            new ArmorSetBonus { PieceCount = 2, Modifier = tier2! },
            new ArmorSetBonus { PieceCount = 4, Modifier = tier4! },
        };
        return set;
    }

    /// <summary>造一件护甲；set 可空（= 散件）</summary>
    private ArmorSO CreateArmor(string name, EnumArmorPart part, ArmorSetSO? set)
    {
        ArmorSO armor = CreateAsset<ArmorSO>();
        armor.Name = name;
        armor.Part = part;
        armor.Set = set;
        return armor;
    }

    /// <summary>穿一件指定部位的护甲，并断言调用返回成功。
    /// set 可空（= 散件）；expectedTier 可空（= 该件数不达标，不该激活任何档位）</summary>
    private void Equip(string name, EnumArmorPart part, ArmorSetSO? set, ModifierEffect? expectedTier)
    {
        ArmorSO piece = CreateArmor(name, part, set);
        Assert.AreEqual(EnumEquipResult.Success, TestEntity.Slots.TryEquipArmor(piece));
        Assert.AreEqual(expectedTier != null, TestEntity.Brain.Modifiers.IsHolding(expectedTier!),
            $"穿上 {part} 后档位「{expectedTier?.Name}」的挂载状态不符");
    }

    private void Unequip(EnumArmorPart part)
    {
        TestEntity.Slots.UnequipArmor(part);
    }

    // ---- 用例 ----

    [UnityTest]
    public IEnumerator 一件不激活_二件激活二件档()
    {
        ModifierEffect tier2 = CreateModifier("二件档", (EnumStatType.MoveSpeed, 1.15f));
        ModifierEffect tier4 = CreateModifier("四件档", (EnumStatType.MoveSpeed, 1.05f), (EnumStatType.DamageTaken, 0.8f));
        ArmorSetSO set = CreateSet("测试套装", tier2, tier4);

        Equip("头", EnumArmorPart.Head, set, null);
        Assert.IsFalse(TestEntity.Brain.Modifiers.IsHolding(tier2), "1 件不该激活二件档");
        Assert.AreEqual(1f, TestEntity.Brain.Modifiers.GetStatMultiplier(EnumStatType.MoveSpeed), Epsilon);

        Equip("胸", EnumArmorPart.Chest, set, tier2);
        Assert.AreEqual(1.15f, TestEntity.Brain.Modifiers.GetStatMultiplier(EnumStatType.MoveSpeed), Epsilon);
        Assert.IsFalse(TestEntity.Brain.Modifiers.IsHolding(tier4), "2 件不该激活四件档");
        Assert.AreEqual(1, TestEntity.Brain.ArmorSets.ActiveTierCount(set));
        yield return null;
    }

    [UnityTest]
    public IEnumerator 三件仍只吃二件档_四件两档同时在挂()
    {
        ModifierEffect tier2 = CreateModifier("二件档", (EnumStatType.MoveSpeed, 1.15f));
        ModifierEffect tier4 = CreateModifier("四件档", (EnumStatType.MoveSpeed, 1.05f), (EnumStatType.DamageTaken, 0.8f));
        ArmorSetSO set = CreateSet("测试套装", tier2, tier4);

        Equip("头", EnumArmorPart.Head, set, null);
        Equip("胸", EnumArmorPart.Chest, set, tier2);
        Equip("腿", EnumArmorPart.Legs, set, tier2);

        Assert.AreEqual(3, TestEntity.Slots.Armor.EquippedCount);
        Assert.AreEqual(1.15f, TestEntity.Brain.Modifiers.GetStatMultiplier(EnumStatType.MoveSpeed), Epsilon, "3 件仍只吃二件档");

        // 穿满 4 件：二件档与四件档**同时**在挂，各自进乘法链（逐档累加，不是替换）
        Equip("足", EnumArmorPart.Feet, set, tier4);

        Assert.AreEqual(4, TestEntity.Slots.Armor.EquippedCount);
        Assert.IsTrue(TestEntity.Brain.Modifiers.IsHolding(tier2), "4 件时二件档应仍然在挂");
        Assert.IsTrue(TestEntity.Brain.Modifiers.IsHolding(tier4), "4 件时应挂上四件档");
        Assert.AreEqual(2, TestEntity.Brain.ArmorSets.ActiveTierCount(set));
        Assert.AreEqual(1.15f * 1.05f, TestEntity.Brain.Modifiers.GetStatMultiplier(EnumStatType.MoveSpeed), Epsilon,
            "4 件移速 = 二件档 × 四件档（累加相乘）");
        Assert.AreEqual(0.8f, TestEntity.Brain.Modifiers.GetStatMultiplier(EnumStatType.DamageTaken), Epsilon);
        yield return null;
    }

    [UnityTest]
    public IEnumerator 从四件卸到三件_只摘四件档_二件档保留()
    {
        ModifierEffect tier2 = CreateModifier("二件档", (EnumStatType.MoveSpeed, 1.15f));
        ModifierEffect tier4 = CreateModifier("四件档", (EnumStatType.MoveSpeed, 1.05f));
        ArmorSetSO set = CreateSet("测试套装", tier2, tier4);

        Equip("头", EnumArmorPart.Head, set, null);
        Equip("胸", EnumArmorPart.Chest, set, tier2);
        Equip("腿", EnumArmorPart.Legs, set, tier2);
        Equip("足", EnumArmorPart.Feet, set, tier4);

        Unequip(EnumArmorPart.Feet);

        Assert.IsTrue(TestEntity.Brain.Modifiers.IsHolding(tier2), "掉到 3 件后二件档应保留");
        Assert.IsFalse(TestEntity.Brain.Modifiers.IsHolding(tier4), "掉到 3 件后四件档应摘除");
        Assert.AreEqual(1.15f, TestEntity.Brain.Modifiers.GetStatMultiplier(EnumStatType.MoveSpeed), Epsilon);
        yield return null;
    }

    [UnityTest]
    public IEnumerator 逐件卸下_档位随件数阶梯回落()
    {
        ModifierEffect tier2 = CreateModifier("二件档", (EnumStatType.MoveSpeed, 1.15f));
        ModifierEffect tier4 = CreateModifier("四件档", (EnumStatType.MoveSpeed, 1.05f));
        ArmorSetSO set = CreateSet("测试套装", tier2, tier4);

        Equip("头", EnumArmorPart.Head, set, null);
        Equip("胸", EnumArmorPart.Chest, set, tier2);
        Equip("腿", EnumArmorPart.Legs, set, tier2);
        Equip("足", EnumArmorPart.Feet, set, tier4);

        // 4 → 3 件：只掉四件档（二件档仍达标）
        Unequip(EnumArmorPart.Feet);
        Assert.AreEqual(3, TestEntity.Slots.Armor.CountOf(set));
        Assert.IsTrue(TestEntity.Brain.Modifiers.IsHolding(tier2), "3 件仍达标二件档");
        Assert.IsFalse(TestEntity.Brain.Modifiers.IsHolding(tier4), "3 件掉四件档");

        // 3 → 2 件：仍是二件档（门槛是"≥2"）
        Unequip(EnumArmorPart.Legs);
        Assert.AreEqual(2, TestEntity.Slots.Armor.CountOf(set));
        Assert.IsTrue(TestEntity.Brain.Modifiers.IsHolding(tier2), "2 件仍达标二件档");

        // 2 → 1 件：跌破二件门槛，两档全摘、倍率回 1
        Unequip(EnumArmorPart.Chest);
        Assert.AreEqual(1, TestEntity.Slots.Armor.CountOf(set));
        Assert.IsFalse(TestEntity.Brain.Modifiers.IsHolding(tier2), "1 件时二件档应摘除");
        Assert.IsFalse(TestEntity.Brain.Modifiers.IsHolding(tier4));
        Assert.AreEqual(0, TestEntity.Brain.ArmorSets.ActiveTierCount(set));
        Assert.AreEqual(1f, TestEntity.Brain.Modifiers.GetStatMultiplier(EnumStatType.MoveSpeed), Epsilon);
        yield return null;
    }

    [UnityTest]
    public IEnumerator 换套_旧套全摘_新套按件数挂载()
    {
        ModifierEffect oldTier2 = CreateModifier("旧-二件", (EnumStatType.MoveSpeed, 1.2f));
        ModifierEffect oldTier4 = CreateModifier("旧-四件", (EnumStatType.MoveSpeed, 1.2f));
        ModifierEffect newTier2 = CreateModifier("新-二件", (EnumStatType.MoveSpeed, 1.1f));
        ModifierEffect newTier4 = CreateModifier("新-四件", (EnumStatType.MoveSpeed, 1.1f));
        ArmorSetSO oldSet = CreateSet("旧套装", oldTier2, oldTier4);
        ArmorSetSO newSet = CreateSet("新套装", newTier2, newTier4);

        Equip("旧头", EnumArmorPart.Head, oldSet, null);
        Equip("旧胸", EnumArmorPart.Chest, oldSet, oldTier2);
        Assert.IsTrue(TestEntity.Brain.Modifiers.IsHolding(oldTier2));

        // 头/胸换成新套 → 旧套件数归零（全摘），新套 2 件 → 只挂新二件档
        Equip("新头", EnumArmorPart.Head, newSet, null);
        Equip("新胸", EnumArmorPart.Chest, newSet, newTier2);

        Assert.IsFalse(TestEntity.Brain.Modifiers.IsHolding(oldTier2), "旧套二件档应摘除");
        Assert.IsFalse(TestEntity.Brain.Modifiers.IsHolding(oldTier4));
        Assert.IsTrue(TestEntity.Brain.Modifiers.IsHolding(newTier2), "新套二件档应挂上");
        Assert.IsFalse(TestEntity.Brain.Modifiers.IsHolding(newTier4), "新套只有 2 件，四件档不该挂");
        Assert.AreEqual(1.1f, TestEntity.Brain.Modifiers.GetStatMultiplier(EnumStatType.MoveSpeed), Epsilon);
        yield return null;
    }

    [UnityTest]
    public IEnumerator 两套混穿_各自独立计数_互不干扰()
    {
        ModifierEffect aTier2 = CreateModifier("A-二件", (EnumStatType.MoveSpeed, 1.3f));
        ModifierEffect aTier4 = CreateModifier("A-四件", (EnumStatType.MoveSpeed, 1.3f));
        ModifierEffect bTier2 = CreateModifier("B-二件", (EnumStatType.DamageTaken, 0.5f));
        ModifierEffect bTier4 = CreateModifier("B-四件", (EnumStatType.DamageTaken, 0.5f));
        ArmorSetSO setA = CreateSet("A 套", aTier2, aTier4);
        ArmorSetSO setB = CreateSet("B 套", bTier2, bTier4);

        Equip("A头", EnumArmorPart.Head, setA, null);
        Equip("A胸", EnumArmorPart.Chest, setA, aTier2);
        Equip("B腿", EnumArmorPart.Legs, setB, null);
        Equip("B足", EnumArmorPart.Feet, setB, bTier2);

        Assert.IsTrue(TestEntity.Brain.Modifiers.IsHolding(aTier2));
        Assert.IsTrue(TestEntity.Brain.Modifiers.IsHolding(bTier2));
        Assert.IsFalse(TestEntity.Brain.Modifiers.IsHolding(aTier4), "A 套只有 2 件");
        Assert.IsFalse(TestEntity.Brain.Modifiers.IsHolding(bTier4), "B 套只有 2 件");
        Assert.AreEqual(1.3f, TestEntity.Brain.Modifiers.GetStatMultiplier(EnumStatType.MoveSpeed), Epsilon);
        Assert.AreEqual(0.5f, TestEntity.Brain.Modifiers.GetStatMultiplier(EnumStatType.DamageTaken), Epsilon);
        yield return null;
    }

    [UnityTest]
    public IEnumerator 散件不计入任何套装()
    {
        ModifierEffect tier2 = CreateModifier("测试二件", (EnumStatType.MoveSpeed, 1.15f));
        ArmorSetSO set = CreateSet("测试套装", tier2, null);

        // 一件属套、一件散件（Set 为空）
        Equip("套头", EnumArmorPart.Head, set, null);
        ArmorSO loose = CreateArmor("散件胸", EnumArmorPart.Chest, null);
        Assert.AreEqual(EnumEquipResult.Success, TestEntity.Slots.TryEquipArmor(loose));

        Assert.AreEqual(2, TestEntity.Slots.Armor.EquippedCount, "两件护甲在身（一件属套 + 一件散件）");
        Assert.AreEqual(1, TestEntity.Slots.Armor.CountOf(set), "散件不该计入套装件数");
        Assert.IsFalse(TestEntity.Brain.Modifiers.IsHolding(tier2), "散件不该把套装件数顶到 2");
        yield return null;
    }

    [UnityTest]
    public IEnumerator 重复同步幂等_不产生重复条目()
    {
        ModifierEffect tier2 = CreateModifier("二件档", (EnumStatType.MoveSpeed, 1.15f));
        ArmorSetSO set = CreateSet("测试套装", tier2, null);

        Equip("头", EnumArmorPart.Head, set, null);
        Equip("胸", EnumArmorPart.Chest, set, tier2);

        string before = TestEntity.Brain.Modifiers.Describe();
        TestEntity.Brain.ArmorSets.Sync();
        TestEntity.Brain.ArmorSets.Sync();
        TestEntity.Slots.TryEquipArmor(TestEntity.Slots.Armor.Get(0)!);   // 同件重穿（同部位替换）

        Assert.AreEqual(before, TestEntity.Brain.Modifiers.Describe(), "重复/重穿后效果条目不该重复");
        Assert.AreEqual(1.15f, TestEntity.Brain.Modifiers.GetStatMultiplier(EnumStatType.MoveSpeed), Epsilon);
        yield return null;
    }

    [UnityTest]
    public IEnumerator 未配效果的门槛_不挂条目不崩()
    {
        // 二件档配空效果：只计数不生效
        ArmorSetSO set = CreateSet("空档套装", null, null);
        ArmorSO head = CreateArmor("头", EnumArmorPart.Head, set);
        ArmorSO chest = CreateArmor("胸", EnumArmorPart.Chest, set);

        TestEntity.Slots.TryEquipArmor(head);
        TestEntity.Slots.TryEquipArmor(chest);

        Assert.AreEqual(2, TestEntity.Slots.Armor.CountOf(set));
        Assert.AreEqual(0, TestEntity.Brain.ArmorSets.ActiveTierCount(set));
        Assert.AreEqual(1f, TestEntity.Brain.Modifiers.GetStatMultiplier(EnumStatType.MoveSpeed), Epsilon);
        yield return null;
    }

    [UnityTest]
    public IEnumerator 空护甲_返回失败且不改动槽位()
    {
        Assert.AreEqual(EnumEquipResult.InvalidWeapon, TestEntity.Slots.TryEquipArmor(null!));
        Assert.AreEqual(0, TestEntity.Slots.Armor.EquippedCount);
        yield return null;
    }

    // ---- ModifierList.Remove 精确摘除（护甲套装新依赖的基座能力）----

    [UnityTest]
    public IEnumerator 精确摘除_命中摘一条_二次摘返回假_不误伤他条()
    {
        ModifierEffect target = CreateModifier("目标", (EnumStatType.MoveSpeed, 2f));
        ModifierEffect bystander = CreateModifier("旁观", (EnumStatType.MoveSpeed, 3f));

        TestEntity.Brain.Modifiers.Apply(target);
        TestEntity.Brain.Modifiers.Apply(bystander);

        Assert.IsTrue(TestEntity.Brain.Modifiers.Remove(target), "第一次摘除应命中");
        Assert.IsFalse(TestEntity.Brain.Modifiers.IsHolding(target));
        Assert.IsTrue(TestEntity.Brain.Modifiers.IsHolding(bystander), "另一条不该被误摘");
        Assert.IsFalse(TestEntity.Brain.Modifiers.Remove(target), "已摘除后再摘应返回 false");
        Assert.AreEqual(3f, TestEntity.Brain.Modifiers.GetStatMultiplier(EnumStatType.MoveSpeed), Epsilon, "只剩旁观那条的乘数");
        yield return null;
    }

    [UnityTest]
    public IEnumerator 全驱散不含套装类别_纯套装条目不被清()
    {
        ModifierEffect armorEffect = CreateModifier("套装效果", (EnumStatType.MoveSpeed, 1.5f));
        // 纯套装条目：Category 只带 ArmorSet（不带 Buff）——All 不含 ArmorSet，故全驱散应放过
        armorEffect.Category = EnumModifierCategory.ArmorSet;

        TestEntity.Brain.Modifiers.Apply(armorEffect);
        int removed = TestEntity.Brain.Modifiers.Dispel(EnumModifierCategory.All);

        Assert.AreEqual(0, removed, "Dispel(All) 不该清掉 ArmorSet 类别的条目");
        Assert.IsTrue(TestEntity.Brain.Modifiers.IsHolding(armorEffect));
        yield return null;
    }

    [UnityTest]
    public IEnumerator 被全驱散清掉的档位_下次同步补回()
    {
        ModifierEffect tier2 = CreateModifier("二件档", (EnumStatType.MoveSpeed, 1.15f));
        // 带 Buff 位：会被 Dispel(All) 清掉（模拟外部驱散/将来净化技能）
        ArmorSetSO set = CreateSet("测试套装", tier2, null);

        Equip("头", EnumArmorPart.Head, set, null);
        Equip("胸", EnumArmorPart.Chest, set, tier2);

        TestEntity.Brain.Modifiers.Dispel(EnumModifierCategory.All);
        Assert.IsFalse(TestEntity.Brain.Modifiers.IsHolding(tier2), "带 Buff 位的档位会被全驱散清掉");

        TestEntity.Brain.ArmorSets.Sync();
        Assert.IsTrue(TestEntity.Brain.Modifiers.IsHolding(tier2), "同步应把仍达标的档位补回");
        Assert.AreEqual(1.15f, TestEntity.Brain.Modifiers.GetStatMultiplier(EnumStatType.MoveSpeed), Epsilon);
        yield return null;
    }
}
