using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ThunderRoad;
using ThunderRoad.Skill;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Bladedancer; 

public class Disoriented : Status {
    public float orgArmSpringMult;
    public float orgArmDamperMult;
    public Creature orgTarget;
    public new StatusDataDisoriented data;
    protected float lastTargetSwitch;

    protected bool cachedDodging;
    private List<BrainModuleHitReaction.PushBehaviour> orgPushHitBehaviours;
    private List<BrainModuleHitReaction.PushBehaviour> orgPushMagicBehaviours;

    public override void Spawn(StatusData data, ThunderEntity entity) {
        base.Spawn(data, entity);
        this.data = data as StatusDataDisoriented;
    }

    public override void FirstApply() {
        base.FirstApply();
        if (entity is not Creature creature) return;

        SkillDiscombobulate.StunCreature(creature, 1f, true, null, false);
    }

    public override void Apply() {
        base.Apply();
        if (entity is not Creature {data.type: CreatureType.Human} creature) return;
        orgTarget = creature.brain.currentTarget;
        var melee = creature.brain.instance.GetModule<BrainModuleMelee>();
        var hitReaction = creature.brain.instance.GetModule<BrainModuleHitReaction>();
        creature.handLeft.OnGrabEvent += OnGrabEvent;
        creature.handLeft.OnUnGrabEvent += OnUnGrab;
        creature.handRight.OnGrabEvent += OnGrabEvent;
        creature.handRight.OnUnGrabEvent += OnUnGrab;
        
        creature.brain.OnAttackEvent += OnAttack;
        
        Extension.BackupSet(ref melee.armSpringMultiplier, ref orgArmSpringMult, melee.armSpringMultiplier * StatusDataDisoriented.strengthMult);
        Extension.BackupSet(ref melee.armDamperMultiplier, ref orgArmDamperMult, melee.armDamperMultiplier * StatusDataDisoriented.strengthMult);

        orgPushHitBehaviours = new List<BrainModuleHitReaction.PushBehaviour>(hitReaction.pushHitBehaviors);
        ShiftList(hitReaction.pushHitBehaviors);
        orgPushMagicBehaviours = new List<BrainModuleHitReaction.PushBehaviour>(hitReaction.pushMagicBehaviors);
        ShiftList(hitReaction.pushMagicBehaviors);
    }

    public void ShiftList<T>(List<T> list) {
        if (list.Count < 2) return;
        T next = list[list.Count - 1];
        for (var i = list.Count - 1; i >= 0; i--) {
            (list[i], next) = (next, list[i]);
        }
    }

    private void OnAttack(Brain.AttackType type, bool strong, Creature target) {
        if (entity is not Creature creature || target == null) return;
        switch (type) {
            case Brain.AttackType.Cast:
                var cast = creature.brain.instance.GetModule<BrainModuleCast>();
                var transform = new GameObject().transform;
                var toTarget = target.ragdoll.targetPart.transform.position
                               - creature.ragdoll.targetPart.transform.position;
                transform.position = target.ragdoll.targetPart.transform.position
                                     + Quaternion.AngleAxis(Random.value * 360, toTarget)
                                     * (Vector3.Cross(Vector3.up, toTarget).normalized * Random.value * StatusDataDisoriented.inaccuracy);
                cast.SetTarget(transform);
                break;
        }
    }

    private void OnGrabEvent(
        Side side,
        Handle handle,
        float axis,
        HandlePose orientation,
        EventTime time) {
        if (time == EventTime.OnEnd) return;
        if (handle.item?.data.moduleAI is { rangedWeaponData: ItemModuleAI.RangedWeaponData ranged })
            ranged.spread = new Vector2(StatusDataDisoriented.inaccuracy, ranged.spread.y);
    }

    private void OnUnGrab(Side side, Handle handle, bool throwing, EventTime time) {
        if (time == EventTime.OnEnd) return;
        if (handle.item?.data.moduleAI is { rangedWeaponData: ItemModuleAI.RangedWeaponData ranged }) {
            var orgData = Catalog.GetData<ItemData>(handle.item.data.id).moduleAI.rangedWeaponData;
            ranged.spread = orgData.spread;
        }
    }

    public override void Update() {
        base.Update();
        if (entity is not Creature creature) return;
        var defense = creature.brain.instance.GetModule<BrainModuleDefense>();
        if (defense.TryGetPrivate("isDodging", out bool isDodging)) {
            switch (isDodging) {
                case true when !cachedDodging:
                    OnDodgeStart(creature);
                    break;
                case false:
                    break;
            }
            
            cachedDodging = isDodging;
        }

        if (Time.time - lastTargetSwitch > StatusDataDisoriented.targetSwitchDelay) {
            lastTargetSwitch = Time.time;
            if (StatusDataDisoriented.targetSwitchChance > Random.value) return;
            if (Creature.allActive.RandomFilteredSelectInPlace(eachTarget
                        => eachTarget is { isKilled: false, isPlayer: false } && eachTarget != creature,
                    out var target)) {
                creature.brain.currentTarget = target;
            }
        }
    }

    public void OnDodgeStart(Creature creature) {
        if (Random.value < StatusDataDisoriented.tripChance) {
            creature.StartCoroutine(Trip(creature));
        }
    }

    public IEnumerator Trip(Creature creature) {
        yield return new WaitForSeconds(0.3f);
        creature.MaxPush(Creature.PushType.Magic,
            creature.ragdoll.targetPart.transform.position
            - Player.currentCreature.ragdoll.targetPart.transform.position);
    }

    public override void Remove() {
        base.Remove();
        if (entity is not Creature { data.type: CreatureType.Human } creature) return;

        creature.brain.currentTarget = orgTarget;
        creature.handLeft.OnGrabEvent -= OnGrabEvent;
        creature.handLeft.OnUnGrabEvent -= OnUnGrab;
        creature.handRight.OnGrabEvent -= OnGrabEvent;
        creature.handRight.OnUnGrabEvent -= OnUnGrab;
        creature.brain.OnAttackEvent -= OnAttack;

        if (creature.brain.instance.GetModule<BrainModuleHitReaction>() is BrainModuleHitReaction hitReaction) {
            hitReaction.pushHitBehaviors = orgPushHitBehaviours;
            hitReaction.pushMagicBehaviors = orgPushMagicBehaviours;
        }

        if (creature.brain.instance.GetModule<BrainModuleMelee>() is BrainModuleMelee melee) {
            melee.armSpringMultiplier = orgArmSpringMult;
            melee.armDamperMultiplier = orgArmDamperMult;
        }
    }
}
