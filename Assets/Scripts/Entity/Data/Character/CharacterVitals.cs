using System;
using UnityEngine;

/// <summary>
/// 生命与资源（Data 层，Entity 的"体质数据库"）：HP + 信心值 + 死亡事实。
/// 旧 EntityHealth 并入此处（对抗审查裁决）：HP 周边只有"数据 + 唯一写入口 + 死亡事实"，
/// 拆两个组件会把同一份不变量劈成两处缓存。
/// 只管数值不变量（钳 [0, MaxHp]、死亡幂等），不认识伤害来源与修饰链——
/// 修饰（DamageTaken 乘数 × 挥剑减伤）在调用侧（EntityBrain.TakeDamage）算好传终值，
/// Data 层不反向依赖 Logic。
/// 表现层经事件订阅（HpChanged/Died——变更才触发，不占每帧路径，为将来网络表现重放留形）。
/// 死亡后不死锁组件：管线由 Brain 的死亡门拦截（多人纪律：enabled=false 会杀掉网络回调）。
/// </summary>
public sealed class CharacterVitals : MonoBehaviour
{
    /// <summary>HP 变更事件（扣血/回血后触发；参数 = 本体，读 CurrentHp 取新值）</summary>
    public event Action<CharacterVitals> HpChanged;

    /// <summary>死亡事件（幂等：一实体只触发一次）</summary>
    public event Action<CharacterVitals> Died;

    /// <summary>角色配置（Entity.Awake 注入；空 = 用内置默认值，防御未配置的实体）</summary>
    private CharacterConfigSO Config;

    /// <summary>信心值资源（Initialize 构造；技能闸门之一，见 SkillSlot）</summary>
    public SkillResource Faith { get; private set; }

    /// <summary>当前生命（写口唯一：ApplyDamage/ApplyHeal）</summary>
    public float CurrentHp { get; private set; }

    /// <summary>是否已死亡（死亡幂等门；Brain 管线据此拦截）</summary>
    public bool IsDead { get; private set; }

    /// <summary>最大生命（config 活值直读；未配置默认 100——Play 模式改资产即时生效，不回填当前值）</summary>
    public float MaxHp => Config != null ? Config.MaxHealth : float.MaxValue;

    /// <summary>信心区间上界（config 活值直读；未配置默认 67；区间 = [-上界, +上界] 对称）</summary>
    public int FaithCapacity => Config != null ? Config.FaithCapacity : 67;

    /// <summary>初始化（Entity.Awake → Brain.Bootstrap 调用一次）：满血、信心居中 0。
    /// 重复调用（测试/复活预留）会重置 HP 与信心值并清除死亡态</summary>
    public void Initialize(CharacterConfigSO characterConfig)
    {
        Config = characterConfig;
        CurrentHp = MaxHp;
        Faith = new SkillResource(this, 0);   // 居中：开局冠冕/潮汐都放得出（钟摆两侧等距）
        IsDead = false;
    }

    /// <summary>受到伤害（唯一扣血写口）：只收"调用侧算好的终值"，钳到 [0, MaxHp]。
    /// 死亡幂等：已死直接忽略。amount <= 0 忽略（治疗走 ApplyHeal）</summary>
    public void ApplyDamage(float finalAmount)
    {
        if (IsDead || finalAmount <= 0f)
        {
            return;
        }

        CurrentHp = Mathf.Clamp(CurrentHp - finalAmount, 0f, MaxHp);
        HpChanged?.Invoke(this);
        if (CurrentHp <= 0f)
        {
            IsDead = true;
            Died?.Invoke(this);
        }
    }

    /// <summary>治疗（唯一回血写口）：钳到 [0, MaxHp]；已死忽略（复活功能后置，到时走 Initialize 重置）</summary>
    public void ApplyHeal(float amount)
    {
        if (IsDead || amount <= 0f)
        {
            return;
        }

        CurrentHp = Mathf.Clamp(CurrentHp + amount, 0f, MaxHp);
        HpChanged?.Invoke(this);
    }
}
