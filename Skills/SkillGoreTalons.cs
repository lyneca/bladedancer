using System;
using System.Collections;
using System.Collections.Generic;
using ThunderRoad;
using ThunderRoad.Skill.Spell;
using UnityEngine;

namespace Bladedancer.Skills; 

public class SkillGoreTalons : SkillSpellPunch {
    [SkillCategory("Gore Talons", Category.Base | Category.Body, 1), ModOptionOrder(0)]
    [ModOption("Talon Count", "Number of blades that are pulled into the talons.")]
    [ModOptionSlider, ModOptionIntValuesDefault(1, 5, 1, 3)]
    public static int talonCount;
    
    [SkillCategory("Gore Talons", Category.Base | Category.Body, 1), ModOptionOrder(0)]
    [ModOption("Talon Climbing",
        "When enabled, talons are fully jointed to your hands, allowing you to climb surfaces by stabbing into them.",
        defaultValueIndex = 1)]
    public static bool allowClimbing = true;

    [SkillCategory("Gore Talons", Category.Base | Category.Body, 1), ModOptionOrder(1)]
    [ModOptionFloatValuesDefault(0.05f, 0.3f, 0.05f, 0.15f)]
    [ModOptionSlider, ModOption("Talon Distance", "How far away talons lie from your hand.")]
    public static float talonDistance;

    [SkillCategory("Gore Talons", Category.Base | Category.Body, 1), ModOptionOrder(1)]
    [ModOptionFloatValuesDefault(0f, 0.1f, 0.05f, 0.05f)]
    [ModOptionSlider, ModOption("Talon Forward Distance", "How far the talons protrude from your hand.")]
    public static float talonForward;
    
    [SkillCategory("Gore Talons", Category.Base | Category.Body, 1), ModOptionOrder(1)]
    [ModOptionFloatValuesDefault(120, 360, 30, 180)]
    [ModOptionSlider, ModOption("Talon Spread Angle", "Angle which the talons are spread over.")]
    public static float talonAngle;

    [SkillCategory("Gore Talons", Category.Base | Category.Body, 1), ModOptionOrder(2)]
    [ModOptionSlider, ModOptionFloatValuesDefault(0, 30, 5, 0)]
    [ModOption("Talon Speed",
        "How fast the talons match your hand movement; higher is faster. Set to 0 for instant. Does nothing when climbing is enabled.")]
    public static float talonSpeed;

    [SkillCategory("Gore Talons", Category.Base | Category.Body, 1), ModOptionOrder(3)]
    [ModOptionSlider, ModOptionFloatValuesDefault(1, 10f, 0.5f, 4)]
    [ModOption("Talon Hand Strength Mult",
        "Talon hand strength multiplier, when climbing is enabled. Increases the player's strength, allowing you to pick up and climb stuff easier with the talons.")]
    public static float handStrengthMult;

    [SkillCategory("Gore Talons", Category.Base | Category.Body, 1), ModOptionOrder(3)]
    [ModOptionSlider, ModOptionFloatValuesDefault(0.5f, 4f, 0.05f, 1.65f)]
    [ModOption("Talon Mass Scale",
        "Talon strength, when climbing is enabled. Lower values mean stronger grabbing and climbing, but can result in buggy behaviour.")]
    public static float talonMassScale;

    [SkillCategory("Gore Talons", Category.Base | Category.Body, 1), ModOptionOrder(4)]
    [ModOptionFloatValuesDefault(0f, 100000f, 500f, 50000)]
    [ModOptionSlider, ModOption("Talon Spring (adv.)", "Talon joint spring")]
    public static float talonSpring;

    [SkillCategory("Gore Talons", Category.Base | Category.Body, 1), ModOptionOrder(4)]
    [ModOptionFloatValuesDefault(0f, 10000f, 50f, 300f)]
    [ModOptionSlider, ModOption("Talon Damper (adv.)", "Talon joint damper")]
    public static float talonDamper;

    [SkillCategory("Gore Talons", Category.Base | Category.Body, 1), ModOptionOrder(4)]
    [ModOptionFloatValuesDefault(0f, 100000f, 1000f, 22000f)]
    [ModOptionSlider, ModOption("Talon Max Force (adv.)", "Talon joint max force")]
    public static float talonMaxForce;

