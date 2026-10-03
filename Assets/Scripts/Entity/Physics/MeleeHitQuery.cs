using System;
using UnityEngine;

/// <summary>纯 Physics 查询，缓冲自动扩容，不认识 Entity、敌我或技能。</summary>
public sealed class MeleeHitQuery
{
    private Collider[] Buffer = new Collider[16];

    public int Overlap(Vector3 center, float radius, int layers, bool includeTriggers, out Collider[] results)
    {
        results = Buffer;
        if (radius <= 0f || float.IsNaN(radius) || float.IsInfinity(radius) || layers == 0
            || !IsFinite(center.x) || !IsFinite(center.y) || !IsFinite(center.z)) return 0;
        QueryTriggerInteraction trigger = includeTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore;
        int count;
        while ((count = Physics.OverlapSphereNonAlloc(center, radius, Buffer, layers, trigger)) == Buffer.Length)
            Array.Resize(ref Buffer, checked(Buffer.Length * 2));
        results = Buffer;
        return count;
    }

    /// <summary>水平扇形柱体：宽相盒查询，再按碰撞体最近点筛半径与水平夹角。</summary>
    public int OverlapSector(Vector3 center, Vector3 forward, float radius, float angle, float height,
        int layers, bool includeTriggers, out Collider[] results)
    {
        results = Buffer;
        if (!IsFinite(radius) || radius <= 0f || !IsFinite(angle) || angle <= 0f || angle > 360f
            || !IsFinite(height) || height <= 0f || layers == 0
            || !IsFinite(center.x) || !IsFinite(center.y) || !IsFinite(center.z)
            || !IsFinite(forward.x) || !IsFinite(forward.y) || !IsFinite(forward.z)) return 0;
        forward.y = 0f;
        if (forward.sqrMagnitude < .000001f) return 0;
        forward.Normalize();
        QueryTriggerInteraction trigger = includeTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore;
        Vector3 halfExtents = new(radius, height * .5f, radius);
        int count;
        while ((count = Physics.OverlapBoxNonAlloc(center, halfExtents, Buffer, Quaternion.identity, layers, trigger)) == Buffer.Length)
            Array.Resize(ref Buffer, checked(Buffer.Length * 2));
        float minimumDot = Mathf.Cos(angle * .5f * Mathf.Deg2Rad);
        int accepted = 0;
        for (int i = 0; i < count; i++)
        {
            Collider collider = Buffer[i];
            Vector3 delta = collider.ClosestPoint(center) - center;
            delta.y = 0f;
            float distanceSquared = delta.sqrMagnitude;
            if (distanceSquared > radius * radius) continue;
            // 中心已在碰撞体内也算相交；360度配置不受朝向约束。
            if (distanceSquared > .000001f && angle < 360f
                && Vector3.Dot(delta.normalized, forward) < minimumDot - .00001f) continue;
            Buffer[accepted++] = collider;
        }
        results = Buffer;
        return accepted;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
