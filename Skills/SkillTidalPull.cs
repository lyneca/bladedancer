using System;
using System.Collections.Generic;
using ThunderRoad;
using ThunderRoad.Skill;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bladedancer.Skills; 

public class SkillTidalPull : SpellSkillData {
    [SkillCategory("Tidal Pull", Category.Base | Category.Gravity, 2)]
    [ModOptionFloatValues(0, 30, 1)]
    [ModOptionSlider, ModOption("Tidal Pull Tether Duration", "Tether joint strength")]
    public static float duration = 10;
    
    [SkillCategory("Tidal Pull", Category.Base | Category.Gravity, 2)]
    [ModOptionFloatValues(0, 10000, 100)]
    [ModOptionSlider, ModOption("Tidal Pull Joint Spring", "Tether joint strength")]
    public static float spring = 2000;
    
    [SkillCategory("Tidal Pull", Category.Base | Category.Gravity, 2)]
    [ModOptionFloatValues(0, 1000, 10)]
    [ModOptionSlider, ModOption("Tidal Pull Joint Damper", "Tether joint damping force")]
    public static float damper = 100;
    
    public const string LastBodyHit = "TidalPullLastBody";
    public const string LastPointHit = "TidalPullLastPoint";
    public const string HasThrown = "TidalPullHasThrown";
    public string effectId = "GravityTether";
    
    public EffectData effectData;

    public override void OnCatalogRefresh() {
        base.OnCatalogRefresh();
        effectData = Catalog.GetData<EffectData>(effectId);
    }

    public override void OnSkillLoaded(SkillData skillData, Creature creature) {
        base.OnSkillLoaded(skillData, creature);
        if (Quiver.TryGet(creature, out var quiver)) {
            quiver.OnBladeThrow -= OnBladeThrow;
            quiver.OnBladeThrow += OnBladeThrow;
        }
    }

    public override void OnSkillUnloaded(SkillData skillData, Creature creature) {
        base.OnSkillUnloaded(skillData, creature);
        if (Quiver.TryGet(creature, out var quiver))
            quiver.OnBladeThrow -= OnBladeThrow;
    }
    
    public void OnBladeThrow(Quiver quiver, Blade blade) {
        if (!blade.ImbuedWith("Gravity")) return;
        blade.item.mainCollisionHandler.OnCollisionStartEvent -= OnCollisionStart;
        blade.item.mainCollisionHandler.OnCollisionStartEvent += OnCollisionStart;
        blade.item.ClearVariable<Rigidbody>(LastBodyHit);
        blade.item.ClearVariable<Vector3>(LastPointHit);
        blade.item.SetVariable(HasThrown, true);
    }

    private void OnCollisionStart(CollisionInstance hit) {
        if (hit.sourceColliderGroup?.collisionHandler?.item is not Item item) return;
        item.mainCollisionHandler.OnCollisionStartEvent -= OnCollisionStart;
        if (!item.TryGetVariable(HasThrown, out bool thrown) || !thrown) return;
        item.SetVariable(HasThrown, false);
        if (item.GetComponent<Blade>() is not Blade blade) return;
        if (item.isPenetrating) {
            return;
        }

        blade.item.SetVariable(LastPointHit, hit.contactPoint);
        if (hit.targetColliderGroup?.collisionHandler?.physicBody.rigidBody is Rigidbody body) {
            blade.item.SetVariable(LastBodyHit, body);
        }

        item.physicBody.velocity = Vector3.Reflect(hit.impactVelocity, hit.contactNormal);
        if (Blade.AimAssist(new Ray(blade.transform.position, item.physicBody.velocity), 20, 40, out var point,
                Filter.EnemyOf(item.lastHandler?.creature),
                CreatureType.Animal | CreatureType.Golem | CreatureType.Human)) {
            item.physicBody.velocity = (point.position - blade.transform.position).normalized
                                       * item.physicBody.velocity.magnitude;
        }
        blade.item.Throw();
            

        item.mainCollisionHandler.OnCollisionStartEvent -= OnSecondCollisionStart;
        item.mainCollisionHandler.OnCollisionStartEvent += OnSecondCollisionStart;
    }

