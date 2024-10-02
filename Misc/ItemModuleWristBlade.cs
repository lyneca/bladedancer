using System;
using Bladedancer.Skills;
using ThunderRoad;
using UnityEngine;

namespace Bladedancer; 

public class ItemModuleWristBlade : ItemModule {
    public override void OnItemLoaded(Item item) {
        base.OnItemLoaded(item);
        item.gameObject.AddComponent<WristBladeBehaviour>();
    }
}

public class WristBladeBehaviour : ThunderBehaviour {
    public Item item;
    
    public Blade heldBlade;

    public Vector3 TargetPoint => item.mainHandleLeft.spellOrbTarget.TransformPoint(Vector3.forward * 0.4f);

    private void Awake() {
        item = GetComponent<Item>();
        item.OnHeldActionEvent += OnHeldAction;
    }

    private void OnHeldAction(RagdollHand hand, Handle handle, Interactable.Action action) {
        switch (action) {
            case Interactable.Action.UseStart when hand.creature.HasSkill(ItemModuleTwinBlade.skillData):
                GrabBlade(hand);
                break;
            case Interactable.Action.UseStop:
                ReleaseBlade(hand);
                break;
        }
    }

    public void GrabBlade(RagdollHand hand) {
        if (heldBlade != null) return;
        if (Quiver.Get(hand.creature).TryGetClosestBlade(TargetPoint, out Blade blade)) {
            heldBlade = blade;
            if (item.imbues.Count > 0 && item.imbues[0] is { spellCastBase: not null and var spell, energy: > 0 }) {
                blade.MaxImbue(spell, Player.currentCreature);
            }
            
            blade.AllowDespawn(false);
            heldBlade.IgnoreItem(item);

            blade.MoveTo(new MoveTarget(MoveMode.PID, 10)
                .Parent(item.transform)
                .AtWorld(TargetPoint, Quaternion.LookRotation(item.transform.right, item.transform.up))
            );
        }
    }

    public bool ReleaseBlade(RagdollHand hand) {
        if (heldBlade == null) return false;

        var velocity = item.physicBody.GetPointVelocity(TargetPoint);
        velocity = Vector3.Slerp(Player.local.head.transform.forward.normalized, velocity.normalized, 0.5f)
                   * velocity.magnitude;

        if (velocity.sqrMagnitude
            < SkillTwinBladeMaestro.shootVelocityThreshold * SkillTwinBladeMaestro.shootVelocityThreshold) {
            heldBlade?.ReturnToQuiver(Quiver.Main);
            heldBlade = null;
            return false;
        }

        heldBlade.Release(true, 0.5f);
        heldBlade.AddForce(velocity * SkillTwinBladeMaestro.throwMult * 1.5f, ForceMode.VelocityChange);
        heldBlade.IgnoreItem(item, false);

        if (Creature.AimAssist(TargetPoint, velocity, 30, 30, out var target, Filter.EnemyOf(Player.currentCreature),
                CreatureType.Golem | CreatureType.Human) is ThunderEntity entity) {
            heldBlade.HomeTo(entity, target);
        }
        
        heldBlade = null;

        return true;
    }
}