#define BLADEDANCER

using System;
using System.Collections.Generic;
using Bladedancer.Skills;
using SequenceTracker;
using ThunderRoad;
using ThunderRoad.Skill.Spell;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bladedancer;

public class SpellCastBlade : SpellCastCharge {
    public string defaultBladeId = "ThrowablesDagger";
    public List<string> allowedQuiverItemIds;

    public string handleId = "ObjectHandleLight";
    public HandleData handleData;

    [SkillCategory("General")]
    [ModOptionSlider, ModOptionFloatValuesDefault(5, 20, 5, 10)]
    [ModOption("Default Joint Mass Scale", "Blade joint-mode mass scale. Makes joint-based movement stronger.")]
    public static float jointMassScale;

    [SkillCategory("General")]
    [ModOptionSlider, ModOptionFloatValuesDefault(250, 1000, 250, 500)]
    [ModOption("Default Joint Spring", "Blade joint-mode spring strength")]
    public static float jointSpring;

    [SkillCategory("General")]
    [ModOptionSlider, ModOptionFloatValuesDefault(0, 100, 1f, 10)]
    [ModOption("Default Joint Damper", "Joint spring damper")]
    public static float jointDamper;

    [SkillCategory("General")]
    [ModOptionSlider, ModOptionFloatValuesDefault(5, 30, 1, 20)]
    [ModOption("Scale Speed", "Speed at which weapons scale themselves (when necessary).")]
    public static float scaleSpeed;

    public static float handJointLerp = 20f;

    [SkillCategory("General")]
    [ModOptionSlider, ModOptionFloatValuesDefault(5000, 100000, 5000, 10000)]
    [ModOption("Joint Max Force", "Joint maximum force")]
    public static float jointMaxForce;

    // [SkillCategory("General")]
    // [ModOption("PID Force Mode", "PID force application mode")]
    public static ForceMode pidForceMode = ForceMode.Acceleration;

    [SkillCategory("General")]
    [ModOptionSlider, ModOptionFloatValuesDefault(50, 300, 50, 100)]
    [ModOption("PID Max Force", "PID Max Force")]
    public static float pidMaxForce;

    [SkillCategory("General")]
    [ModOptionSlider, ModOptionFloatValuesDefault(0, 10, 1, 1)]
    [ModOption("PID Damping", "PID Damping")]
    public static float pidDamping;

    [SkillCategory("General")]
    [ModOptionSlider, ModOptionFloatValuesDefault(0, 5, 1, 3)]
    [ModOption("Despawn Time", "Time before a blade despawns or is collected")]
    public static float collectTime;

    [SkillCategory("Slingblade", Category.Base, 1, 0)]
    [ModOptionSlider, ModOptionFloatValuesDefault(8, 20, 2, 12)]
    [ModOption("Throw Force", "Force added when thrown using Slingblade")]
    public static float throwForce;

    [SkillCategory("Slingblade", Category.Base, 1, 0)]
    [ModOptionSlider, ModOptionFloatValuesDefault(2, 10, 2, 7)]
    [ModOption("AI Throw Force", "Forced throw hand velocity for NPCs using Slingblade")]
    public static float aiThrowForce;

    [SkillCategory("Slingblade", Category.Base, 1, 0)]
    [ModOptionSlider, ModOptionFloatValuesDefault(1, 5, 0.5f, 3.5f)]
    [ModOption("Gravity Force Compensation", "Force multiplier when throwing a Gravity-imbued blade using Slingblade")]
    public static float gravityForceCompensation;

    public static bool aimAssist = false;
    public bool stealIfNearby;

    protected AnimationCurve rotationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    protected bool readyHaptic = false;

    public Blade activeBlade;

    public static float intangibleThreshold = 3f;
    public static bool handleEnabled;
    protected bool handleActive;

    public Handle handle;

