using System.Collections.Generic;
using ThunderRoad;
using UnityEngine;

namespace Bladedancer.Skills;

public class SkillArchersGavotte : SkillData {
    private const string ListName = "GavotteBlades";
    public override void OnSkillLoaded(SkillData skillData, Creature creature) {
        base.OnSkillLoaded(skillData, creature);
        EventManager.OnBowDrawEvent -= OnBowDraw;
        EventManager.OnBowDrawEvent += OnBowDraw;
        EventManager.OnBowReleaseEvent -= OnBowRelease;
        EventManager.OnBowReleaseEvent += OnBowRelease;
        EventManager.OnBowFireEvent -= OnBowFire;
        EventManager.OnBowFireEvent += OnBowFire;
    }

    public override void OnSkillUnloaded(SkillData skillData, Creature creature) {
        base.OnSkillUnloaded(skillData, creature);
        EventManager.OnBowDrawEvent -= OnBowDraw;
        EventManager.OnBowReleaseEvent -= OnBowRelease;
        EventManager.OnBowFireEvent -= OnBowFire;
        if (Quiver.TryGet(creature, out var quiver))
            quiver.OnBladeAddEvent += OnBladeAdded;
        
        ReturnBlades(creature);

        creature.ClearVariable<List<Blade>>(ListName);
    }

    private void OnBladeAdded(Quiver quiver, Blade blade) {
        
    }

    public Vector3 Position(RagdollHand hand, int i) => hand.transform.position + hand.PalmDir * 0.15f * (i * 2 - 1);

    public void OnBowDraw(Creature creature, BowString bowString) {
        Refresh(creature, bowString);
    }

    public void ReturnBlades(Creature creature) {
        if (!creature.TryGetVariable(ListName, out List<Blade> blades) || blades == null || blades.Count == 0) return;
        for (var i = 0; i < blades.Count; i++) {
            if (blades[i] == null) continue;
            blades[i].ReturnToQuiver(creature);
        }

        creature.ClearVariable<List<Blade>>(ListName);
    }

    public void Refresh(Creature creature, BowString bowString) {
        if (bowString.stringHandle.handlers.Count == 0) {
            return;
        }

        if (!Quiver.TryGet(creature, out var quiver)) return;

        if (!creature.TryGetVariable(ListName, out List<Blade> list)) {
            list = new List<Blade>();
            creature.SetVariable(ListName, list);
        }

        // Clear invalid entries
        for (int i = list.Count - 1; i >= 0; i--) {
            if (list[i] == null || !list[i].IsValid) list.RemoveAt(i);
        }

        var stringHand = bowString.stringHandle.handlers[0];
        var bowHand = stringHand.otherHand;

        if (list.Count >= 2) return;
        int required = 2 - list.Count;
        for (var i = 0; i < required; i++) {
            if (!quiver.TryGetClosestBlade(Position(bowHand, i), out var blade)) break;
            blade.MoveTo(new MoveTarget(MoveMode.PID, 6)
                .Parent(bowString.currentRest)
                .Scale(ScaleMode.Scaled)
                .AtWorld(Position(bowHand, i),
                    bowString.loadedArrow?.transform.rotation
                    ?? Quaternion.LookRotation(bowHand.PointDir, bowHand.PalmDir)));
            
            blade.IgnoreItem(bowString.item);
            blade.item.OnFlyEndEvent += _ => blade.IgnoreItem(bowString.item, false);
            list.Add(blade);
        }
    }

    public void OnBowFire(RagdollHand hand, BowString bowString, Item arrow) {
        if (!hand.creature.TryGetVariable(ListName, out List<Blade> list)) return;
        
        for (var i = 0; i < list.Count; i++) {
            var blade = list[i];
            blade.IgnoreItem(arrow);
            blade.item.OnFlyEndEvent += _ => blade.IgnoreItem(arrow, false);
        }

        arrow.mainCollisionHandler.OnCollisionStartEvent += OnCollisionStart;
        arrow.mainCollisionHandler.item.OnDespawnEvent += OnDespawn;
        FireBlades();
        hand.creature.ClearVariable<List<Blade>>(ListName);
        return;

        void FireBlades() {
            for (var i = 0; i < list.Count; i++) {
                list[i].transform.position = Position(hand.otherHand, i);
                list[i].Release();
                list[i].AddForce(arrow.Velocity / 3f + Vector3.up * 3 + hand.PalmDir * -2f * (i * 2 - 1), ForceMode.VelocityChange, false,
                    true);
            }
        }

        void OnDespawn(EventTime time) {
            arrow.mainCollisionHandler.item.OnDespawnEvent -= OnDespawn;
            arrow.mainCollisionHandler.OnCollisionStartEvent -= OnCollisionStart;
        }

        void OnCollisionStart(CollisionInstance hit) {
            if (hit == null) return;
            if (hit.targetColliderGroup) {
                var target = new GameObject().transform;
                target.transform.SetParent(hit.targetColliderGroup.transform);
                target.transform.position = hit.contactPoint;
                arrow.mainCollisionHandler.OnCollisionStartEvent -= OnCollisionStart;
                if (hit.targetColliderGroup?.collisionHandler?.ragdollPart is RagdollPart part) {
                    for (var i = 0; i < list.Count; i++) {
                        list[i].HomeTo(part.ragdoll.creature, target);
                    }
                } else if (hit.targetColliderGroup?.collisionHandler?.item is Item item) {
                    for (var i = 0; i < list.Count; i++) {
                        list[i].HomeTo(item, target);
                    }
                }
            }
        }
    }

    public void OnBowRelease(Creature creature, BowString bowString) {
        if (bowString.stringHandle.handlers.Count > 0)
            ReturnBlades(creature);
        else 
            creature.RunAfter(() => ReturnBlades(creature), 0.3f);
    }
}