    protected static bool refreshing;
    
    public string startEffectId;
    protected EffectData startEffectData;

    public const string TalonList = "TalonList";
    public const string TalonActive = "TalonActive";

    public static Rigidbody[] gripObjects = new Rigidbody[2];
    public static Joint[] joints = new Joint[2];

    public override void OnCatalogRefresh() {
        base.OnCatalogRefresh();
        startEffectData = Catalog.GetData<EffectData>(startEffectId);
    }

    public override void OnSkillLoaded(SkillData skillData, Creature creature) {
        base.OnSkillLoaded(skillData, creature);
        refreshing = false;
    }

    public override void OnSkillUnloaded(SkillData skillData, Creature creature) {
        base.OnSkillUnloaded(skillData, creature);
        creature.handLeft.playerHand?.link.RemoveJointModifier(this);
        if (Quiver.TryGet(creature, out var quiver)) quiver.OnCountChangeEvent -= OnQuiverCountChange;
        RefreshClimb(creature.handLeft, true);
        RefreshClimb(creature.handRight, true);
    }

    public override void OnFist(PlayerHand hand, bool gripping) {
        base.OnFist(hand, gripping);
        hand.StartCoroutine(OnFistRoutine(hand, gripping));
    }

    public IEnumerator OnFistRoutine(PlayerHand hand, bool gripping) {
        yield return new WaitForEndOfFrame();
        if (hand == null || hand.ragdollHand == null) yield break;
        if (hand.ragdollHand.grabbedHandle != null) yield break;
        if (!Quiver.TryGet(hand.ragdollHand?.creature, out var quiver)) yield break;
        Player.currentCreature.SetVariable(TalonActive + hand.side, gripping);

        Refresh(hand.side);
        RefreshClimb(hand.ragdollHand, true);

        if (gripping) {
            hand.link.SetAllJointModifiers(this, handStrengthMult);
            quiver.OnCountChangeEvent -= OnQuiverCountChange;
            quiver.OnCountChangeEvent += OnQuiverCountChange;
        } else {
            hand.link.RemoveJointModifier(this);
            quiver.OnCountChangeEvent -= OnQuiverCountChange;
        }
    }

    private void OnQuiverCountChange(Quiver quiver) {
        Refresh();
    }

    protected void Refresh(Side side) {
        if (refreshing) return;
        var hand = Player.local.GetHand(side);

        if (!Player.currentCreature.TryGetVariable(TalonActive + side, out bool active)) {
            active = false;
            Player.currentCreature.SetVariable(TalonActive + side, false);
        }

        if (!Player.currentCreature.TryGetVariable(TalonList + side, out List<Blade> talons)) {
            talons = new List<Blade>();
            Player.currentCreature.SetVariable(TalonList + side, talons);
        }

        if (active && talons.Count == talonCount) return;
        refreshing = true;
        int startCount = talons.Count;

        for (var i = 0; i < talons.Count; i++) {
            if (!talons[i]) continue;
            talons[i].OnPenetrate -= OnPenetrate;
            talons[i].OnUnPenetrate -= OnPenetrate;
            if (!talons[i].ReturnToQuiver(Quiver.Main))
                talons[i].Release();
        }

        talons.Clear();

        if (!active) {
            refreshing = false;
            return;
        }
        
        if (Quiver.Main.Count < talonCount)
            Quiver.Main.FillFromHolsters();
        
        int count = Math.Min(Quiver.Main.Count, talonCount);
        var pointDir = hand.ragdollHand.transform.InverseTransformDirection(hand.ragdollHand.PointDir).normalized;
        var palmDir = hand.ragdollHand.transform.InverseTransformDirection(hand.ragdollHand.PalmDir).normalized;

        for (var i = 0; i < count; i++) {
            if (!Quiver.Main.TryGetClosestBlade(hand.ragdollHand.transform.position, out var blade))
                break;
            var position
                = Quaternion.AngleAxis((i - (count - 1) / 2f) * (talonAngle / count), pointDir)
                  * palmDir
                  * -talonDistance
                  + pointDir * talonForward;
            var moveTarget = new MoveTarget(MoveMode.Joint, talonSpeed)
                .Parent(hand.ragdollHand.transform)
                .At(position, Quaternion.LookRotation(pointDir, position));

            blade.MoveTo(moveTarget);
            blade.OnPenetrate -= OnPenetrate;
            blade.OnPenetrate += OnPenetrate;
            blade.OnUnPenetrate -= OnPenetrate;
            blade.OnUnPenetrate += OnPenetrate;
            talons.Add(blade);
        }

        if (startCount == 0 && talons.Count > 0) {
            startEffectData?.Spawn(hand.ragdollHand.caster.magicSource).Play();
            if (hand.ragdollHand.caster.spellInstance is SpellCastBlade spell) {
                spell.disableHandle.Add(this);
            }
        } else if (startCount > 0 && talons.Count == 0) {
            if (hand.ragdollHand.caster.spellInstance is SpellCastBlade spell) {
                spell.disableHandle.Remove(this);
            }
        }

        refreshing = false;
    }