    private void OnSecondCollisionStart(CollisionInstance hit) {
        if (hit.sourceColliderGroup.collisionHandler.item is not Item item
            || item.GetComponent<Blade>() is not Blade blade) {
            hit.sourceColliderGroup.collisionHandler.OnCollisionStartEvent -= OnSecondCollisionStart;
            return;
        }

        blade.item.TryGetVariable(LastBodyHit, out Rigidbody lastBody);
        var thisBody = hit.targetColliderGroup?.collisionHandler?.physicBody.rigidBody;

        var lastEntity = lastBody ? lastBody.GetComponentInParent<ThunderEntity>() : null;
        var thisEntity = thisBody ? thisBody.GetComponentInParent<ThunderEntity>() : null;

        if (lastEntity && lastEntity == thisEntity) {
            return;
        }

        item.mainCollisionHandler.OnCollisionStartEvent -= OnSecondCollisionStart;

        var holdingCreatures = new List<Creature>();
        if (lastEntity is Creature lastCreature) holdingCreatures.Add(lastCreature);
        if (thisEntity is Creature thisCreature) holdingCreatures.Add(thisCreature);
        if (lastEntity is Item { mainHandler.creature: Creature lastHoldingCreature })
            holdingCreatures.Add(lastHoldingCreature);
        if (thisEntity is Item { mainHandler.creature: Creature thisHoldingCreature })
            holdingCreatures.Add(thisHoldingCreature);

        if (lastBody && thisBody) {
            Tether(lastBody, thisBody, Start, End);
        } else if (lastBody != null) {
            Tether(lastBody, hit.contactPoint, Start, End);
        } else if (thisBody != null && blade.item.TryGetVariable(LastPointHit, out Vector3 point)) {
            Tether(thisBody, point, Start, End);
        }

        return;

        void CreatureStart(Creature creature, Joint joint) {
            creature.brain.AddNoStandUpModifier(this);
            if (creature.ragdoll.state == Ragdoll.State.Standing)
                creature.ragdoll.SetState(Ragdoll.State.Destabilized);
            creature.OnDespawnEvent += time => {
                if (time == EventTime.OnStart) Object.Destroy(joint);
            };
        }

        void Start(Joint joint) {
            for (var i = 0; i < holdingCreatures.Count; i++) {
                CreatureStart(holdingCreatures[i], joint);
            }
        }

        void End(Joint joint) {
            for (var i = 0; i < holdingCreatures.Count; i++) {
                if (holdingCreatures[i] == null) return;
                holdingCreatures[i].brain.RemoveNoStandUpModifier(this);
            }
        }
    }

    public void Tether(Rigidbody source, Rigidbody target, Action<Joint> onStart = null, Action<Joint> onEnd = null) {
        var joint = CreateJoint(source, target);
        var effect = effectData.Spawn(source.transform);
        var sourcePoint = new GameObject().transform;
        var targetPoint = new GameObject().transform;
        sourcePoint.transform.position = source.transform.position;
        sourcePoint.transform.SetParent(source.transform);
        targetPoint.transform.position = target.transform.position;
        targetPoint.transform.SetParent(target.transform);
        effect.SetSourceAndTarget(sourcePoint, targetPoint);
        effect.Play();
        onStart?.Invoke(joint);
        Player.local.RunAfter(() => {
            onEnd?.Invoke(joint);
            if (joint != null) Object.Destroy(joint);
            if (effect != null) {
                effect.onEffectFinished += _ => {
                    Object.Destroy(sourcePoint);
                    Object.Destroy(targetPoint);
                };
                effect.End();
            }
        }, duration);
    }

    public void Tether(Rigidbody body, Vector3 target, Action<Joint> onStart = null, Action<Joint> onEnd = null) {
        var joint = CreateJoint(body, null, target);
        var effect = effectData.Spawn(body.transform);
        var sourcePoint = new GameObject().transform;
        var targetPoint = new GameObject().transform;
        sourcePoint.transform.position = body.transform.position;
        sourcePoint.transform.SetParent(body.transform);
        targetPoint.transform.position = target;
        effect.SetSourceAndTarget(sourcePoint, targetPoint);
        effect.Play();
        onStart?.Invoke(joint);
        Player.local.RunAfter(() => {
            onEnd?.Invoke(joint);
            if (joint != null) Object.Destroy(joint);
            if (effect != null) {
                effect.onEffectFinished += _ => {
                    Object.Destroy(sourcePoint);
                    Object.Destroy(targetPoint);
                };
                effect.End();
            }
        }, duration);
    }

    public Joint CreateJoint(Rigidbody body, Rigidbody other, Vector3 anchor = default) {
        var joint = body.gameObject.AddComponent<SpringJoint>();
        joint.autoConfigureConnectedAnchor = false;
        if (other != null) {
            joint.connectedBody = other;
            joint.connectedAnchor = other.centerOfMass;
        } else {
            joint.connectedBody = null;
            joint.connectedAnchor = anchor;
        }
        joint.anchor = body.centerOfMass;
        joint.spring = spring;
        joint.damper = damper;
        return joint;
    }
}
