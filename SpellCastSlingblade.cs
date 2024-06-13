using System;
using SequenceTracker;
using ThunderRoad;
using ThunderRoad.Skill.Spell;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bladedancer;

public class SpellCastSlingblade : SpellCastCharge {
    public string itemId = "ThrowablesDagger";

    public string handleId = "ObjectHandleLight";
    public HandleData handleData;

    [SkillCategory("General")]
    [ModOptionSlider, ModOptionFloatValues(5, 20, 5)]
    [ModOption("Joint Mass Scale", "Blade joint-mode mass scale. Makes joint-based movement stronger.", defaultValueIndex = 1)]
    public static float jointMassScale = 10f;

    [SkillCategory("General")]
    [ModOptionSlider, ModOptionFloatValues(250, 1000, 250)]
    [ModOption("Joint Spring", "Blade joint-mode spring strength", defaultValueIndex = 1)]
    public static float jointSpring = 500f;

    [SkillCategory("General")]
    [ModOptionSlider, ModOptionFloatValues(5, 50, 5)]
    [ModOption("Joint Damper", "Joint spring damper", defaultValueIndex = 1)]
    public static float jointDamper = 10f;

    public static float handJointLerp = 20f;

    [SkillCategory("General")]
    [ModOptionSlider, ModOptionFloatValues(5000, 100000, 5000)]
    [ModOption("Joint Max Force", "Joint maximum force", defaultValueIndex = 1)]
    public static float jointMaxForce = 10000f;

    public static ModOptionInt[] forceModeValues = {
        new("Force", (int)ForceMode.Force),
        new("Impulse", (int)ForceMode.Impulse),
        new("Acceleration", (int)ForceMode.Acceleration),
        new("Velocity Change", (int)ForceMode.VelocityChange)
    };

    // [SkillCategory("General")]
    // [ModOptionValues(nameof(forceModeValues), typeof(int))]
    // [ModOption("PID Force Mode", "PID force application mode", defaultValueIndex = 3)]
    // public static int pidForceModeInt = (int)ForceMode.Acceleration;

    public static ForceMode pidForceMode = ForceMode.Acceleration;

    [SkillCategory("General")]
    [ModOptionSlider, ModOptionFloatValues(50, 300, 50)]
    [ModOption("PID Max Force", "PID Max Force", defaultValueIndex = 1)]
    public static float pidMaxForce = 100;

    [SkillCategory("General")]
    [ModOptionSlider, ModOptionFloatValues(0, 3, 1)]
    [ModOption("Despawn Time", "Time before a blade despawns or is collected", defaultValueIndex = 3)]
    public static float collectTime = 3f;

    [SkillCategory("General")]
    [ModOptionSlider, ModOptionFloatValues(8, 20, 2)]
    [ModOption("Throw Force", "Force added when thrown using Slingblade", defaultValueIndex = 2)]
    public static float throwForce = 12f;

    [SkillCategory("General")]
    [ModOptionSlider, ModOptionFloatValues(2, 10, 2)]
    [ModOption("AI Throw Force", "Forced throw hand velocity for NPCs using Slingblade", defaultValueIndex = 2)]
    public static float aiThrowForce = 7;

    [SkillCategory("General")]
    [ModOptionSlider, ModOptionFloatValues(1, 5, 0.5f)]
    [ModOption("Gravity Force Compensation", "Force multiplier when throwing a Gravity-imbued blade using Slingblade",
        defaultValueIndex = 5)]
    public static float gravityForceCompensation = 3.5f;

    public static bool freeCharge;
    public static bool aimAssist = false;

    protected AnimationCurve rotationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    protected bool readyHaptic = false;

    public Blade activeBlade;

    public bool quiverEnabled = false;
    public static float intangibleThreshold = 3f;
    public static bool handleEnabled;
    protected bool handleActive;

    public Handle handle;

    public delegate void HandleGrabEvent(SpellCastSlingblade spell, Handle handle, EventTime time);
    public event HandleGrabEvent OnHandleGrabEvent;

