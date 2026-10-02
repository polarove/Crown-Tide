using Assets.Scripts.Entity.Data.Armor;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 护甲槽纯数据测试（EditMode，无需 GameObject）：件数计数 / 套装查询 / 装备替换 / 空引用纪律。
/// 这些不变量是套装档位判定的输入，全在这里钉住。
/// </summary>
public sealed class ArmorSlotTests
{
    private readonly List<ScriptableObject> Created = new();

    [TearDown]
    public void TearDown()
    {
        foreach (ScriptableObject asset in Created)
        {
            UnityEngine.Object.DestroyImmediate(asset);
        }
        Created.Clear();
    }

    private T Create<T>() where T : ScriptableObject
    {
        T asset = ScriptableObject.CreateInstance<T>();
        Created.Add(asset);
        return asset;
    }

    /// <summary>造一件指定部位/套装的护甲（测试桩，不走资产数据库）；set 可空（= 散件）</summary>
    private ArmorSO CreateArmor(string name, EnumArmorPart part, ArmorSetSO? set)
    {
        ArmorSO armor = Create<ArmorSO>();
        armor.Name = name;
        armor.Part = part;
        armor.Set = set;
        return armor;
    }

    [Test]
    public void 空槽_件数为零且任何套装计数为零()
    {
        ArmorSlot slot = new();
        ArmorSetSO set = Create<ArmorSetSO>();

        Assert.AreEqual(0, slot.EquippedCount);
        Assert.AreEqual(0, slot.CountOf(set));
        Assert.IsNull(slot.Get(0));
        Assert.IsNull(slot.Get(3));
    }

    [Test]
    public void CountOf_按不同部位计数_头胸腿三件同套记三()
    {
        ArmorSlot slot = new();
        ArmorSetSO set = Create<ArmorSetSO>();

        slot.Equip(CreateArmor("头", EnumArmorPart.Head, set));
        Assert.AreEqual(1, slot.EquippedCount);
        Assert.AreEqual(1, slot.CountOf(set));

        slot.Equip(CreateArmor("胸", EnumArmorPart.Chest, set));
        slot.Equip(CreateArmor("腿", EnumArmorPart.Legs, set));
        Assert.AreEqual(3, slot.EquippedCount);
        Assert.AreEqual(3, slot.CountOf(set));

        slot.Equip(CreateArmor("足", EnumArmorPart.Feet, set));
        Assert.AreEqual(4, slot.EquippedCount);
        Assert.AreEqual(4, slot.CountOf(set));
    }

    [Test]
    public void 同部位替换_件数不增_旧件被返回()
    {
        ArmorSlot slot = new();
        ArmorSetSO set = Create<ArmorSetSO>();
        ArmorSO first = CreateArmor("旧头", EnumArmorPart.Head, set);
        ArmorSO second = CreateArmor("新头", EnumArmorPart.Head, set);

        Assert.IsNull(slot.Equip(first));            // 首次装备无旧件
        Assert.AreSame(first, slot.Equip(second));   // 替换返回旧件
        Assert.AreEqual(1, slot.EquippedCount);
        Assert.AreEqual(1, slot.CountOf(set));
        Assert.AreSame(second, slot.Get(0));
    }

    [Test]
    public void CountOf_两套混合_各记各的且散件不计()
    {
        ArmorSlot slot = new();
        ArmorSetSO setA = Create<ArmorSetSO>();
        ArmorSetSO setB = Create<ArmorSetSO>();

        slot.Equip(CreateArmor("A头", EnumArmorPart.Head, setA));
        slot.Equip(CreateArmor("A胸", EnumArmorPart.Chest, setA));
        slot.Equip(CreateArmor("B腿", EnumArmorPart.Legs, setB));
        slot.Equip(CreateArmor("散件足", EnumArmorPart.Feet, null));   // 散件（Set 为空）

        Assert.AreEqual(4, slot.EquippedCount);
        Assert.AreEqual(2, slot.CountOf(setA));
        Assert.AreEqual(1, slot.CountOf(setB));
        Assert.AreEqual(0, slot.CountOf(null!));   // 空套装引用 = 不计
    }

