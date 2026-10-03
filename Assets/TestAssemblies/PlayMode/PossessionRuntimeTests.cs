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
        SoulOutEffect = CreatePossessionEffect("灵魂出窍", EnumPossessionRole.SoulOut, 0f);
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
        Assert.AreEqual(0f, SoulOutEffect.Duration, "原角色标记应永久，只有目标计时");
        Assert.IsTrue(Host.Brain.TryBeginPossession(Target, PossessedEffect, SoulOutEffect));

        yield return new WaitForSeconds(0.1f); // 按游戏时间等待，不能假设固定帧数等于固定时长。

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
    public IEnumerator AI发起附身_不凭空取得本地玩家输入()
    {
        Host.Brain.BindInputSource(Host.Brain.AiSource);   // 当前不是玩家驱动（多人下允许代其发起）

        bool allowed = Host.Brain.TryBeginPossession(Target, PossessedEffect, SoulOutEffect);
        Assert.IsTrue(allowed, "Entity 固有技能允许 AI 发起");
        Assert.IsTrue(Target.Brain.IsPossessing);
        Assert.IsTrue(Target.Brain.InputSource is AITreeInputSource, "AI 发起不能凭空抢本地玩家输入");
        yield return null;
    }

    [UnityTest]
    public IEnumerator 被附身者死亡_及时收尾()
    {
        ApplyConfig(Target, maxHealth: 100f, faithLossPerHit: 5);   // 有血量上限才打得死
        Assert.IsTrue(Host.Brain.TryBeginPossession(Target, PossessedEffect, SoulOutEffect));

        Target.Brain.TakeDamage(999f);   // 被玩家操作的怪被反杀
        Assert.IsTrue(Target.IsDead);

        // 不手动结束：真实死亡事件必须自动归还控制。
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
    [UnityTest]
    public IEnumerator 控制期间拒绝附身_显式例外可释放()
    {
        ModifierEffect stun = CreateAsset<ModifierEffect>();
        stun.HasControl = true;
        Host.Brain.Modifiers.Apply(stun);
        Assert.IsFalse(Host.Brain.TryBeginPossession(Target, PossessedEffect, SoulOutEffect));
        Assert.IsFalse(Target.Brain.Modifiers.IsHolding(PossessedEffect));
        PossessedEffect.AllowWhileControlled = true;
        Assert.IsTrue(Host.Brain.TryBeginPossession(Target, PossessedEffect, SoulOutEffect));
        yield return null;
    }

    [UnityTest]
    public IEnumerator 原角色永久标记_目标到期清理双方()
    {
        Assert.IsTrue(Host.Brain.TryBeginPossession(Target, PossessedEffect, SoulOutEffect));
        Assert.AreEqual(-1f, Host.Brain.Modifiers.GetRemaining(SoulOutEffect));
        Target.Brain.Modifiers.Tick(11f);
        yield return null;
        Assert.IsFalse(Host.Brain.Modifiers.IsHolding(SoulOutEffect));
        Assert.IsFalse(Host.Brain.IsPossessing);
        Assert.IsTrue(Host.Brain.InputSource is PlayerInputSource);
        Assert.IsTrue(Target.Brain.InputSource is AITreeInputSource);
    }

    [UnityTest]
    public IEnumerator 延长只改目标实例_共享模板不受影响()
    {
        Assert.IsTrue(Host.Brain.TryBeginPossession(Target, PossessedEffect, SoulOutEffect));
        Target.Brain.Modifiers.Tick(4f);
        Assert.IsTrue(Host.Brain.ExtendPossession(5f));
        Assert.AreEqual(11f, Target.Brain.PossessionRemaining, Epsilon);
        Assert.AreEqual(10f, PossessedEffect.Duration);
        Assert.IsFalse(Target.Brain.ExtendPossession(float.NaN));
        Assert.IsFalse(Target.Brain.ExtendPossession(-1f));
        Target.Brain.EndPossession();
        Target.Brain.EndPossession();
        Assert.IsFalse(Host.Brain.Modifiers.IsHolding(SoulOutEffect));
        yield return null;
    }

    [UnityTest]
    public IEnumerator 修改模板时长_不改变在挂实例的计时()
    {
        Assert.IsTrue(Host.Brain.TryBeginPossession(Target, PossessedEffect, SoulOutEffect));
        PossessedEffect.Duration = 0f;
        Target.Brain.Modifiers.Tick(11f);
        yield return null;
        Assert.IsFalse(Target.Brain.IsPossessing);
        Assert.IsTrue(Host.Brain.InputSource is PlayerInputSource);
    }

    [UnityTest]
    public IEnumerator 原角色死亡_返回视角但不恢复玩家操作()
    {
        ApplyConfig(Host, 100f, 5);
        Host.Brain.BindInputSource(Host.Brain.PlayerSource);
        Assert.IsTrue(Host.Brain.TryBeginPossession(Target, PossessedEffect, SoulOutEffect));
        Host.Brain.TakeDamage(999f);
        Assert.IsFalse(Host.Brain.IsPossessing);
        Assert.IsTrue(Host.Brain.HasPlayerView);
        Assert.IsTrue(Host.Brain.InputSource is AITreeInputSource);
        Assert.IsFalse(Target.Brain.HasPlayerView);
        Assert.IsFalse(Target.Brain.Modifiers.IsHolding(PossessedEffect));
        yield return null;
    }

    [UnityTest]
    public IEnumerator 周期伤害致死_遍历安全并清理双方()
    {
        ApplyConfig(Target, 100f, 5);
        Assert.IsTrue(Host.Brain.TryBeginPossession(Target, PossessedEffect, SoulOutEffect));
        ModifierEffect wound = CreateAsset<ModifierEffect>();
        wound.Duration = 0f;
        wound.HasPeriodic = true;
        wound.TickInterval = 0.01f;
        wound.DamagePerTick = 999f;
        Target.Brain.Modifiers.Apply(wound);
        Assert.DoesNotThrow(() => Target.Brain.Modifiers.Tick(0.1f));
        yield return null;
        Assert.IsFalse(Target.Brain.IsPossessing);
        Assert.IsTrue(Host.Brain.InputSource is PlayerInputSource);
        Assert.IsFalse(Host.Brain.Modifiers.IsHolding(SoulOutEffect));
    }

    [UnityTest]
    public IEnumerator 原角色交AI_出窍标记不阻止追击指令()
    {
        Assert.IsTrue(Host.Brain.TryBeginPossession(Target, PossessedEffect, SoulOutEffect));
        Host.Brain.AiSource!.Target = Target.transform;
        Host.Brain.AiSource.DecisionInterval = 0f;
        Target.transform.position = Host.transform.position + Vector3.right * 5f;
        Host.Brain.Commands.ResetLevels();
        Host.Brain.AiSource.GatherCommands(Host.Brain.Commands);
        Assert.Greater(Host.Brain.Commands.MoveDirection.sqrMagnitude, 0f);
        yield return null;
    }

    [UnityTest]
    public IEnumerator 任一侧标记被移除_结束整个附身()
    {
        Assert.IsTrue(Host.Brain.TryBeginPossession(Target, PossessedEffect, SoulOutEffect));
        Host.Brain.Modifiers.Remove(SoulOutEffect);
        yield return null;
        Assert.IsFalse(Target.Brain.Modifiers.IsHolding(PossessedEffect));
        Assert.IsTrue(Host.Brain.InputSource is PlayerInputSource);
        Assert.IsTrue(Target.Brain.InputSource is AITreeInputSource);
    }

    [UnityTest]
    public IEnumerator 目标禁用_立即归还控制权()
    {
        Assert.IsTrue(Host.Brain.TryBeginPossession(Target, PossessedEffect, SoulOutEffect));
        TargetObject.SetActive(false);
        Assert.IsFalse(Host.Brain.IsPossessing);
        Assert.IsTrue(Host.Brain.InputSource is PlayerInputSource);
        Assert.IsFalse(Host.Brain.Modifiers.IsHolding(SoulOutEffect));
        yield return null;
    }

    [UnityTest]
    public IEnumerator 目标销毁_清理原角色标记()
    {
        Assert.IsTrue(Host.Brain.TryBeginPossession(Target, PossessedEffect, SoulOutEffect));
        Object.Destroy(TargetObject);
        yield return null;
        Assert.IsFalse(Host.Brain.IsPossessing);
        Assert.IsTrue(Host.Brain.InputSource is PlayerInputSource);
        Assert.IsFalse(Host.Brain.Modifiers.IsHolding(SoulOutEffect));
    }

    [UnityTest]
    public IEnumerator 两组附身共用模板_到期不会串号()
    {
        GameObject secondHostObject = new("SecondHost");
        GameObject secondTargetObject = new("SecondTarget");
        try
        {
            Entity secondHost = secondHostObject.AddComponent<Entity>();
            Entity secondTarget = secondTargetObject.AddComponent<Entity>();
            secondHost.Brain.BindInputSource(secondHost.Brain.PlayerSource);
            Assert.IsTrue(Host.Brain.TryBeginPossession(Target, PossessedEffect, SoulOutEffect));
            Assert.IsTrue(secondHost.Brain.TryBeginPossession(secondTarget, PossessedEffect, SoulOutEffect));
            Target.Brain.EndPossession();
            Assert.IsTrue(Host.Brain.InputSource is PlayerInputSource);
            Assert.IsTrue(secondTarget.Brain.InputSource is PlayerInputSource);
            Assert.IsTrue(secondHost.Brain.HasSoulOut);
            Assert.IsTrue(secondTarget.Brain.IsPossessing);
            secondTarget.Brain.EndPossession();
            Assert.IsTrue(secondHost.Brain.InputSource is PlayerInputSource);
        }
        finally
        {
            Object.Destroy(secondHostObject);
            Object.Destroy(secondTargetObject);
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator 标记缺失或角色配错_拒绝且不写入效果()
    {
        Assert.IsFalse(Host.Brain.TryBeginPossession(Target, PossessedEffect));
        SoulOutEffect.PossessionRole = EnumPossessionRole.Possessed;
        Assert.IsFalse(Host.Brain.TryBeginPossession(Target, PossessedEffect, SoulOutEffect));
        Assert.IsFalse(Target.Brain.Modifiers.IsHolding(PossessedEffect));
        yield return null;
    }

}


/// <summary>已保存演示场景的真实接线回归：输入源与相机随附身往返。</summary>
public sealed class SampleScenePossessionTests
{
    [UnityTearDown]
    public IEnumerator UnloadTestScene()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName("SampleScene");
        if (scene.IsValid() && scene.isLoaded)
            yield return UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
    }

    [UnityTest]
    public IEnumerator 演示场景_双方出生不穿平台且运行后不掉落()
    {
        yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("SampleScene", UnityEngine.SceneManagement.LoadSceneMode.Additive);
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName("SampleScene");
        try
        {
            var roots = scene.GetRootGameObjects();
            Collider platform = System.Array.Find(roots, go => go.name == "Platform").GetComponent<Collider>();
            GameObject player = System.Array.Find(roots, go => go.name == "Player");
            GameObject enemy = System.Array.Find(roots, go => go.name == "Enemy");
            Physics.SyncTransforms();
            foreach (GameObject actor in new[] { player, enemy })
            {
                CharacterController controller = actor.GetComponent<CharacterController>();
                Assert.GreaterOrEqual(controller.bounds.min.y, platform.bounds.max.y - 0.1f,
                    $"{actor.name} 出生时穿入平台");
            }
            // 保留真实 Brain、AI 追击和重力管线；敌人会走向玩家。
            yield return new WaitForSeconds(2f);
            foreach (GameObject actor in new[] { player, enemy })
            {
                CharacterController controller = actor.GetComponent<CharacterController>();
                Assert.GreaterOrEqual(controller.bounds.min.y, platform.bounds.max.y - 0.1f,
                    $"{actor.name} 运行后掉到平台下面");
                Assert.IsTrue(actor.GetComponent<EntityMotor>().IsGrounded,
                    $"{actor.name} 未稳定落地");
            }
        }
        finally
        {
            // UnityTearDown 等待卸载，包含断言失败路径。
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator 演示场景_附身输入与相机完整往返()
    {
        yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("SampleScene", UnityEngine.SceneManagement.LoadSceneMode.Additive);
        yield return null; // 等相机 LateUpdate 完成首次只读归属同步。
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName("SampleScene");
        try
        {
            var roots = scene.GetRootGameObjects();
            Entity player = System.Array.Find(roots, go => go.name == "Player").GetComponent<Entity>();
            Entity enemy = System.Array.Find(roots, go => go.name == "Enemy").GetComponent<Entity>();
            Camera playerCamera = System.Array.Find(roots, go => go.name == "Main Camera").GetComponent<Camera>();
            Camera enemyCamera = System.Array.Find(roots, go => go.name == "Enemy Camera").GetComponent<Camera>();
            PlayerInputSource source = player.Brain.PlayerSource!;
            var enemyInput = enemy.GetComponent<UnityEngine.InputSystem.PlayerInput>();
            Assert.IsNotNull(enemyInput.actions);
            Assert.AreEqual(UnityEngine.InputSystem.PlayerNotifications.SendMessages, enemyInput.notificationBehavior);
            Assert.AreEqual("Player", enemyInput.defaultActionMap);
            Assert.IsTrue(playerCamera.enabled);
            Assert.IsFalse(enemyCamera.enabled);

            // 模拟 V / LB 指令而不是直接调用起手，覆盖统一的 Brain 消费路径。
            player.Commands.PossessionQueued = true;
            yield return null;
            yield return null; // 观察已完成 LateUpdate 的相机状态。
            Assert.IsTrue(enemy.Brain.InputSource is PlayerInputSource);
            Assert.IsTrue(player.Brain.InputSource is AITreeInputSource);
            Assert.IsTrue(enemyInput.enabled);
            Assert.IsFalse(playerCamera.enabled);
            Assert.IsTrue(enemyCamera.enabled);
            Assert.AreEqual(-1f, player.Brain.Modifiers.GetRemaining(source.DebugSoulOutEffect!));

            enemy.Brain.Modifiers.Tick(11f);
            yield return null;
            yield return null; // 观察已完成 LateUpdate 的相机状态。
            Assert.IsTrue(player.Brain.InputSource is PlayerInputSource);
            Assert.IsTrue(enemy.Brain.InputSource is AITreeInputSource);
            Assert.IsFalse(enemyInput.enabled);
            Assert.IsTrue(playerCamera.enabled);
            Assert.IsFalse(enemyCamera.enabled);
            Assert.IsFalse(player.Brain.HasSoulOut);
        }
        finally
        {
            // UnityTearDown 等待卸载，包含断言失败路径。
        }
        yield return null;
    }
}
