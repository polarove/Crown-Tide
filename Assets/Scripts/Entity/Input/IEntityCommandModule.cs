/// <summary>可选指令扩展。编排者只调用接口，不负责扩展的输入或具体效果。</summary>
public interface IEntityCommandModule
{
    void GatherCommands(Entity entity, CommandBuffer commands);
}
