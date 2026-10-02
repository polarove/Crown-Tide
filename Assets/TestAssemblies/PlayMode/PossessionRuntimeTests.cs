using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// 附身会话全链路测试（PlayMode，真 Entity + 真 buff 机制）：**会话状态就是 buff**——
/// 挂「被附身」载体 = 会话开始，Duration 到期自动摘 = 自动换回；换绑由 EntityBrain 感知 buff 完成。
/// 覆盖：开始/结束的输入源归属、二次发起与占用闸门、目标死亡后的收尾、受伤降信心钩子。
/// 没有任何会话管理器参与（需求钦定：附身按 buff 的逻辑做）。
/// </summary>
public sealed class PossessionRuntimeTests
{
    // 由 [UnitySetUp] 赋值（NUnit 不在构造期跑 SetUp，故用 null! 断言）
    private GameObject HostObject = null!;
    private GameObject TargetObject = null!;
    private Entity Host = null!;
    private Entity Target = null!;
    private PossessionEffect PossessedEffect = null!;   // 会话载体（挂在被附身者身上，Duration = 时长）
    private PossessionEffect SoulOutEffect = null!;     // 出窍者侧纯标记
    private readonly List<ScriptableObject> Created = new();

    private const float Epsilon = 1e-4f;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        HostObject = new GameObject("PossessionHost");
        Host = HostObject.AddComponent<Entity>();   // RequireComponent 拉起全家（含双输入源与 PlayerInput）
        TargetObject = new GameObject("PossessionTarget");
        Target = TargetObject.AddComponent<Entity>();
        yield return null;   // 等 Awake 跑完（Bootstrap 装配）

        // 兜底：极端情况下 Awake 未跑（不是预期路径），显式装配一次让测试可读失败而不是空引用
        if (Host.Brain == null || Host.Brain.Modifiers == null)
        {
            Host.Brain!.Bootstrap(Host);
        }
        if (Target.Brain == null || Target.Brain.Modifiers == null)
        {
            Target.Brain!.Bootstrap(Target);
        }

        // 默认开局绑 AI（未勾 startPlayerControlled）→ 手动让 Host 由玩家驱动（附身发起方）
        Host.Brain.BindInputSource(Host.Brain.PlayerSource);

