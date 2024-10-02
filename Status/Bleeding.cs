using System;
using ThunderRoad;
using ThunderRoad.Skill;
using UnityEngine;

namespace Bladedancer; 

public class Bleeding : Status {
    public const string VarBleeding = "Bleeding";
    
    [SkillCategory("Bleeding", Category.Base, 0, 2)]
    [ModOptionSlider, ModOptionFloatValues(0, 100f, 1)]
    [ModOption("Bleeding damage per tick")]
    public static float damage = 5;
    
    [SkillCategory("Bleeding", Category.Base, 0, 2)]
    [ModOptionSlider, ModOptionFloatValues(0.1f, 5f, 0.1f)]
    [ModOption("Delay between bleeding damage ticks")]
    public static float tickDelay = 2;
    
    [SkillCategory("Blood Loss", Category.Base, 1, 1)]
    [ModOption("Enable Head Blood Loss")]
    public static bool enableHead = true;
    
    [SkillCategory("Blood Loss", Category.Base, 0, 2)]
    [ModOption("Enable Arm Blood Loss")]
    public static bool enableArms = true;
    
    [SkillCategory("Blood Loss", Category.Base, 0, 2)]
    [ModOption("Enable Leg Blood Loss")]
    public static bool enableLegs = true;
    
    [SkillCategory("Blood Loss", Category.Base, 0, 2)]
    [ModOption("Enable Ragdoll When Both Legs are Bleeding")]
    public static bool destabilize = true;

    public static bool bloodLoss;
    
    public new StatusDataBleeding data;
    public float lastTick;
    
    public override void Spawn(StatusData data, ThunderEntity entity)
    {
      base.Spawn(data, entity);
      this.data = data as StatusDataBleeding;
    }

    public override bool AddHandler(
        object handler,
        float duration = Mathf.Infinity,
        object parameter = null,
        bool playEffect = true) {
        if (parameter is not RagdollPart part || entity is not Creature creature || creature.isPlayer)
            return base.AddHandler(handler, duration, parameter, playEffect);

        creature.TryGetVariable(VarBleeding, out RagdollPart.Type injuredParts);
        injuredParts |= part.type;
        creature.SetVariable(VarBleeding, injuredParts);
        return base.AddHandler(handler, duration, parameter, playEffect);
    }

    public override void Update() {
        base.Update();
        if (entity is not Creature { isKilled: false } creature) return;
        if (Time.time - lastTick > tickDelay) {
            lastTick = Time.time;
            float damageToDeal = damage;
            if (creature.TryGetVariable(VarBleeding, out RagdollPart.Type injuredParts)
                && injuredParts.HasFlag(RagdollPart.Type.Neck))
                damageToDeal *= 2;
            creature.Damage(damageToDeal, DamageType.Slash);
            creature.ForceStagger(-creature.ragdoll.headPart.transform.forward,
                BrainModuleHitReaction.PushBehaviour.Effect.StaggerLight);
        }
    }

    public override void OnValueChange() {
        base.OnValueChange();
        
        if (entity is not Creature creature
            || !bloodLoss
            || creature.isPlayer
            || !creature.TryGetVariable(VarBleeding, out RagdollPart.Type injuredParts)) return;
        
        foreach (RagdollPart.Type part in Enum.GetValues(typeof(RagdollPart.Type))) {
            if (!injuredParts.HasFlag(part)) continue;

            switch (part) {
                case RagdollPart.Type.Head when enableHead:
                    Discombobulate(creature);
                    break;
                case RagdollPart.Type.LeftArm when creature.handLeft is not null && enableArms:
                    MakeFloppy(creature.handLeft.upperArmPart);
                    MakeFloppy(creature.handLeft.lowerArmPart);
                    MakeFloppy(creature.handLeft);
                    creature.handLeft.TryRelease();
                    break;
                case RagdollPart.Type.RightArm when creature.handRight is not null && enableArms:
                    MakeFloppy(creature.handRight.upperArmPart);
                    MakeFloppy(creature.handRight.lowerArmPart);
                    MakeFloppy(creature.handRight);
                    creature.handRight.TryRelease();
                    break;
                case RagdollPart.Type.LeftHand when creature.handLeft is not null && enableArms:
                    creature.handLeft.TryRelease();
                    break;
                case RagdollPart.Type.RightHand when creature.handRight is not null && enableArms:
                    creature.handRight.TryRelease();
                    break;
                case RagdollPart.Type.LeftLeg when enableLegs:
                    MakeFloppy(creature.footLeft.upperLegBone);
                    MakeFloppy(creature.footLeft.lowerLegBone);
                    MakeFloppy(creature.footLeft);
                    break;
                case RagdollPart.Type.RightLeg when enableLegs:
                    MakeFloppy(creature.footRight.upperLegBone);
                    MakeFloppy(creature.footRight.lowerLegBone);
                    MakeFloppy(creature.footRight);
                    break;
            }
        }

        if (!injuredParts.HasFlag(RagdollPart.Type.LeftLeg | RagdollPart.Type.RightLeg)
            || creature.ragdoll.state != Ragdoll.State.Standing
            || !enableLegs
            || !destabilize) return;

        creature.ragdoll.SetState(Ragdoll.State.Destabilized);
        creature.brain.AddNoStandUpModifier(this);
    }

    public void MakeFloppy(Transform bone) => MakeFloppy((entity as Creature)?.ragdoll.GetPart(bone));

    public void MakeFloppy(RagdollPart part) {
        part.collisionHandler.physicBody.useGravity = true;
        part.collisionHandler.SetPhysicModifier(this, 1);
        part.bone.SetPinPositionForce(0, 0, 0);
        part.bone.SetPinRotationForce(0, 0, 0);
    }

    public void Discombobulate(Creature creature) {
        if (!creature.isKilled
            && (!creature.brain.instance.isActive || creature.ragdoll.state != Ragdoll.State.Standing))
            creature.ragdoll.SetState(Ragdoll.State.Inert, true);
        bool toggleHitReaction = creature.state == Creature.State.Destabilized;
        SkillDiscombobulate.BrainToggle(creature, false, toggleHitReaction);
        creature.autoEyeClipsActive = false;
        creature.PlayEyeClip("CloseEyes");
    }
}