    private void OnPenetrate(Blade blade, CollisionInstance hit, Damager damager) {
        if (blade.MoveTarget?.parent?.GetComponent<RagdollHand>() is RagdollHand hand && allowClimbing)
            RefreshClimb(hand);
    }

    public void RefreshClimb(RagdollHand hand, bool forceUnClimb = false) {
        if (forceUnClimb && hand.climb.gripNode == null) {
            hand.climb.isGripping = false;
            hand.climb.gripPhysicBody = null;
            return;
        }

        if (!Player.currentCreature.TryGetVariable(TalonList + hand.side, out List<Blade> talons)) return;
        bool wasPenetratingTerrain = hand.climb.isGripping;
        var isPenetratingTerrain = false;
        for (var i = 0; i < talons.Count; i++) {
            var talon = talons[i];
            var talonIsPenetratingTerrain = false;
            if (talon.item.isPenetrating) {
                for (var j = 0; j < talon.item.mainCollisionHandler.collisions.Length; j++) {
                    var collision = talon.item.mainCollisionHandler.collisions[j];
                    if (collision.damageStruct.penetration == DamageStruct.Penetration.None
                        || (collision.targetColliderGroup != null
                            && collision.targetColliderGroup?.collisionHandler is not
                                { physicBody.isKinematic: true })) continue;

                    talonIsPenetratingTerrain = true;
                    isPenetratingTerrain = true;
                    break;
                }
            }

            if (talonIsPenetratingTerrain) {
                if (talon.MoveTarget is not MoveTarget target) continue;
                talon.MoveTarget = target.JointTo(hand.physicBody.rigidBody, JointType.Config, talonMassScale,
                    talonSpring, talonDamper, talonMaxForce);
                talon.ForceRefreshMoveTarget();
            } else {
                if (talon.MoveTarget is not MoveTarget target) continue;
                talon.MoveTarget = target.JointTo(null);
                talon.ForceRefreshMoveTarget();
            }

        }

        // if (hand.climb.gripNode != null || isPenetratingTerrain == wasPenetratingTerrain) return;
        // if (isPenetratingTerrain) {
        //     hand.climb.isGripping = true;
        //     var obj = new GameObject("TEMP").AddComponent<Rigidbody>();
        //     obj.isKinematic = true;
        //     hand.climb.gripPhysicBody = new PhysicBody(obj);
        //     hand.ragdoll.creature.currentLocomotion.Jump(true);
        // } else {
        //     if (hand.climb.gripPhysicBody && hand.climb.gripPhysicBody.gameObject.name == "TEMP")
        //         Object.Destroy(hand.climb.gripPhysicBody.gameObject);
        //     hand.climb.gripPhysicBody = null;
        //     hand.climb.isGripping = false;
        //     for (var i = 0; i < talons.Count; i++) {
        //     }
        // }
    }

    public void Refresh() {
        for (var i = 0; i < 2; i++) {
            Refresh((Side)i);
        }
    }
}
