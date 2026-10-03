using Assets.Scripts.Entity.Data.Skill;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Debug 专属输入与执行模块；正式技能只提交请求，仲裁仍是纯查询。</summary>
public sealed class EntityDebugCommands : IEntityCommandModule
{
    private bool WarnedMissingModifiers;

    public void GatherCommands(Entity entity, CommandBuffer commands)
    {
        if (GameSettingsController.IsGameplayInputBlocked || !DebugSystem.IsEnabled || entity.Brain.InputSource is not PlayerInputSource source)
        {
            return;
        }
        Keyboard? keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.f3Key.wasPressedThisFrame)
        {
            ApplyModifier(entity, source.DebugStunModifier, "F3 眩晕");
        }
        if (keyboard.f4Key.wasPressedThisFrame)
        {
            ApplyModifier(entity, source.DebugHasteModifier, "F4 急速");
        }
        if (keyboard.f5Key.wasPressedThisFrame)
        {
            ApplyModifier(entity, source.DebugWoundModifier, "F5 创伤");
        }
        if (keyboard.f6Key.wasPressedThisFrame)
        {
            entity.Brain.Modifiers.Dispel(EnumModifierCategory.All);
        }
        if (keyboard.f7Key.wasPressedThisFrame && source.DebugStunModifier != null)
        {
            foreach (Entity other in Object.FindObjectsByType<Entity>())
            {
                if (other != entity)
                {
                    other.Brain.Modifiers.Apply(source.DebugStunModifier);
                }
            }
        }
        if (keyboard.f9Key.wasPressedThisFrame)
        {
            commands.SkillSlotQueued = (int)EnumSkillType.Crown;
        }
        if (keyboard.f11Key.wasPressedThisFrame)
        {
            commands.SkillSlotQueued = (int)EnumSkillType.Tide;
        }
    }

    private void ApplyModifier(Entity entity, ModifierEffect? modifier, string keyName)
    {
        if (modifier == null)
        {
            if (!WarnedMissingModifiers)
            {
                WarnedMissingModifiers = true;
                Debug.LogWarning($"{keyName}：调试槽未拖 ModifierEffect 资产（此警告只提示一次）");
            }
            return;
        }
        entity.Brain.Modifiers.Apply(modifier);
    }
}
