using UnityEngine;

/// <summary>
/// 装备组件（玩家与 NPC 同挂，实体必挂——EntityController 上 RequireComponent）：
/// 持有当前角色定义与武器，提供组合后的最终参数查询。
/// NPC 强度 = 这里配了什么（装什么会什么）；玩家操作不同角色 = 换 character 引用。
/// 字段可空：未拖资产时用内置默认（等价于占位值/空手攻速 1.0），方便渐进接入。
/// M2 后续：饰品槽、技能槽数组也挂这里；M2.5 攻速 buff 在查询处再乘一层。
/// </summary>
public class CharacterEquipment : MonoBehaviour
{
    [Header("角色")]
    [Tooltip("当前角色定义：基础攻击节奏等；空 = 内置默认")]
    public CharacterDefinition character;

    [Header("武器")]
    [Tooltip("当前武器：攻速修正、（M3 起）伤害与连招表；空 = 空手（攻速 1.0）")]
    public WeaponDefinition weapon;

    // ---- 组合查询（状态/战斗系统读这里，不直接碰两张表）----

    /// <summary>前摇时长 = 角色基础 ÷ 武器攻速</summary>
    public float AttackWindup => (character != null ? character.attackWindup : 0.15f) / AttackSpeed;

    /// <summary>命中帧时长 = 角色基础 ÷ 武器攻速</summary>
    public float AttackHit => (character != null ? character.attackHit : 0.1f) / AttackSpeed;

    /// <summary>后摇时长 = 角色基础 ÷ 武器攻速</summary>
    public float AttackRecovery => (character != null ? character.attackRecovery : 0.3f) / AttackSpeed;

    /// <summary>武器攻速（空手或非法值 = 1）；÷ 语义：数值越大出手越快</summary>
    private float AttackSpeed => weapon != null && weapon.attackSpeed > 0f ? weapon.attackSpeed : 1f;
}
