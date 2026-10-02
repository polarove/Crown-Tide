using System;
using System.Collections.Generic;
using System.Text;

namespace Assets.Scripts.Entity.Data.Weapon
{

    /// <summary>
    /// 武器类型
    /// 值是武器需要的容量（handCost），用于容量判定
    /// </summary>
    public enum EnumWeaponType
    {
        /// <summary>
        /// 双拳
        /// </summary>
        Fist = 0,

        /// <summary>
        /// 匕首
        /// </summary>
        Dagger = 2,

        /// <summary>
        /// 巨斧
        /// </summary>
        GreatAxe = 4,

        /// <summary>
        /// 圣剑
        /// </summary>
        HolySword = 9,
    }
}
