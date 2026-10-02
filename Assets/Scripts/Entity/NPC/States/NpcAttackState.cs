using UnityEngine;

/// <summary>
/// 攻击（Action 层）：前摇 → 命中帧 → 后摇 的计时骨架，暂无判定。
/// 时长读 CharacterEquipment（角色定义基础值 ÷ 武器攻速）——多角色/多武器手感差异全在数据。
/// LocksMovement 声明锁移动——Locomotion 层的 ApplyLocomotion 消费（站桩贴地）。
/// 起手指令（QueuedSkillSlot）不区分玩家按键与 AI 决策——指令层汇流。
/// M3 将重写为读连招表的通用攻击状态。
/// 已知取舍：眩晕（CC 压制本层）期间计时照走，解除时若已过总时长则当帧结束——
/// 正式的打断语义（清除本层）由 M2.5 效果容器负责。
/// </summary>
public sealed class NpcAttackState : NpcStateBase
{
    private float startTime;

    public NpcAttackState(NpcBlackboard board, StateMachine<NpcBlackboard> machine) : base(board, machine) { }

    public override StateLayer Layer => StateLayer.Action;

    public override string StateName => "Attack";

    /// <summary>攻击期间锁移动（Locomotion 层的 ApplyLocomotion 读取）</summary>
    public override bool LocksMovement => true;

    /// <summary>当前阶段名（调试面板显示用）</summary>
    public string Phase
    {
        get
        {
            float elapsed = Time.time - startTime;
            CharacterEquipment equipment = Board.Controller.Equipment;
            if (elapsed < equipment.AttackWindup)
            {
                return "前摇";
            }
            if (elapsed < equipment.AttackWindup + equipment.AttackHit)
            {
                return "命中";
            }
            return "后摇";
        }
    }

    public override void Enter()
    {
        startTime = Time.time;
    }

    public override void HandleTransitions()
    {
        CharacterEquipment equipment = Board.Controller.Equipment;
        float total = equipment.AttackWindup + equipment.AttackHit + equipment.AttackRecovery;
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
