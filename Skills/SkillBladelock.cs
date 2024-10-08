﻿using ThunderRoad;
using ThunderRoad.Skill;
using UnityEngine;

namespace Bladedancer.Skills;

public class SkillBladelock : SpellSkillData {
    public override void OnSpellLoad(SpellData spell, SpellCaster caster = null) {
        base.OnSpellLoad(spell, caster);
        if (spell is not SpellCastBlade blade) return;
        target = null;
        targetBlade = null;
        blade.OnSpellUpdateLoopEvent -= OnUpdate;
        blade.OnSpellUpdateLoopEvent += OnUpdate;
        blade.OnBladeThrowEvent -= OnBladeThrow;
        blade.OnBladeThrowEvent += OnBladeThrow;
    }

    public override void OnSpellUnload(SpellData spell, SpellCaster caster = null) {
        base.OnSpellUnload(spell, caster);
        StopTarget(caster?.mana.creature);
        if (spell is not SpellCastBlade blade) return;
        blade.OnSpellUpdateLoopEvent -= OnUpdate;
        blade.OnBladeThrowEvent -= OnBladeThrow;
    }

    public ThunderEntity target;
    public Transform targetPoint;
    public Blade targetBlade;
    public float checkDelay = 0.2f;
    protected float lastCheck;

    public void OnUpdate(SpellCastCharge spell) {
        if (spell is not SpellCastBlade bladeSpell
            || spell.spellCaster?.ragdollHand?.playerHand?.controlHand?.gripPressed == true) {
            return;
        }

        var otherHand = spell.spellCaster?.ragdollHand?.otherHand;
        if (!otherHand
            || otherHand.playerHand == null
            || !otherHand.playerHand.controlHand.gripPressed
            || otherHand.playerHand.controlHand.usePressed
            || otherHand.grabbedHandle != null
            || otherHand.climb.isGripping
            || spell.spellCaster.other.telekinesis.catchedHandle != null) {
            StopTarget(spell.spellCaster?.mana.creature);
            return;
        }

        if (target == null || target is Creature { isKilled: true }) StopTarget(spell.spellCaster.mana.creature);

        if (Time.time - lastCheck < checkDelay) return;

        var newTarget = Creature.AimAssist(new Ray(otherHand.transform.position, otherHand.PointDir), 20, 10, out var newTargetPoint,
            Filter.EnemyOf(otherHand.creature), CreatureType.Golem);

        if (newTarget is Creature) newTargetPoint = null;

        if (target != null && newTarget == null || (newTarget == target && newTargetPoint == targetPoint)) return;
        StopTarget(spell.spellCaster.mana.creature);
        target = newTarget;
        targetPoint = newTargetPoint;
        StartTarget(spell.spellCaster.mana.creature, target, bladeSpell, targetPoint);
        if (Quiver.TryGet(spell.spellCaster.mana.creature, out var quiver) && quiver.Count > 0)
            otherHand.HapticTick();
    }

    public void StartTarget(
        Creature player,
        ThunderEntity entity,
        SpellCastBlade bladeSpell,
        Transform newTargetPoint) {
        if (entity == null) {
            target = null;
            return;
        }

        MoveTarget moveTarget = default;
        if (!bladeSpell.quiver.TryGetClosestBlade(entity.RootTransform.position, out var blade)) {
            target = null;
            targetPoint = null;
            return;
        }

        moveTarget = entity switch {
            Creature creature => new MoveTarget(MoveMode.PID, 10)
                .Parent(creature.ragdoll.headPart.transform, false)
                .At(Vector3.up * 0.5f)
                .LookAt(creature.ragdoll.headPart.transform),
            Golem golem => new MoveTarget(MoveMode.PID, 10)
                .Parent(newTargetPoint)
                .At(Vector3.up * 0.5f)
                .LookAt(newTargetPoint),
            _ => moveTarget
        };

        targetBlade = blade;
        blade.Release();
        blade.MoveTo(moveTarget);
        blade.AllowDespawn(false);
        blade.SetIntangible(true);
    }

    public void StopTarget(Creature player) {
        target = null;
        if (targetBlade == null) targetBlade = null;
        targetBlade?.SetIntangible(false);
        targetBlade?.ReturnToQuiver(player);
        targetBlade = null;
    }

    public void OnBladeThrow(SpellCastBlade spell, Vector3 velocity, Blade blade) {
        if (target != null) blade.HomeTo(target, target is Creature creature ? creature.ragdoll.headPart.transform : targetPoint);
    }
}