    public delegate void BladeEvent(SpellCastSlingblade spell, Blade blade);
    public event BladeEvent OnBladeSpawnEvent;

    public delegate void QuiverEvent(SpellCastSlingblade spell, Quiver quiver);
    public event QuiverEvent OnQuiverLoadEvent;

    public delegate void BladeThrowEvent(SpellCastSlingblade spell, Vector3 velocity, Blade blade);
    public event BladeThrowEvent OnBladeThrowEvent;

    public event SpellEvent OnSpellUpdateLoopEvent;

    public delegate void HitEntityEvent(
        SpellCastSlingblade spell,
        Blade blade,
        ThunderEntity entity,
        CollisionInstance hit);

    public event HitEntityEvent OnHitEntityEvent;

    public Step root;

    public Quiver quiver;

    public Transform anchor;

    public override void OnCatalogRefresh() {
        base.OnCatalogRefresh();
        Blade.itemData = Catalog.GetData<ItemData>(itemId);
        handleData = Catalog.GetData<HandleData>(handleId);
        ModOptions.Setup();
        var soldier = Catalog.GetData<ContainerData>("SoldierAllemande");
        Debug.Log("LOGGING STUFF");
        foreach (var content in soldier.containerContents) {
            switch (content) {
                case SpellContent spell:
                    Debug.Log($"{spell.referenceID}: {Catalog.GetData<SpellData>(spell.referenceID)}");
                    break;
                case ItemContent item:
                    Debug.Log($"{item.referenceID}: {Catalog.GetData<ItemData>(item.referenceID)}");
                    break;
                case TableContent table:
                    Debug.Log($"{table.referenceID}: {Catalog.GetData<LootTable>(table.referenceID)}");
                    break;
                case SkillContent skill:
                    Debug.Log($"{skill.referenceID}: {Catalog.GetData<SkillData>(skill.referenceID)}");
                    break;
            }
        }
    }

    public override void Load(SpellCaster spellCaster) {
        base.Load(spellCaster);

        anchor = new GameObject().transform;
        anchor.transform.SetParent(spellCaster.ragdollHand.transform);
        anchor.transform.SetPositionAndRotation(spellCaster.ragdollHand.transform.position,
            Quaternion.LookRotation(spellCaster.ragdollHand.PointDir, spellCaster.ragdollHand.ThumbDir));

        if (spellCaster.mana.creature.isPlayer) {
            var obj = new GameObject().AddComponent<Rigidbody>().gameObject;
            handle = obj.AddComponent<Handle>();
            handle.physicBody.isKinematic = true;
            handle.Load(handleData);
            handle.data.highlightDefaultTitle = "Slicer";
            handle.SetTouch(false);
            handleActive = false;
            handle.allowedHandSide
                = spellCaster.side == Side.Left ? Interactable.HandSide.Right : Interactable.HandSide.Left;
            handle.Grabbed += OnHandleGrab;
            handle.UnGrabbed += OnHandleUnGrab;
        }

        quiver = Quiver.Get(spellCaster.mana.creature);
        OnQuiverLoadEvent?.Invoke(this, quiver);
        root = Step.Start();
        SetupModifiers();
    }

    protected void OnHandleGrab(RagdollHand hand, Handle handle, EventTime time) {
        if (time == EventTime.OnStart) {
            handle.physicBody.isKinematic = false;
        } else {
            OnHandleGrabEvent?.Invoke(this, handle, EventTime.OnStart);
        }
    }
    protected void OnHandleUnGrab(RagdollHand hand, Handle handle, EventTime time) {
        if (time == EventTime.OnStart) {
            handle.physicBody.isKinematic = true;
        } else {
            OnHandleGrabEvent?.Invoke(this, handle, EventTime.OnEnd);
        }
    }

    public override void Unload() {
        base.Unload();

        if (!spellCaster.ragdollHand.ragdoll.creature.isPlayer) {
            spellCaster.ragdollHand.ragdoll.forcePhysic.Remove(this);
        }

        if (!handle) return;
        Object.Destroy(handle);
        handle = null;
    }

