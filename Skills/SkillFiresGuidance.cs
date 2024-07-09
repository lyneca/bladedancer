using System.Collections;
using ThunderRoad;
using ThunderRoad.Skill;
using ThunderRoad.Skill.SpellPower;
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
    [ModOption("Fire's Guidance Allow Throw", "Whether to allow throwing the dagger when you release your hand",
        defaultValueIndex = 1)]
    public static bool allowThrow;

    [SkillCategory("Fire's Guidance", Category.Base | Category.Fire, 1)]
    [ModOptionFloatValuesDefault(0f, 20f, 1f, 5f)]
    [ModOptionSlider, ModOption("Fire's Guidance Throw Force", "Force added to the dagger on throw")]
    public static float throwForce;

    [SkillCategory("Fire's Guidance", Category.Base | Category.Fire, 1)]
    [ModOptionFloatValuesDefault(0f, 20f, 0.01f, 1f)]
    [ModOptionSlider, ModOption("Fire's Guidance Haptic Max Angle", "Duration over which guidance 'kicks in' after throw")]
    public static float hapticAngleAmount;

    public string throwEffectId;
    protected EffectData throwEffectData;

    public string grabEffectId;
    protected EffectData grabEffectData;
    public override void OnCatalogRefresh() {
        base.OnCatalogRefresh();
        spellHashId = Animator.StringToHash(spellId.ToLower());
        throwEffectData = Catalog.GetData<EffectData>(throwEffectId);
        grabEffectData = Catalog.GetData<EffectData>(grabEffectId);
    }

    public override void OnSpellLoad(SpellData spell, SpellCaster caster = null) {
        base.OnSpellLoad(spell, caster);
        if (caster?.mana.creature.isPlayer != true || spell is not SpellCastBlade blade) return;
        blade.OnBladeThrowEvent += OnBladeThrow;
    }

    public override void OnSpellUnload(SpellData spell, SpellCaster caster = null) {
        base.OnSpellUnload(spell, caster);
        if (caster?.mana.creature.isPlayer != true || spell is not SpellCastBlade blade) return;
        blade.OnBladeThrowEvent -= OnBladeThrow;
    }

    private void OnBladeThrow(SpellCastBlade spell, Vector3 velocity, Blade blade) {
        if (blade.ImbuedWith(spellHashId)
            && spell.spellCaster.ragdollHand?.creature?.isPlayer == true
            && !spell.spellCaster.ragdollHand.playerHand.controlHand.gripPressed) {
            blade.StartCoroutine(BladeThrowRoutine(spell, blade));
        }
    }

    public IEnumerator BladeThrowRoutine(SpellCastBlade spell, Blade blade) {
        var caster = spell.spellCaster;
        caster.telekinesis.Disable(spell);
        float startTime = Time.unscaledTime;
        var didGrip = false;
        bool wasUnPressed = false;
        while (Time.unscaledTime - startTime < gripWindow) {
            if (caster.ragdollHand.playerHand.controlHand.gripPressed && caster.ragdollHand.grabbedHandle == null) {
                didGrip = true;
                break;
            }

            if (!caster.ragdollHand.playerHand.controlHand.usePressed) wasUnPressed = true;

            if ((wasUnPressed && caster.ragdollHand.playerHand.controlHand.usePressed)
                || caster.spellInstance == null
                || caster.ragdollHand.grabbedHandle != null) {
                break;
            }

            yield return 0;
        }

        if (didGrip) {
            caster.ragdollHand.HapticTick();
            grabEffectData?.Spawn(caster.magicSource).Play();
            blade.StartGuidance(spell.spellCaster.ragdollHand, guidanceSpeed);
            if (allowThrow)
                blade.OnGuidanceStop += Throw;
        }

        caster.telekinesis.Enable(spell);
    }

    public void Throw(Blade blade, bool ungrab) {
        if (blade == null) return;
        blade.OnGuidanceStop -= Throw;
        if (ungrab && blade.item.isFlying && blade.guidanceHand is RagdollHand hand && hand.Velocity().magnitude > SpellCaster.throwMinHandVelocity) {
            throwEffectData?.Spawn(blade.transform.position, blade.transform.rotation).Play();
            blade.AddForce(hand.Velocity() * throwForce, ForceMode.VelocityChange, true, true);
        }
    }
}