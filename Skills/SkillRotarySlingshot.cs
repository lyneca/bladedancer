﻿using System.Collections.Generic;
using ThunderRoad;
using ThunderRoad.DebugViz;
using ThunderRoad.Skill;
using UnityEngine;

namespace Bladedancer.Skills; 

public class SkillRotarySlingshot : SpellSkillData {
    [SkillCategory("Rotary Slingshot", Category.Base, 2)]
    [ModOptionFloatValues(0.0f, 1.5f, 0.25f)]
    [ModOptionSlider, ModOption("Slingshot Max Draw Length", "How much you need to draw the bow to hit max velocity.", defaultValueIndex = 3)]
    public static float drawLength = 0.75f;
    
    [SkillCategory("Rotary Slingshot", Category.Base, 2)]
    [ModOptionFloatValues(60f, 160f, 20f)]
    [ModOptionSlider, ModOption("Slingshot Full Fire Velocity", "Projectile shoot speed for all blades at once, based on how far you draw.", defaultValueIndex = 1)]
    public static float velocityMultiplier = 80f;
    
    [SkillCategory("Rotary Slingshot", Category.Base, 2)]
    [ModOptionFloatValues(20f, 1600f, 20f)]
    [ModOptionSlider, ModOption("Slingshot Quick Fire Velocity", "Projectile shoot speed for single blades, based on how far you draw.", defaultValueIndex = 1)]
    public static float quickFireVelocity = 40f;

    public float minFireMagnitude = 0.1f;

    public string snickEffectId;
    public string fireEffectId;
    public string fireAllEffectId;
    
    public EffectData snickEffectData;
    public EffectData fireEffectData;
    public EffectData fireAllEffectData;

    public override void OnCatalogRefresh() {
        base.OnCatalogRefresh();
        snickEffectData = Catalog.GetData<EffectData>(snickEffectId);
        fireEffectData = Catalog.GetData<EffectData>(fireEffectId);
        fireAllEffectData = Catalog.GetData<EffectData>(fireAllEffectId);
    }

    public override void OnSkillLoaded(SkillData skillData, Creature creature) {
        base.OnSkillLoaded(skillData, creature);
        SpellCastBlade.handleEnabled = true;
    }

    public override void OnSkillUnloaded(SkillData skillData, Creature creature) {
        base.OnSkillUnloaded(skillData, creature);
        SpellCastBlade.handleEnabled = false;
    }

    public override void OnSpellLoad(SpellData spell, SpellCaster caster = null) {
        base.OnSpellLoad(spell, caster);
        if (spell is not SpellCastBlade slingblade
            || caster == null
            || !caster.gameObject.TryGetOrAddComponent(out RotarySlingshot slicer)) return;
        slicer.Init(this, slingblade);
    }

    public override void OnSpellUnload(SpellData spell, SpellCaster caster = null) {
        base.OnSpellUnload(spell, caster);
        if (spell is not SpellCastBlade
            || caster == null
            || !caster.gameObject.TryGetOrAddComponent(out RotarySlingshot slicer)) return;
        slicer.End();
    }
}


public class RotarySlingshot : ThunderBehaviour {
    public List<Blade> blades;
    private SpellCastBlade spell;
    private SkillRotarySlingshot skill;
    public Transform anchor;

    public override ManagedLoops EnabledManagedLoops => ManagedLoops.Update;
    public bool onGrabHasRun;

    public bool Ready => spell != null
                         && spell.slingshotHandle != null
                         && spell.slingshotHandle.IsHanded()
                         && spell.spellCaster != null
                         && spell.quiver != null
                         && onGrabHasRun;

    public float DrawAmount => Mathf.InverseLerp(0, SkillRotarySlingshot.drawLength,
        (spell.spellCaster.magicSource.position - spell.slingshotHandle.transform.position).magnitude);

    public int TargetBladeCount => Ready
        ? Mathf.RoundToInt(DrawAmount * spell.quiver.Max)
        : 0;

    public void Init(SkillRotarySlingshot skill, SpellCastBlade spell) {
        this.spell = spell;
        this.skill = skill;
        anchor = new GameObject().transform;
        anchor.transform.SetPositionAndRotation(spell.spellCaster.ragdollHand.transform.position,
            Quaternion.LookRotation(spell.spellCaster.magicSource.position - spell.slingshotHandle.transform.position,
                spell.spellCaster.ragdollHand.ThumbDir));
        blades = new List<Blade>();
        spell.OnHandleGrabEvent -= OnHandleGrab;
        spell.OnHandleGrabEvent += OnHandleGrab;
        spell.slingshotHandle.OnHeldActionEvent -= OnHeldAction;
        spell.slingshotHandle.OnHeldActionEvent += OnHeldAction;
    }

    public void End() {
        onGrabHasRun = false;
        spell.OnHandleGrabEvent -= OnHandleGrab;
        if (spell.slingshotHandle)
            spell.slingshotHandle.OnHeldActionEvent -= OnHeldAction;
    }
    