    public delegate void HandleGrabEvent(SpellCastBlade spell, Handle handle, EventTime time);
    public event HandleGrabEvent OnHandleGrabEvent;

    public delegate void BladeEvent(SpellCastBlade spell, Blade blade);
    public event BladeEvent OnBladeSpawnEvent;

    public delegate void QuiverEvent(SpellCastBlade spell, Quiver quiver);
    public event QuiverEvent OnQuiverLoadEvent;

    public delegate void BladeThrowEvent(SpellCastBlade spell, Vector3 velocity, Blade blade);
    public event BladeThrowEvent OnBladeThrowEvent;

    public event SpellEvent OnSpellUpdateLoopEvent;

    public delegate void HitEntityEvent(
        SpellCastBlade spell,
        Blade blade,
        ThunderEntity entity,
        CollisionInstance hit);

    public event HitEntityEvent OnHitEntityEvent;

    public Step root;

    public Quiver quiver;

    public BoolHandler disableHandle;

    public string spawnEffectId = "SpawnBlade";
    public EffectData spawnEffectData;
    public string retrieveEffectId = "RetrieveBlade";
    public EffectData retrieveEffectData;

    public Transform anchor;
    private Blade lastThrownBlade;

    private Trigger.CallBack orgImbueTriggerCallback;
    private bool hasReachedFullCharge;

    public override void OnCatalogRefresh() {
        base.OnCatalogRefresh();
        Blade.itemData = Catalog.GetData<ItemData>(defaultBladeId);
        Quiver.allowedQuiverItemHashIds = new List<int>();
        if (allowedQuiverItemIds is not null) {
            for (var i = 0; i < allowedQuiverItemIds.Count; i++) {
                if (string.IsNullOrEmpty(allowedQuiverItemIds[i])) continue;
                Quiver.allowedQuiverItemHashIds.Add(Animator.StringToHash(allowedQuiverItemIds[i].ToLower()));
            }
        }
        handleData = Catalog.GetData<HandleData>(handleId);

        spawnEffectData = Catalog.GetData<EffectData>(spawnEffectId);
        retrieveEffectData = Catalog.GetData<EffectData>(retrieveEffectId);
        ModOptions.Setup();
    }

    public override void Load(SpellCaster spellCaster) {
        base.Load(spellCaster);
        
        disableHandle = new BoolHandler(false);
        disableHandle.OnChangeEvent += DisableHandleChanged;

        anchor = new GameObject().transform;
        anchor.transform.SetParent(spellCaster.ragdollHand.transform);
        anchor.transform.SetPositionAndRotation(spellCaster.ragdollHand.transform.position,
            Quaternion.LookRotation(spellCaster.ragdollHand.PointDir, spellCaster.ragdollHand.ThumbDir));

        if (spellCaster.mana.creature.isPlayer) {
            var obj = new GameObject().AddComponent<Rigidbody>().gameObject;
            handle = obj.AddComponent<Handle>();
            handle.physicBody.isKinematic = true;
            handle.Load(handleData);
            handle.data.localizationId = "ThisCanBeAnythingForSomeReason";
            handle.data.highlightDefaultTitle = "Slingshot";
            handle.data.highlightDefaultTitle = "Slingshot";
            handle.handOverlapColliders = new List<Collider>();
            handle.SetTouch(false);
            handle.SetTelekinesis(false);
            handleActive = false;
            handle.allowedHandSide
                = spellCaster.side == Side.Left ? Interactable.HandSide.Right : Interactable.HandSide.Left;
            handle.Grabbed += OnHandleGrab;
            handle.UnGrabbed += OnHandleUnGrab;
        }

        orgImbueTriggerCallback = spellCaster.imbueTrigger.callBack;
        spellCaster.imbueTrigger.SetCallback(OnTriggerImbue);

        root = Step.Start();
        SetupModifiers();
        Init();
    }

