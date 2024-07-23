using ThunderRoad;
using ThunderRoad.Skill;

namespace Bladedancer.Skills; 

public class SkillKnifethief : SpellSkillData {
    [SkillCategory("Knifethief", Category.Base | Category.Mind, 1)]
    [ModOptionFloatValues(0.1f, 2f, 0.1f)]
    [ModOptionSlider, ModOption("Knifethief Grab Radius", "Radius in which Knifethief allows you to steal thrown daggers.")]
    public static float grabRadius = 0.7f;

    public override void OnSpellLoad(SpellData spell, SpellCaster caster = null) {
        base.OnSpellLoad(spell, caster);
        if (spell is SpellCastBlade blade)
            blade.stealIfNearby = true;
    }

    public override void OnSpellUnload(SpellData spell, SpellCaster caster = null) {
        base.OnSpellUnload(spell, caster);
        if (spell is SpellCastBlade blade)
            blade.stealIfNearby = false;
    }
}
