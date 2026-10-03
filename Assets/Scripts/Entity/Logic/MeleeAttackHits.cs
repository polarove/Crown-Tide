using System.Collections.Generic;
using UnityEngine;
using Assets.Scripts.Entity.Data.Skill;

/// <summary>一次攻击段的目标去重与结算。实例属于攻击状态，没有全局命中表。</summary>
public sealed class MeleeAttackHits
{
    private readonly Entity Attacker;
    private readonly MeleeHitQuery Query = new();
    private readonly HashSet<Entity> HitTargets = new();
    private uint Revision;

    public MeleeAttackHits(Entity attacker) => Attacker = attacker;
    public void BeginSegment()
    {
        HitTargets.Clear();
        Revision++;
    }

    public void Sample(ComboEntry entry, EnumSkillType? skillKind = null)
    {
        if (Attacker == null || Attacker.IsDead || !Attacker.isActiveAndEnabled
            || entry.MeleeDamage <= 0f || float.IsNaN(entry.MeleeDamage) || float.IsInfinity(entry.MeleeDamage)
            || !entry.IsHitVolumeValid()) return;
        Vector3 center = Attacker.transform.TransformPoint(entry.HitOffset);
        uint revision = Revision;
        Collider[] colliders;
        int count = entry.HitShape == EnumMeleeHitShape.Sector
            ? Query.OverlapSector(center, Attacker.transform.forward, entry.HitRadius, entry.HitAngle, entry.HitHeight,
                entry.HitLayers, entry.IncludeTriggers, out colliders)
            : Query.Overlap(center, entry.HitRadius, entry.HitLayers, entry.IncludeTriggers, out colliders);
        for (int i = 0; i < count; i++)
        {
            if (revision != Revision || Attacker.IsDead) return;
            Entity? target = colliders[i] != null ? colliders[i].GetComponentInParent<Entity>() : null;
            if (target == null || target == Attacker || target.IsDead || !target.isActiveAndEnabled
                || !HitTargets.Add(target)) continue;
            int sourceFaction = Attacker.Config?.FactionId ?? 0;
            int targetFaction = target.Config?.FactionId ?? 0;
            bool enemyHit = sourceFaction != 0 && targetFaction != 0 && sourceFaction != targetFaction;
            Attacker.Brain.ResolveHit(target, entry.MeleeDamage, enemyHit, skillKind);
        }
    }
}