    private void DisableHandleChanged(bool oldValue, bool newValue) {
        if (handle == null) return;
        
        // If the handle doesn't want to be active as per the spell, return
        if (!handleActive) return;
        handle.SetTouch(!newValue);
        if (newValue)
            handle.Release();
    }

    public void Init() {
        quiver = Quiver.Get(spellCaster?.mana.creature ?? imbue.imbueCreature);
        OnQuiverLoadEvent?.Invoke(this, quiver);
    }

    protected void OnTriggerImbue(Collider other, bool enter) {
        var colliderGroup = other.GetComponentInParent<ColliderGroup>()?.RootGroup;
        if (!colliderGroup) return;
        if (enter) {
            if (colliderGroup.modifier.imbueType == ColliderGroupData.ImbueType.None) return;

            // Can't imbue metal if the spellInstance doesn't support it
            if (imbueAllowMetal == false
                && colliderGroup.modifier.imbueType == ColliderGroupData.ImbueType.Metal) return;

            // Don't allow imbue if the CG has a defined imbueCustomSpellID that is not the selected spell
            if (!string.IsNullOrEmpty(colliderGroup.imbueCustomSpellID)
                && !string.Equals(id, colliderGroup.imbueCustomSpellID, StringComparison.OrdinalIgnoreCase)) return;

            // Don't allow imbue if the CG data disallows this spell from imbuing it
            if (colliderGroup.modifier.imbueType == ColliderGroupData.ImbueType.Custom
                && (colliderGroup.imbueCustomSpellID == null
                    || !string.Equals(spellCaster.spellInstance.id, colliderGroup.imbueCustomSpellID,
                        StringComparison.OrdinalIgnoreCase))) return;

            // Check if already present
            for (int i = spellCaster.imbueObjects.Count - 1; i >= 0; i--) {
                if (spellCaster.imbueObjects[i].colliderGroup == colliderGroup) return;
            }

            var item = colliderGroup.gameObject.GetComponentInParent<Item>();
            if (item.TryGetComponent<Blade>(out var blade) && blade.isDefaultBlade) return;

            // Add ColliderGroup to list
            spellCaster.imbueObjects.Add(new SpellCaster.ImbueObject(colliderGroup));
        } else {
            // Remove ColliderGroup to list
            for (int i = spellCaster.imbueObjects.Count - 1; i >= 0; i--) {
                var imbueObject = spellCaster.imbueObjects[i];
                if (imbueObject.colliderGroup != colliderGroup) continue;
                imbueObject.effectInstance?.End();
                spellCaster.imbueObjects.RemoveAt(i);
            }
        }
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

        if (spellCaster == null) return;
        spellCaster.imbueTrigger.callBack = orgImbueTriggerCallback;
        if (!spellCaster.ragdollHand.ragdoll.creature.isPlayer) {
            spellCaster.ragdollHand.ragdoll.forcePhysic.Remove(this);
        }

        if (!handle) return;
        Object.Destroy(handle);
        handle = null;
    }

    public bool FreeCharge {
        get => spellCaster
               && spellCaster.ragdollHand.creature.TryGetVariable(SkillLethalReturn.FreeCharge,
                   out bool freeCharge)
               && freeCharge;
        set => spellCaster.ragdollHand.creature.SetVariable(SkillLethalReturn.FreeCharge, value);
    }

