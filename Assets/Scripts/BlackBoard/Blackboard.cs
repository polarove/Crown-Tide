/// <summary>
/// 黑板基类：实体状态机各状态共享数据的载体（黑板模式）。
/// 纯数据类，不继承 MonoBehaviour；具体黑板目前只有一个：角色黑板 NpcBlackboard
/// （玩家与 NPC 共用：指令区 + 感知区 + 运动数据）。框架层不依赖任何 Unity 玩法类型。
/// </summary>
public abstract class Blackboard
{
}