    public Vector3 FireVector {
        get {
            var handleVector = spell.spellCaster.magicSource.position
                               - spell.slingshotHandle.transform.position;
            return Vector3.Slerp(handleVector.normalized, spell.spellCaster.ragdollHand.PointDir, 0.5f)
                   * handleVector.magnitude;
        }
    }

    protected override void ManagedUpdate() {
        base.ManagedUpdate();
        if (!Ready) return;

        anchor.transform.SetPositionAndRotation(spell.spellCaster.ragdollHand.transform.position,
            Quaternion.LookRotation(FireVector, spell.spellCaster.ragdollHand.ThumbDir));

        int target = TargetBladeCount;
        if (blades.Count == target) return;

        var needsRefresh = false;
        if (blades.Count < target && spell.quiver.TryGetClosestBlade(spell.spellCaster.transform.position, out var blade, scale: false)) {
            skill.snickEffectData?.Spawn(spell.spellCaster.magicSource).Play();
            blades.Add(blade);
            needsRefresh = true;
            blade.IgnoreBlades(blades);
        } else if (blades.Count > target && blades.Count > 0) {
            blades[0].IgnoreBlades(blades, false);
            blades[0].ReturnToQuiver(spell.quiver);
            blades.RemoveAt(0);
            needsRefresh = true;
        }

        if (!needsRefresh) return;
        float amount = DrawAmount;
        spell.spellCaster.ragdollHand.HapticTick(Mathf.Lerp(0.2f, 0.8f, amount));
        spell.spellCaster.other.ragdollHand.HapticTick(amount);
        Refresh();
    }

    public void Refresh() {
        for (var i = 0; i < blades.Count; i++) {
            blades[i].MoveTo(GetBladeTarget(i, blades.Count));
        }
    }

    public MoveTarget GetBladeTarget(int i, int count) {
        var rotatedPosition = Quaternion.AngleAxis(360f / count * i, Vector3.forward)
                              * new Vector3(0, 0.15f, 0.05f);
        return new MoveTarget(MoveMode.PID, 12)
            .Parent(anchor)
            .At(rotatedPosition,
                Quaternion.LookRotation(Vector3.forward, Quaternion.AngleAxis(90, Vector3.forward) * -rotatedPosition))
            .Scale(ScaleMode.Scaled);
    }

    public void Fire(Blade blade, Vector3 velocity, bool single) {
        float mult = single ? SkillRotarySlingshot.quickFireVelocity : SkillRotarySlingshot.velocityMultiplier;
        blade.Release();
        blade.AddForce(velocity * mult, ForceMode.VelocityChange, false, true);
    }

    private void OnHandleGrab(SpellCastBlade spell, Handle handle, EventTime time) {
        if (time == EventTime.OnStart) {
            if (spell.activeBlade) blades.Add(spell.activeBlade);
            spell.ReleaseBlade();
            if (spell.spellCaster.other.spellInstance is SpellCastBlade otherSpell) {
                otherSpell.disableHandle.Add(this);
            }
            onGrabHasRun = true;
            Refresh();
        } else {
            if (spell.spellCaster.other.spellInstance is SpellCastBlade otherSpell) {
                otherSpell.disableHandle.Remove(this);
            }
            onGrabHasRun = false;
            var vector = FireVector;
            if (vector.magnitude > skill.minFireMagnitude) {
                for (int i = blades.Count - 1; i >= 0; i--) {
                    var blade = blades[i];
                    Fire(blade, vector, false);
                }

                if (blades.Count > 0) {
                    spell.spellCaster.other.ragdollHand.playerHand.controlHand.HapticPlayClip(Catalog.gameData.haptics
                        .bowShoot);
                    skill.fireAllEffectData?.Spawn(spell.spellCaster.transform).Play();

                    this.RunAfter(() => { spell.spellCaster.ragdollHand.HapticTick(0.6f); },
                        Mathf.Lerp(0.0f, 0.05f, DrawAmount));
                }
            } else {
                for (int i = blades.Count - 1; i >= 0; i--) {
                    blades[i].ReturnToQuiver(this.spell.quiver);
                }
            }

            blades.Clear();
            Refresh();
        }
    }

    private void OnHeldAction(RagdollHand hand, Interactable.Action action) {
        if (action != Interactable.Action.UseStart || blades.Count == 0) return;
        var blade = blades[0];
        blades.Remove(blade);
        var vector = FireVector;
        skill.fireEffectData?.Spawn(blade.transform).Play();
        Fire(blade, vector, true);
        spell.spellCaster.other.ragdollHand.HapticTick();
        this.RunAfter(() => {
            spell.spellCaster.ragdollHand.HapticTick(0.6f);
        }, Mathf.Lerp(0.0f, 0.1f, DrawAmount));
        Refresh();
    }
}