    public override void Fire(bool active) {
        if (active && spellCaster.ragdollHand.grabbedHandle) return;
        base.Fire(active);

        if (imbueEnabled) {
            spellCaster.imbueTrigger.SetRadius(imbueRadius);
        } else {
            spellCaster.imbueTrigger.SetRadius(0);
            spellCaster.imbueTrigger.SetActive(false);
        }

        if (active) {
            bool wasFreeCharge = FreeCharge;
            if (FreeCharge) {
                FreeCharge = false;
                AddModifier(this, Modifier.ChargeSpeed, 100);
            }

            if (quiver.Count > 0)
                AddModifier(this, Modifier.ChargeSpeed, 20);
            lastThrownBlade?.StopGuidance();
            lastThrownBlade = null;
            readyHaptic = false;
            if (stealIfNearby
                && Blade.TryGetClosestSlungInRadius(spellCaster.magicSource.position, SkillKnifethief.grabRadius,
                    out var closest, quiver)) {
                closest.item.StopFlying();
                closest.item.StopThrowing();
                closest.isDangerous.Remove(Blade.UntilHit);
                currentCharge = 1;
                OnBladeSpawn(closest, false);
            } else {
                Blade.Spawn(OnBladeSpawn, HandPosition, HandRotation, spellCaster.mana.creature,
                    !Quiver.Get(spellCaster.mana.creature).IsFull && wasFreeCharge);
            }
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
                RemoveModifier(this, Modifier.ChargeSpeed);
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

        if (handle && !handle.IsHanded()) handle.transform.position = spellCaster.magicSource.position;

        if (lastThrownBlade
            && lastThrownBlade.guided
            && spellCaster.ragdollHand.creature.isPlayer
            && spellCaster.ragdollHand.playerHand.controlHand.gripPressed != true) {
            lastThrownBlade.StopGuidance(true);
        }

        if (root.AtEnd()) root.Reset();
        if (!activeBlade) return;

        if (currentCharge < 1) {
            hasReachedFullCharge = false;
            activeBlade.MoveTo(new MoveTarget(MoveMode.Joint, handJointLerp)
                .Parent(spellCaster.magicSource)
                .AtWorld(HandPosition, HandRotation));
        } else if (!hasReachedFullCharge) {
            hasReachedFullCharge = true;
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
        if (!isNew)
            currentCharge = 1;
        activeBlade = blade;
        blade.SetTouch(false);
        blade.OnlyIgnoreRagdoll(spellCaster.mana.creature.ragdoll, true);
        blade.item.RefreshCollision();
        blade.Quiver = quiver;
        blade.item.lastHandler = spellCaster.ragdollHand;
        blade.item.OnDespawnEvent += ActiveBladeDespawn;

        if (isNew) {
            spawnEffectData?.Spawn(spellCaster.magicSource).Play();
        } else {
            retrieveEffectData?.Spawn(spellCaster.magicSource).Play();
        }

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
        return;

        void ActiveBladeDespawn(EventTime time) {
            blade.item.OnDespawnEvent -= ActiveBladeDespawn;
            if (activeBlade == blade && activeBlade != null) {
                Fire(false);
            }
        }
    }


    // IMBUE
    public override void Load(Imbue imbue) {
        base.Load(imbue);
        Init();
        if (imbue.colliderGroup.collisionHandler?.item is not Item item) return;

        imbue.SetEnergyInstant(imbue.maxEnergy);

        var blade = item.gameObject.GetOrAddComponent<Blade>();
        blade.Quiver = quiver;
        blade.isDefaultBlade = false;
        blade.AllowDespawn(true);
        for (var i = spellCaster.imbueObjects.Count - 1; i >= 0; i--) {
            if (imbue.spellCastBase.spellCaster.imbueObjects[i].colliderGroup?.collisionHandler?.item is Item
                    collidingItem
                && collidingItem == blade.item) {
                if (imbue.spellCastBase.spellCaster.spellInstance is SpellCastBlade imbuingSpell)
                    imbuingSpell.OnTriggerImbue(imbue.colliderGroup.colliders[0], false);
                break;
            }
        }
        if (blade.item.IsFree) {
            if (quiver.IsFull) {
                if (quiver.TryGetClosestBlade(item.transform.position, out Blade replacement, null, true)) replacement.Release();
            }

            if (blade.ReturnToQuiver(quiver, true)) return;
        } else if (blade.item.mainHandler != null) {
            blade.item.OnUngrabEvent += UnGrab;
            return;
        }

        quiver.OnAvailableSpace += AvailableSpace;
        blade.OnAddToQuiver += AddToQuiver;
        blade.item.OnDespawnEvent += Despawn;
        return;

        void UnGrab(Handle _, RagdollHand __, bool ___) {
            blade.item.OnUngrabEvent -= UnGrab;
            if (blade.item.IsFree && !blade.item.isCulled) {
                if (quiver.IsFull) {
                    if (quiver.TryGetClosestBlade(item.transform.position, out var replacement, null, true))
                        replacement.Release();
                }

                if (blade.ReturnToQuiver(quiver, true)) return;
            }

            quiver.OnAvailableSpace += AvailableSpace;
            blade.OnAddToQuiver += AddToQuiver;
        }

        void AvailableSpace(Quiver _) {
            if (!blade.IsFree) return;
            quiver.OnAvailableSpace -= AvailableSpace;
            blade.OnAddToQuiver -= AddToQuiver;
            blade.item.OnDespawnEvent -= Despawn;
            blade.item.OnUngrabEvent -= UnGrab;
            blade.ReturnToQuiver(quiver);
        }

        void AddToQuiver(Blade _, Quiver __) {
            quiver.OnAvailableSpace -= AvailableSpace;
            blade.OnAddToQuiver -= AddToQuiver;
            blade.item.OnDespawnEvent -= Despawn;
            blade.item.OnUngrabEvent -= UnGrab;
        }

        void Despawn(EventTime time) {
            if (time == EventTime.OnEnd) return;

            blade.item.OnDespawnEvent -= Despawn;
            blade.item.OnUngrabEvent -= UnGrab;
            quiver.OnAvailableSpace -= AvailableSpace;
            blade.OnAddToQuiver -= AddToQuiver;
        }
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
        
        throwEffectData?.Spawn(spellCaster.magicSource).Play();

        activeBlade.OnlyIgnoreRagdoll(spellCaster.mana.creature.ragdoll, true);
        activeBlade.AddForce(velocity * throwForce, ForceMode.VelocityChange, aimAssist, true, spellCaster.ragdollHand);
        activeBlade.item.lastHandler = spellCaster.ragdollHand;
        activeBlade.wasSlung = true;
        Blade.slung.Add(activeBlade);
        lastThrownBlade = activeBlade;
        activeBlade.isDangerous.Add(Blade.UntilHit);
        activeBlade.OnSlingEnd -= OnSlingEnd;
        activeBlade.OnSlingEnd += OnSlingEnd;
        activeBlade.OnHitEntity -= OnHit;
        activeBlade.OnHitEntity += OnHit;
        activeBlade.StopGuidance();
        try {
            OnBladeThrowEvent?.Invoke(this, velocity, activeBlade);
        } catch (Exception e) {
            Debug.LogWarning("Exception in OnBladeThrowEvent");
            Debug.LogException(e);
        }

        // This is such a hack lol
        float length = activeBlade.item.parryTargets[0].length;
        activeBlade.item.parryTargets[0].length = 0.2f;
        spellCaster.mana.creature.InvokeOnThrowEvent(spellCaster.ragdollHand,
            activeBlade.item.GetMainHandle(spellCaster.side));
        activeBlade.item.parryTargets[0].length = length;

        if (!spellCaster.mana.creature.isPlayer) {
            OnCastStop(true);
        }

        return;

        void OnSlingEnd(Blade blade) {
            Blade.slung.Remove(activeBlade);
            blade.OnHitEntity -= OnHit;
            blade.OnSlingEnd -= OnSlingEnd;
        }

        void OnHit(Blade blade, ThunderEntity entity, CollisionInstance hit) {
            blade.OnHitEntity -= OnHit;
            OnHitEntityEvent?.Invoke(this, blade, entity, hit);
        }
    }
}

public enum Mode {
    Crown,
    Slicer,
    Rain,
    Blender,
    Volley,
    VolleySpraying
}