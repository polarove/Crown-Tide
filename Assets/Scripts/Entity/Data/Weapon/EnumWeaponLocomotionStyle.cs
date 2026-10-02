using System;
using System.Collections.Generic;
using System.Text;

namespace Assets.Scripts.Entity.Data.Weapon
{
    /// <summary>
    /// 持械步态（出招表的动画选型键）：决定 Idle/Walk/Sprint/Dodge 用哪套动画。
    /// 本轮 Presentation 层（EntityVisual）只读这个键，不接动画——接动画时按键映射动画剪辑
    /// </summary>
    public enum EnumWeaponLocomotionStyle
    {
        Fist = 0,    // 拳套
        Light = 1,   // 轻武器
        Heavy = 2,   // 重武器
        Dual = 3,    // 双持
    }
}
