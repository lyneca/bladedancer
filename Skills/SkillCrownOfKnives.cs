using ThunderRoad;
using ThunderRoad.Skill;

namespace Bladedancer.Skills; 

public class SkillCrownOfKnives : SpellSkillData {
    public override void OnSpellLoad(SpellData spell, SpellCaster caster = null) {
        base.OnSpellLoad(spell, caster);
        if (spell is SpellCastSlingblade blade) {
            blade.quiverEnabled = true;
        }
    }

    public override void OnSpellUnload(SpellData spell, SpellCaster caster = null) {
        base.OnSpellUnload(spell, caster);
        if (spell is SpellCastSlingblade blade) {
            blade.quiverEnabled = true;
        }
    }
}