        PossessedEffect = CreatePossessionEffect("被附身", EnumPossessionRole.Possessed, 10f);
        SoulOutEffect = CreatePossessionEffect("灵魂出窍", EnumPossessionRole.SoulOut, 10f);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (HostObject != null)
        {
            Object.Destroy(HostObject);
        }
        if (TargetObject != null)
        {
            Object.Destroy(TargetObject);
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

    /// <summary>造一份附身效果（纯标记：无数值/控制/周期成分，只有角色与时长）</summary>
    private PossessionEffect CreatePossessionEffect(string name, EnumPossessionRole role, float duration)
    {
        PossessionEffect effect = CreateAsset<PossessionEffect>();
        effect.Name = name;
        effect.PossessionRole = role;
        effect.Duration = duration;
        effect.Category = EnumModifierCategory.Buff;
        return effect;
    }

    /// <summary>造角色配置并让实体重新装配生效（Vitals.Initialize 可重复调用——复活预留）</summary>
    private CharacterConfigSO ApplyConfig(Entity entity, float maxHealth, int faithLossPerHit)
    {
        CharacterConfigSO config = CreateAsset<CharacterConfigSO>();
        config.MaxHealth = maxHealth;
        config.FaithLossPerHit = faithLossPerHit;
        entity.Config = config;
        entity.Brain.Bootstrap(entity);
        return config;
    }

    // ---- 用例 ----

    [UnityTest]
    public IEnumerator 挂上载体buff_会话开始并换绑()
    {
        Assert.IsTrue(Host.Brain.TryBeginPossession(Target, PossessedEffect, SoulOutEffect), "正常发起应成功");

        Assert.IsTrue(Target.Brain.Modifiers.IsHolding(PossessedEffect), "被附身者身上应有会话载体 buff");
        Assert.IsTrue(Host.Brain.Modifiers.IsHolding(SoulOutEffect), "出窍者身上应有灵魂出窍标记");
        Assert.IsTrue(Target.Brain.InputSource is PlayerInputSource, "载体 buff 在挂 → 目标由玩家驱动");
        Assert.IsTrue(Host.Brain.InputSource is AITreeInputSource, "出窍者 → 回 AI 驱动");
        Assert.IsTrue(Target.Brain.IsPossessing, "目标的会话状态应已生效");
        Assert.IsTrue(Host.Brain.HasSoulOut, "出窍者应报告灵魂出窍中");

        // 倒计时读点：会话剩余时间就是载体 buff 的剩余时长
        Assert.Greater(Target.Brain.PossessionRemaining, 9f);
        yield return null;
    }

    [UnityTest]
    public IEnumerator 载体buff到期_自动摘掉并换回()
    {
        PossessedEffect.Duration = 0.05f;   // 会话载体按资产 Duration 计时
        SoulOutEffect.Duration = 0.05f;
        Assert.IsTrue(Host.Brain.TryBeginPossession(Target, PossessedEffect, SoulOutEffect));

        for (int i = 0; i < 12; i++)
        {
            yield return null;   // 等 ModifierList.Tick 把时长走完并自动摘除
        }

        Assert.IsFalse(Target.Brain.Modifiers.IsHolding(PossessedEffect), "载体 buff 到期应自动摘除");
        Assert.IsFalse(Host.Brain.Modifiers.IsHolding(SoulOutEffect), "灵魂出窍标记应同步到期");
        Assert.IsFalse(Target.Brain.IsPossessing, "会话应已结束");
        Assert.IsTrue(Host.Brain.InputSource is PlayerInputSource, "结束后出窍者拿回玩家输入");
        Assert.IsTrue(Target.Brain.InputSource is AITreeInputSource, "结束后被附身者回 AI");
    }

    [UnityTest]
    public IEnumerator 显式中断入口_等同于摘掉载体()
    {
        Assert.IsTrue(Host.Brain.TryBeginPossession(Target, PossessedEffect, SoulOutEffect));

        Target.Brain.EndPossession();   // 唯一中断入口（将来网络强制/死亡观战切人走它；玩家按键不调）
        yield return null;

        Assert.IsFalse(Target.Brain.Modifiers.IsHolding(PossessedEffect));
        Assert.IsFalse(Target.Brain.IsPossessing);
        Assert.IsTrue(Target.Brain.InputSource is AITreeInputSource, "中断后目标回 AI");
        Assert.IsTrue(Host.Brain.InputSource is PlayerInputSource, "中断后出窍者拿回控制");
    }

    [UnityTest]
    public IEnumerator 身体已被占用_拒绝附身()
    {
        Assert.IsTrue(Host.Brain.TryBeginPossession(Target, PossessedEffect, SoulOutEffect));

        // 第三个实体想抢同一具身体：有「被附身」buff = 已被占用（多人下这是初步闸门）
        GameObject otherObject = new("PossessionOther");
        Entity other = otherObject.AddComponent<Entity>();
        yield return null;
        other.Brain.BindInputSource(other.Brain.PlayerSource);   // 另一个玩家

        PossessionEffect otherCarrier = CreatePossessionEffect("被附身", EnumPossessionRole.Possessed, 10f);
        Assert.IsFalse(other.Brain.TryBeginPossession(Target, otherCarrier), "身体已被占用应拒绝");
        Assert.IsFalse(other.Brain.IsPossessing, "被拒绝方不应进入会话");

        Object.Destroy(otherObject);
        yield return null;
    }

    [UnityTest]
    public IEnumerator 已在附身中_拒绝二次发起()
    {
        Assert.IsTrue(Host.Brain.TryBeginPossession(Target, PossessedEffect, SoulOutEffect));

        // 出窍者身上已有 SoulOut 标记 = 我已在附身中（不能中途跑路）
        GameObject otherObject = new("PossessionOther");
        Entity other = otherObject.AddComponent<Entity>();
        yield return null;

        Assert.IsFalse(Host.Brain.TryBeginPossession(other, PossessedEffect, SoulOutEffect),
            "已在附身中应拒绝再次发起");
        Assert.IsFalse(other.Brain.IsPossessing, "被拒绝的目标不应被挂上载体");

        Object.Destroy(otherObject);
        yield return null;
    }

    [UnityTest]
    public IEnumerator 出窍者当前由AI驱动_仍可发起()
    {
        Host.Brain.BindInputSource(Host.Brain.AiSource);   // 当前不是玩家驱动（多人下允许代其发起）

        bool allowed = Host.Brain.TryBeginPossession(Target, PossessedEffect, SoulOutEffect);
        Assert.IsTrue(allowed, "附身闸门只看 buff 与存活，不要求发起者当前一定被玩家驱动");
        Assert.IsTrue(Target.Brain.IsPossessing);
        yield return null;
    }

    [UnityTest]
    public IEnumerator 被附身者死亡_及时收尾()
    {
        ApplyConfig(Target, maxHealth: 100f, faithLossPerHit: 5);   // 有血量上限才打得死
        Assert.IsTrue(Host.Brain.TryBeginPossession(Target, PossessedEffect, SoulOutEffect));

        Target.Brain.TakeDamage(999f);   // 被玩家操作的怪被反杀
        Assert.IsTrue(Target.IsDead);

        Target.Brain.EndPossession();   // 死亡收尾（将来由死亡观战切人流程调用同一入口）
        yield return null;

        Assert.IsFalse(Target.Brain.IsPossessing, "死亡后会话应结束");
        Assert.IsFalse(Target.Brain.Modifiers.IsHolding(PossessedEffect));
        Assert.IsTrue(Host.Brain.InputSource is PlayerInputSource, "出窍者拿回控制，不至于卡在 AI 手里");
    }

    [UnityTest]
    public IEnumerator 受击降低信心_负向贴边封底()
    {
        ApplyConfig(Host, maxHealth: 100f, faithLossPerHit: 5);
        Assert.IsNotNull(Host.Vitals.Faith);
        Assert.AreEqual(0, Host.Vitals.Faith!.Current, "初始信心居中 0");

        Host.Brain.TakeDamage(10f);   // 伤害不问来源：被自己附身的怪打、被队友误伤同规则
        Assert.AreEqual(-5, Host.Vitals.Faith.Current, "受击一次信心往潮汐方向降 5");
        Assert.AreEqual(90f, Host.Vitals.CurrentHp, Epsilon);

        for (int i = 0; i < 30; i++)
        {
            Host.Brain.TakeDamage(1f);   // 共 40 点伤害：活着，但信心早已贴边
        }
        Assert.AreEqual(-Host.Vitals.FaithCapacity, Host.Vitals.Faith.Current,
            "贴边 -FaithCapacity 封底不再下降（潮汐大技能在这条负向通道上攒）");
        yield return null;
    }
}