    [Test]
    public void Unequip_卸下后计数归零_重复卸下返回空()
    {
        ArmorSlot slot = new();
        ArmorSetSO set = Create<ArmorSetSO>();
        ArmorSO piece = CreateArmor("头", EnumArmorPart.Head, set);
        slot.Equip(piece);

        Assert.AreSame(piece, slot.Unequip(EnumArmorPart.Head));
        Assert.AreEqual(0, slot.CountOf(set));
        Assert.AreEqual(0, slot.EquippedCount);
        Assert.IsNull(slot.Unequip(EnumArmorPart.Head));
    }

    [Test]
    public void Equip_空护甲抛异常()
    {
        ArmorSlot slot = new();
        Assert.Throws<ArgumentNullException>(() => slot.Equip(null!));
    }

    [Test]
    public void Get_越界返回空()
    {
        ArmorSlot slot = new();
        Assert.IsNull(slot.Get(-1));
        Assert.IsNull(slot.Get(4));
        Assert.IsNull(slot.Get(99));
    }

    [Test]
    public void Get_按部位枚举顺序取件()
    {
        ArmorSlot slot = new();
        ArmorSO head = CreateArmor("头", EnumArmorPart.Head, null);
        ArmorSO chest = CreateArmor("胸", EnumArmorPart.Chest, null);
        ArmorSO legs = CreateArmor("腿", EnumArmorPart.Legs, null);
        ArmorSO feet = CreateArmor("足", EnumArmorPart.Feet, null);

        slot.Equip(head);
        slot.Equip(chest);
        slot.Equip(legs);
        slot.Equip(feet);

        Assert.AreSame(head, slot.Get(0));
        Assert.AreSame(chest, slot.Get(1));
        Assert.AreSame(legs, slot.Get(2));
        Assert.AreSame(feet, slot.Get(3));
    }
}

/// <summary>
/// 套装档位表纯数据测试：DescribeTiers 的输出即「门槛 → 效果名」配表事实，
/// 用于确认二件档/四件档两条档位被正确登记（累加语义的资产侧）。
/// </summary>
public sealed class ArmorSetSOTests
{
    private readonly List<ScriptableObject> Created = new();

    [TearDown]
    public void TearDown()
    {
        foreach (ScriptableObject asset in Created)
        {
            UnityEngine.Object.DestroyImmediate(asset);
        }
        Created.Clear();
    }

    private T Create<T>() where T : ScriptableObject
    {
        T asset = ScriptableObject.CreateInstance<T>();
        Created.Add(asset);
        return asset;
    }

    [Test]
    public void DescribeTiers_两条档位依次列出()
    {
        ArmorSetSO set = Create<ArmorSetSO>();
        set.Name = "测试套装";
        ModifierEffect tier2 = Create<ModifierEffect>();
        tier2.Name = "二件";
        ModifierEffect tier4 = Create<ModifierEffect>();
        tier4.Name = "四件";
        set.Bonuses = new[]
        {
            new ArmorSetBonus { PieceCount = 2, Modifier = tier2 },
            new ArmorSetBonus { PieceCount = 4, Modifier = tier4 },
        };

        Assert.AreEqual("2件→二件、4件→四件", set.DescribeTiers());
    }

    [Test]
    public void DescribeTiers_空表与空效果都有可读输出()
    {
        ArmorSetSO empty = Create<ArmorSetSO>();
        Assert.AreEqual("无档位", empty.DescribeTiers());

        ArmorSetSO withHole = Create<ArmorSetSO>();
        withHole.Bonuses = new[] { new ArmorSetBonus { PieceCount = 2, Modifier = null! } };
        Assert.AreEqual("2件→(空)", withHole.DescribeTiers());
    }
}
