using UnityEngine;

/// <summary>
/// 攻击（Action 层）：前摇 → 命中帧 → 后摇 的计时骨架，暂无判定。
/// 时长 = CharacterEquipment（角色基础 ÷ 武器攻速）再 ÷ 效果攻速乘数——
/// 多角色/多武器/急速的手感差异全在数据（AttackDurationScale 一处缩放）。
/// LocksMovement 声明锁移动——Locomotion 层的 ApplyLocomotion 消费（站桩贴地）。
/// 起手指令（QueuedSkillSlot）不区分玩家按键与 AI 决策——指令层汇流。
/// 挥剑标记：Enter 挂 / Exit 摘 Swinging 标签——写方只命名不赋义，读方（武器 SO）是解释器：
/// CharacterEquipment.SwingDamageTakenMultiplier 据此给挥剑期间受伤乘数（默认 1 = 纯标记，
/// 武器重写即获得效果——吸血/破甲将来同样挂读）。
/// 失控打断：效果容器推 CC 层时会 ClearState(Action)（走 Exit 摘标记）——攻击不会在眩晕后"续播"。
/// M3 将重写为读连招表的通用攻击状态。
/// </summary>
public sealed class NpcAttackState : NpcStateBase
{
    private float startTime;

    public NpcAttackState(NpcBlackboard board, StateMachine<NpcBlackboard> machine) : base(board, machine) { }

    public override StateLayer Layer => StateLayer.Action;

    public override string StateName => "Attack";

    /// <summary>攻击期间锁移动（Locomotion 层的 ApplyLocomotion 读取）</summary>
    public override bool LocksMovement => true;

    /// <summary>攻速缩放：时长 = 基础 ÷ 攻速乘数。乘数钳下限 0.05 防除零（效果配置事故不卡死编辑器）</summary>
    private float AttackDurationScale => 1f / Mathf.Max(0.05f, Board.Controller.GetStatMultiplier(StatType.AttackSpeed));

    /// <summary>当前阶段名（调试面板显示用）</summary>
    public string Phase
    {
        get
        {
            float elapsed = Time.time - startTime;
            CharacterEquipment equipment = Board.Controller.Equipment;
            float scale = AttackDurationScale;
            if (elapsed < equipment.AttackWindup * scale)
            {
                return "前摇";
            }
            if (elapsed < (equipment.AttackWindup + equipment.AttackHit) * scale)
            {
                return "命中";
            }
            return "后摇";
        }
    }

    public override void Enter()
    {
        startTime = Time.time;
        Board.Tags.Add((ulong)EntityTag.Swinging);   // 挂挥剑标记（读方解释，见头注释）
    }

    public override void Exit()
    {
        Board.Tags.Remove((ulong)EntityTag.Swinging);   // 摘标记：打完/被打断（失控清层走的就是这里）
    }

    public override void HandleTransitions()
    {
        CharacterEquipment equipment = Board.Controller.Equipment;
        float scale = AttackDurationScale;
        float total = (equipment.AttackWindup + equipment.AttackHit + equipment.AttackRecovery) * scale;
        if (Time.time >= startTime + total)
        {
            // 打完整套：清空 Action 层（层回到未激活 = 无动作）
            Machine.ClearState(Layer);
        }
    }

    public override void Tick()
    {
        // M1 占位：命中帧无判定（M3 接球形 Overlap）；移动由 Locomotion 层锁住后站桩
    }
}
