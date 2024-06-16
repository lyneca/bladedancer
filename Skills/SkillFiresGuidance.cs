using System.Collections;
using ThunderRoad;
using ThunderRoad.Skill;
using UnityEngine;

namespace Bladedancer.Skills;

public class SkillFiresGuidance : SpellSkillData {
    public string spellId = "Fire";
    public static int spellHashId;

    [SkillCategory("Fire's Guidance", Category.Base | Category.Fire, 1)]
    [ModOptionFloatValuesDefault(5f, 30f, 5f, 15f)]
    [ModOptionSlider, ModOption("Fire's Guidance Speed", "Speed of guided fire-imbued daggers")]
    public static float guidanceSpeed;
    
    [SkillCategory("Fire's Guidance", Category.Base | Category.Fire, 1)]
    [ModOptionFloatValuesDefault(0f, 0.5f, 0.1f, 0.2f)]
    [ModOptionSlider, ModOption("Fire's Guidance Grip Window", "Length of the time window you have to grip for guidance to start")]
    public static float gripWindow;

    [SkillCategory("Fire's Guidance", Category.Base | Category.Fire, 1)]
    [ModOptionFloatValuesDefault(0f, 0.5f, 0.1f, 0.2f)]
    [ModOptionSlider, ModOption("Fire's Guidance Warmup Delay", "Duration over which guidance 'kicks in' after throw")]
    public static float guidanceDelay;

    public override void OnCatalogRefresh() {
        base.OnCatalogRefresh();
        spellHashId = Animator.StringToHash(spellId.ToLower());
    }

    public override void OnSpellLoad(SpellData spell, SpellCaster caster = null) {
        base.OnSpellLoad(spell, caster);
        if (caster?.mana.creature.isPlayer != true || spell is not SpellCastSlingblade blade) return;
        blade.OnBladeThrowEvent += OnBladeThrow;
    }

    public override void OnSpellUnload(SpellData spell, SpellCaster caster = null) {
        base.OnSpellUnload(spell, caster);
        if (caster?.mana.creature.isPlayer != true || spell is not SpellCastSlingblade blade) return;
        blade.OnBladeThrowEvent -= OnBladeThrow;
    }

    private void OnBladeThrow(SpellCastSlingblade spell, Vector3 velocity, Blade blade) {
        if (blade.ImbuedWith(spellHashId)
            && spell.spellCaster.ragdollHand?.creature?.isPlayer == true
            && !spell.spellCaster.ragdollHand.playerHand.controlHand.gripPressed) {
            blade.StartCoroutine(BladeThrowRoutine(spell, blade));
        }
    }

    public IEnumerator BladeThrowRoutine(SpellCastSlingblade spell, Blade blade) {
        var caster = spell.spellCaster;
        caster.telekinesis.Disable(spell);
        float startTime = Time.unscaledTime;
        var didGrip = false;
        while (Time.unscaledTime - startTime < gripWindow) {
            if (caster.ragdollHand.playerHand.controlHand.gripPressed && caster.ragdollHand.grabbedHandle == null) {
                didGrip = true;
                break;
            }

            if (caster.ragdollHand.playerHand.controlHand.usePressed
                || caster.spellInstance.hashId != spell.hashId
                || caster.ragdollHand.grabbedHandle != null) {
                break;
            }

            yield return 0;
        }

        if (didGrip) {
            blade.StartGuidance(spell.spellCaster.ragdollHand, guidanceSpeed, guidanceDelay);
        }

        caster.telekinesis.Enable(spell);
    }
}