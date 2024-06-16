using ThunderRoad;
using ThunderRoad.Skill;
using UnityEngine;

namespace Bladedancer.Skills;

public class SkillBladelock : SpellSkillData {
    public override void OnSpellLoad(SpellData spell, SpellCaster caster = null) {
        base.OnSpellLoad(spell, caster);
        if (spell is not SpellCastSlingblade blade) return;
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
        if (spell is not SpellCastSlingblade blade) return;
        blade.OnSpellUpdateLoopEvent -= OnUpdate;
        blade.OnBladeThrowEvent -= OnBladeThrow;
    }

    public Creature target;
    public Blade targetBlade;
    public float checkDelay = 0.2f;
    protected float lastCheck;

    public void OnUpdate(SpellCastCharge spell) {
        if (spell is not SpellCastSlingblade slingblade
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

        if (target == null || target.isKilled) StopTarget(spell.spellCaster.mana.creature);

        if (Time.time - lastCheck < checkDelay) return;

        var newTarget = ThunderEntity.AimAssist(new Ray(otherHand.transform.position, otherHand.PointDir), 20, 10,
            Filter.EnemyOf(otherHand.creature)) as Creature;

        if (target != null && newTarget == null || newTarget == target) return;
        StopTarget(spell.spellCaster.mana.creature);
        target = newTarget;
        StartTarget(spell.spellCaster.mana.creature, target, slingblade);
        if (Quiver.TryGet(spell.spellCaster.mana.creature, out var quiver) && quiver.Count > 0)
            otherHand.HapticTick();
    }

    public void StartTarget(Creature player, Creature creature, SpellCastSlingblade slingblade) {
        if (creature == null
            || !slingblade.quiver.TryGetClosestBlade(creature.ragdoll.targetPart.transform.position,
                out var blade)) {
            target = null;
            return;
        }

        targetBlade = blade;
        blade.Release();
        blade.AllowDespawn(false);
        blade.MoveTo(new MoveTarget(MoveMode.PID, 10)
            .Parent(creature.ragdoll.headPart.transform, false)
            .At(Vector3.up * 0.5f)
            .LookAt(creature.ragdoll.headPart.transform));
        blade.SetIntangible(true);
    }

    public void StopTarget(Creature player) {
        target = null;
        if (targetBlade == null) targetBlade = null;
        targetBlade?.SetIntangible(false);
        targetBlade?.ReturnToQuiver(player);
        targetBlade = null;
    }

    public void OnBladeThrow(SpellCastSlingblade spell, Vector3 velocity, Blade blade) {
        if (target != null) blade.HomeTo(target, target.ragdoll.headPart.transform);
    }
}