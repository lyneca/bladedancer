using ThunderRoad;
using ThunderRoad.Skill;

namespace Bladedancer.Skills; 

public class SkillVersatility : SkillData {
    [SkillCategory("General")]
    [ModOptionSlider, ModOptionFloatValuesDefault(0, 1, 0.1f, 0.3f)]
    [ModOption("Versatility Target Weapon Size", "Target size that weapons adapted into your quiver will try to shrink to.")]
    public static float targetWeaponSize;
}
