/// <summary>
/// 黑板基类：实体状态机各状态共享数据的载体（黑板模式）。
/// 纯数据类，不继承 MonoBehaviour；由具体实体（玩家/敌人/召唤物）派生自己的黑板，
/// 持有该类实体需要共享的数据与服务引用。框架层不依赖任何 Unity 玩法类型。
/// </summary>
public abstract class Blackboard
{
}
