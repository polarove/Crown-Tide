/// <summary>
/// 输入源接口（Input 层接缝）：回答"Entity 要做什么"——把设备/决策翻译成指令写进缓冲。
/// 实现：PlayerInputSource（读输入设备）、AITreeInputSource（跑决策树）、
/// 将来的 NetworkInputRelay（多人：转发远端玩家的指令镜像——仿真代码零改动换输入源）。
/// IsPlayerControlled 的唯一职责就是决定 EntityBrain 绑定哪个实现（需求钦定）。
/// 组合而非继承：输入源是可替换零件，不是角色种类。
/// </summary>
public interface IInputSource
{
    void GatherCommands(CommandBuffer commands);
}