    public override void Fire(bool active) {
        if (active && spellCaster.ragdollHand.grabbedHandle) return;
        base.Fire(active);
        if (active) {
            bool wasFreeCharge = freeCharge;
            if (freeCharge) {
                freeCharge = false;
                AddModifier(this, Modifier.ChargeSpeed, 100);
            }

            if (quiver.Count > 0)
                AddModifier(this, Modifier.ChargeSpeed, 20);
            readyHaptic = false;
            Blade.Spawn(OnBladeSpawn, HandPosition, HandRotation, spellCaster.mana.creature, !Quiver.Get(spellCaster.mana.creature).IsFull && wasFreeCharge);
        } else {
            root.Reset();
            OnCastStop();
        }
    }

    public void OnCastStop(bool skipQuiver = false) {
        if (handle) {
            handle.Release();
            handle.SetTouch(false);
            handleActive = false;
        }

        DismissActiveBlade(skipQuiver);
    }

    public void DismissActiveBlade(bool skipQuiver = false) {
        if (activeBlade != null) {
            activeBlade.transform.localScale = Vector3.one;
        }

        if (!skipQuiver && activeBlade != null && !CanThrow) {
            if (quiver.AddToQuiver(activeBlade)) {
                activeBlade = null;
                return;
            }
        }

        ReleaseBlade();
    }

    public bool CanThrow => allowThrow
                            && spellCaster.allowCasting
                            && !spellCaster.grabbedFire
                            && (!spellCaster.mana.creature.player
                                || (!spellCaster.ragdollHand.playerHand.controlHand.gripPressed
                                    && !spellCaster.mana.mergeActive
                                    && currentCharge > throwMinCharge
                                    && (Player.local.transform.rotation
                                        * PlayerControl.GetHand(spellCaster.ragdollHand.side).GetHandVelocity())
                                    .magnitude
                                    > SpellCaster.throwMinHandVelocity
                                ));

    public void ReleaseBlade() {
        RemoveModifier(this, Modifier.ChargeSpeed);
        if (activeBlade == null) return;
        activeBlade.Release();
        activeBlade.transform.localScale = Vector3.one;
        activeBlade = null;
    }

    public Vector3 HandPosition => spellCaster.ragdollHand.transform.position - spellCaster.ragdollHand.PalmDir * 0.1f;

    public Quaternion HandRotation => Quaternion.AngleAxis(
                                          (1 - Mathf.Sqrt(rotationCurve.Evaluate(currentCharge))) * 180,
                                          spellCaster.ragdollHand.PalmDir
                                          * (spellCaster.ragdollHand.side == Side.Right ? -1 : 1))
                                      * Quaternion.LookRotation(spellCaster.ragdollHand.PointDir,
                                          -spellCaster.ragdollHand.PalmDir);

    public override void UpdateCaster() {
        base.UpdateCaster();
        OnSpellUpdateLoopEvent?.Invoke(this);
        root.Update();
        if (handle && !handleActive && handleEnabled && currentCharge > throwMinCharge) {
            handle.SetTouch(true);
            handleActive = true;
        }

        if (root.AtEnd()) root.Reset();
        if (!activeBlade) return;

        if (handle && !handle.IsHanded()) handle.transform.position = spellCaster.magicSource.position;
        if (currentCharge < 1) {
            activeBlade.MoveTo(new MoveTarget(MoveMode.Joint, handJointLerp)
                .Parent(spellCaster.magicSource)
                .AtWorld(HandPosition, HandRotation));
        }

        activeBlade.transform.localScale = Vector3.one * currentCharge;
        if (currentCharge > throwMinCharge) {
            if (spellCaster.ragdollHand.creature.isPlayer
                && spellCaster.ragdollHand.playerHand.controlHand.gripPressed
                && !spellCaster.ragdollHand.grabbedHandle) {
                var blade = activeBlade;
                ReleaseBlade();
                blade.item.StopFlying();
                blade.item.StopThrowing();
                var bladeHandle = blade.item.GetMainHandle(spellCaster.ragdollHand.side);
                spellCaster.ragdollHand.Grab(bladeHandle,
                    bladeHandle.GetNearestOrientation(spellCaster.ragdollHand.grip, spellCaster.ragdollHand.side), 0,
                    true);
                return;
            }

            if (readyHaptic) return;
            readyHaptic = true;
            spellCaster.ragdollHand.HapticTick();
        } else {
            spellCaster.ragdollHand.HapticTick(currentCharge / 2);
        }
    }

