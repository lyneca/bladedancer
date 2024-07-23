﻿using ThunderRoad;
using ThunderRoad.Skill;

namespace Bladedancer.Skills; 

public class SkillVersatility : SpellSkillData {
    [SkillCategory("General")]
    [ModOptionSlider, ModOptionFloatValues(0, 1, 0.1f)]
    [ModOption("Versatility Target Weapon Size", "Target size that weapons adapted into your quiver will try to shrink to.")]
    public static float targetWeaponSize = 0.3f;

    public override void OnSpellLoad(SpellData spell, SpellCaster caster = null) {
        base.OnSpellLoad(spell, caster);
        if (spell is SpellCastBlade blade) blade.imbueEnabled = true;
    }

    public override void OnSpellUnload(SpellData spell, SpellCaster caster = null) {
        base.OnSpellUnload(spell, caster);
        if (spell is SpellCastBlade blade) blade.imbueEnabled = false;
    }
}