    public void OnBladeSpawn(Blade blade, bool isNew) {
        activeBlade = blade;
        blade.SetTouch(false);
        blade.OnlyIgnoreRagdoll(spellCaster.mana.creature.ragdoll);
        blade.quiver = quiver;

        if (isNew) {
            activeBlade.transform.localScale = Vector3.zero;
            activeBlade.transform.SetPositionAndRotation(
                spellCaster.ragdollHand.transform.position - spellCaster.ragdollHand.PalmDir * 0.1f,
                Quaternion.LookRotation(spellCaster.ragdollHand.PointDir, -spellCaster.ragdollHand.PalmDir));
        }

        activeBlade.MoveTo(new MoveTarget(MoveMode.Joint, handJointLerp)
            .Parent(spellCaster.magicSource)
            .AtWorld(HandPosition, HandRotation));
        OnBladeSpawnEvent?.Invoke(this, blade);
    }

    public override void Throw(Vector3 velocity) {
        base.Throw(velocity);
        if (spellCaster.mana.creature.isPlayer) {
            velocity = Vector3.Slerp(velocity.normalized, spellCaster.mana.creature.ragdoll.headPart.transform.forward,
                           0.5f)
                       * velocity.magnitude;
        } else {
            velocity *= aiThrowForce;
        }

        if (!activeBlade) return;
        activeBlade.transform.position = HandPosition;
        foreach (var eachImbue in activeBlade.item.imbues) {
            if (eachImbue.spellCastBase is not SpellCastGravity) continue;
            velocity *= gravityForceCompensation;
            break;
        }

        spellCaster.ragdollHand.playerHand?.controlHand.HapticPlayClip(Catalog.gameData.haptics.bowShoot);

        activeBlade.OnlyIgnoreRagdoll(spellCaster.mana.creature.ragdoll);
        activeBlade.AddForce(velocity * throwForce, ForceMode.VelocityChange, aimAssist, true, spellCaster.ragdollHand);
        activeBlade.item.lastHandler = spellCaster.ragdollHand;
        activeBlade.wasSlung = true;
        Blade.slung.Add(activeBlade);
        activeBlade.isDangerous.Add(Blade.UntilHit);
        activeBlade.OnSlingEndEvent -= OnSlingEnd;
        activeBlade.OnSlingEndEvent += OnSlingEnd;
        activeBlade.OnHitEntityEvent -= OnHit;
        activeBlade.OnHitEntityEvent += OnHit;
        try {
            OnBladeThrowEvent?.Invoke(this, velocity, activeBlade);
        } catch (Exception e) {
            Debug.LogWarning("Exception in OnBladeThrowEvent");
            Debug.LogException(e);
        }

        if (!spellCaster.mana.creature.isPlayer) {
            OnCastStop(true);
        }

        return;

        void OnSlingEnd(Blade blade) {
            Blade.slung.Remove(activeBlade);
            blade.OnHitEntityEvent -= OnHit;
            blade.OnSlingEndEvent -= OnSlingEnd;
        }

        void OnHit(Blade blade, ThunderEntity entity, CollisionInstance hit) {
            blade.OnHitEntityEvent -= OnHit;
            OnHitEntityEvent?.Invoke(this, blade, entity, hit);
        }
    }
}

public enum Mode {
    Crown,
    Slicer,
    Rain,
    Blender
}