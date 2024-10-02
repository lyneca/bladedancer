#define BLADEDANCER

using System;
using System.Collections.Generic;
using System.Reflection;
using Bladedancer.Skills;
using HarmonyLib;
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
    [ModOption("Allow Ranged Expert", "Whether to allow Ranged Expert to function with blades summoned by this mod. It can be buggy.", defaultValueIndex = 0)]
    public static bool allowRangedExpert;

    [SkillCategory("General")]
    [ModOptionSlider, ModOptionFloatValues(5, 20, 5)]
    [ModOption("Default Joint Mass Scale", "Blade joint-mode mass scale. Makes joint-based movement stronger.")]
    public static float jointMassScale = 10;

    [SkillCategory("General")]
    [ModOptionSlider, ModOptionFloatValues(250, 1000, 250)]
    [ModOption("Default Joint Spring", "Blade joint-mode spring strength")]
    public static float jointSpring = 500;

    [SkillCategory("General")]
    [ModOptionSlider, ModOptionFloatValues(0, 100, 1f)]
    [ModOption("Default Joint Damper", "Joint spring damper")]
    public static float jointDamper = 10;

    [SkillCategory("General")]
    [ModOptionSlider, ModOptionFloatValues(5, 30, 1)]
    [ModOption("Scale Speed", "Speed at which weapons scale themselves (when necessary).")]
    public static float scaleSpeed = 20;

    public static float handJointLerp = 20f;

    [SkillCategory("General")]
    [ModOptionSlider, ModOptionFloatValues(5000, 100000, 5000)]
    [ModOption("Joint Max Force", "Joint maximum force")]
    public static float jointMaxForce = 10000;

    // [SkillCategory("General")]
    // [ModOption("PID Force Mode", "PID force application mode")]
    public static ForceMode pidForceMode = ForceMode.Acceleration;

    [SkillCategory("General")]
    [ModOptionSlider, ModOptionFloatValues(50, 300, 50)]
    [ModOption("PID Max Force", "PID Max Force")]
    public static float pidMaxForce = 100;

    [SkillCategory("General")]
    [ModOptionSlider, ModOptionFloatValues(0, 10, 1)]
    [ModOption("PID Damping", "PID Damping")]
    public static float pidDamping = 1;

    [SkillCategory("General")]
    [ModOptionSlider, ModOptionFloatValues(0, 5, 0.5f)]
    [ModOption("Despawn Time", "Time before a blade despawns or is collected")]
    public static float collectTime = 1.5f;

    [SkillCategory("Slingblade", Category.Base, 1, 0)]
    [ModOptionSlider, ModOptionFloatValues(8, 20, 2)]
    [ModOption("Throw Force", "Force added when thrown using Slingblade")]
    public static float throwForce = 12;

    [SkillCategory("Slingblade", Category.Base, 1, 0)]
    [ModOptionSlider, ModOptionFloatValues(2, 10, 1)]
    [ModOption("AI Throw Force", "Forced throw hand velocity for NPCs using Slingblade")]
    public static float aiThrowForce = 7;

    [SkillCategory("Slingblade", Category.Base, 1, 0)]
    [ModOptionSlider, ModOptionFloatValues(1, 5, 0.5f)]
    [ModOption("Gravity Force Compensation", "Force multiplier when throwing a Gravity-imbued blade using Slingblade")]
    public static float gravityForceCompensation = 3.5f;

    [SkillCategory("Staff Slam", Category.Base, 1)]
    [ModOptionSlider, ModOptionIntValues(1, 12, 1)]
    [ModOption("Staff Slam Blade Count", "Number of blades fired when you slam the staff")]
    public static int slamBladeCount = 6;
    
    [SkillCategory("Staff Slam", Category.Base, 1)]
    [ModOptionSlider, ModOptionFloatValues(1, 40, 1f)]
    [ModOption("Staff Slam Throw Force", "Force multiplier on blades summoned via staff slam")]
    public static float slamThrowForce = 20;
    
    public static bool aimAssist = false;
    public bool stealIfNearby;

    protected AnimationCurve rotationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    protected bool readyHaptic = false;

    public Blade activeBlade;

    public static float intangibleThreshold = 3f;
    public static bool handleEnabled;
    protected bool handleActive;

    public Handle slingshotHandle;

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
    public BoolHandler disableImbue;

    public string spawnEffectId = "SpawnBlade";
    public EffectData spawnEffectData;
    public string retrieveEffectId = "RetrieveBlade";
    public EffectData retrieveEffectData;

    public Transform anchor;
    private Blade lastThrownBlade;

    private Trigger.CallBack orgImbueTriggerCallback;
    private bool hasReachedFullCharge;
    private SpellCastBlade baseSpell;
    private float orgAiCastMaxDistance;
    private bool spawning;

    public void Localize(string group, string text) {
        Debug.Log($"{group}/{text}: \"{LocalizationManager.Instance.GetLocalizedString(group, text)}\"");
    }

    public override void OnSkillLoaded(SkillData skillData, Creature creature) {
        base.OnSkillLoaded(skillData, creature);
    }

    public override void OnSkillUnloaded(SkillData skillData, Creature creature) {
        base.OnSkillUnloaded(skillData, creature);
        if (creature.isPlayer)
            creature.ClearVariable<ItemData>(Blade.BladeItem);
    }

    public override void OnCatalogRefresh() {
        base.OnCatalogRefresh();

        orgAiCastMaxDistance = aiCastMaxDistance;

        var harmony = new Harmony("com.lyneca.bladedancer");
        harmony.PatchAll();

        LocalizationManager.Instance.OnLanguageChanged(LocalizationManager.Instance.Language);
        
        IngameDebugConsole.DebugLogConsole.AddCommand("localize", "Localize a string", Localize);
        
        EventManager.onPossess += OnPossess;

        Debug.Log(
            $"Bladedancer version {ModManager.GetModDataFromAssembly(Assembly.GetCallingAssembly().FullName)?.ModVersion ?? "[unknown]"}");

        Blade.defaultBladeId = defaultBladeId;
        Blade.defaultItemData = Catalog.GetData<ItemData>(defaultBladeId);
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

    private void OnPossess(Creature creature, EventTime time) {
        if (time != EventTime.OnEnd) return;
        creature.SetVariable(Blade.BladeItem, Blade.LoadSavedCustomBlade() ?? Catalog.GetData<ItemData>(defaultBladeId));
    }

    public override void Load(SpellCaster spellCaster) {
        base.Load(spellCaster);
        
        disableHandle = new BoolHandler(false);
        disableHandle.OnChangeEvent += DisableHandleChanged;

        disableImbue = new BoolHandler(false);
        if (imbueEnabled) {
            disableImbue.OnChangeEvent += DisableImbueChanged;
        }

        anchor = new GameObject().transform;
        anchor.transform.SetParent(spellCaster.ragdollHand.transform);
        anchor.transform.SetPositionAndRotation(spellCaster.ragdollHand.transform.position,
            Quaternion.LookRotation(spellCaster.ragdollHand.PointDir, spellCaster.ragdollHand.ThumbDir));

        if (spellCaster.mana.creature.isPlayer) {
            var obj = new GameObject().AddComponent<Rigidbody>().gameObject;
            slingshotHandle = obj.AddComponent<Handle>();
            slingshotHandle.physicBody.isKinematic = true;
            slingshotHandle.Load(handleData);
            slingshotHandle.data.localizationId = "ThisCanBeAnythingForSomeReason";
            slingshotHandle.data.highlightDefaultTitle = "Slingshot";
            slingshotHandle.data.highlightDefaultTitle = "Slingshot";
            slingshotHandle.handOverlapColliders = new List<Collider>();
            slingshotHandle.SetTouch(false);
            slingshotHandle.SetTelekinesis(false);
            handleActive = false;
            slingshotHandle.allowedHandSide
                = spellCaster.side == Side.Left ? Interactable.HandSide.Right : Interactable.HandSide.Left;
            slingshotHandle.Grabbed += SlingshotHandleGrab;
            slingshotHandle.UnGrabbed += SlingshotHandleUnGrab;
        }

        // orgImbueTriggerCallback = spellCaster.imbueTrigger.callBack;
        // spellCaster.imbueTrigger.SetCallback(OnTriggerImbue);

        root = Step.Start();
        SetupModifiers();
        Init();
    }

    private void DisableImbueChanged(bool oldValue, bool newValue) {
        imbueEnabled = !newValue;
        spellCaster.imbueTrigger.SetRadius(imbueEnabled ? imbueRadius : 0);
    }

    private void DisableHandleChanged(bool oldValue, bool newValue) {
        if (slingshotHandle == null) return;
        
        // If the handle doesn't want to be active as per the spell, return
        if (!handleActive) return;
        slingshotHandle.SetTouch(!newValue);
        if (newValue)
            slingshotHandle.Release();
    }

    public void Init() {
        quiver = Quiver.Get(spellCaster?.mana.creature ?? imbue.imbueCreature);
        if (quiver == null) return;
        for (var i = 0; i < quiver.creature.mana.spells.Count; i++) {
            var spell = quiver.creature.mana.spells[i];
            if (spell.hashId == hashId) {
                baseSpell = spell as SpellCastBlade;
                break;
            }
        }
        OnQuiverLoadEvent?.Invoke(this, quiver);
        // if (baseSpell != null) {
        //     quiver.OnCountChangeEvent += _ => {
        //         baseSpell.aiCastMaxDistance = quiver.FullyEmpty ? 0 : orgAiCastMaxDistance;
        //     };
        // }
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
            if (item.TryGetComponent<Blade>(out var blade)) return;

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

    protected void SlingshotHandleGrab(RagdollHand hand, Handle handle, EventTime time) {
        if (time == EventTime.OnStart) {
            handle.physicBody.isKinematic = false;
        } else {
            OnHandleGrabEvent?.Invoke(this, handle, EventTime.OnStart);
        }
    }
    protected void SlingshotHandleUnGrab(RagdollHand hand, Handle handle, EventTime time) {
        if (time == EventTime.OnStart) {
            handle.physicBody.isKinematic = true;
        } else {
            OnHandleGrabEvent?.Invoke(this, handle, EventTime.OnEnd);
        }
    }

    public override void Unload() {
        base.Unload();

        if (spellCaster == null) return;
        // spellCaster.imbueTrigger.callBack = orgImbueTriggerCallback;
        if (!spellCaster.ragdollHand.ragdoll.creature.isPlayer) {
            spellCaster.ragdollHand.ragdoll.forcePhysic.Remove(this);
        }

        if (!slingshotHandle) return;
        Object.Destroy(slingshotHandle);
        slingshotHandle = null;
    }

    public bool FreeCharge {
        get => spellCaster
               && spellCaster.ragdollHand.creature.TryGetVariable(SkillCaputMortuum.FreeCharge,
                   out bool freeCharge)
               && freeCharge;
        set => spellCaster.ragdollHand.creature.SetVariable(SkillCaputMortuum.FreeCharge, value);
    }

    public override void Fire(bool active) {
        if (active && spellCaster.ragdollHand.grabbedHandle) return;
        base.Fire(active);

        if (imbueEnabled) {
            spellCaster.imbueTrigger.SetRadius(imbueRadius);
        } else {
            spellCaster.imbueTrigger.SetRadius(0);
        }
        
        spawning = false;

        if (active) {
            bool wasFreeCharge = FreeCharge;
            if (FreeCharge) {
                FreeCharge = false;
                AddModifier(this, Modifier.ChargeSpeed, 100);
            }

            if (quiver.Count == 0
                && (!Quiver.holsterWhenCrouched
                    || !quiver.creature.currentLocomotion.isCrouched
                    || !quiver.creature.currentLocomotion.isGrounded))
                quiver.FillFromHolsters();

            if (quiver.Count > 0)
                AddModifier(this, Modifier.ChargeSpeed, 20);
            lastThrownBlade?.StopGuidance();
            lastThrownBlade = null;
            readyHaptic = false;
            hasReachedFullCharge = false;
            if (stealIfNearby
                && Blade.TryGetClosestSlungInRadius(spellCaster.magicSource.position, SkillKnifethief.grabRadius,
                    out var closest, quiver)) {
                closest.item.StopFlying();
                closest.item.StopThrowing();
                currentCharge = 1;
                OnBladeSpawn(closest, false);
            } else {
                if (wasFreeCharge) {
                    Blade.Spawn(blade => OnBladeSpawn(blade, true), HandPosition, HandRotation, spellCaster.mana.creature);
                } else if (!quiver.IsEmpty) {
                    Blade.GetOrSpawn(OnBladeSpawn, HandPosition, HandRotation, spellCaster.mana.creature, !spellCaster.mana.creature.isPlayer);
                }
            }
        } else {
            root.Reset();
            OnCastStop();
        }
    }

    public void OnCastStop(bool skipQuiver = false) {
        if (slingshotHandle) {
            slingshotHandle.Release();
            slingshotHandle.SetTouch(false);
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
        if (slingshotHandle && !handleActive && handleEnabled && currentCharge > throwMinCharge) {
            slingshotHandle.SetTouch(true);
            handleActive = true;
        }

        if (slingshotHandle && !slingshotHandle.IsHanded())
            slingshotHandle.transform.position = spellCaster.magicSource.position;

        if (lastThrownBlade
            && lastThrownBlade.guided
            && spellCaster.ragdollHand.creature.isPlayer
            && spellCaster.ragdollHand.playerHand.controlHand.gripPressed != true) {
            lastThrownBlade.StopGuidance(true);
        }

        if (root.AtEnd()) root.Reset();

        bool firing = spellCaster.isFiring
                      && !spellCaster.isMerging
                      && !spellCaster.mana.mergeActive
                      && !spellCaster.ragdollHand.grabbedHandle;
        if (firing
            && !spawning
            && !activeBlade
            && (!quiver.IsEmpty || currentCharge == 1)) {
            spawning = Blade.GetOrSpawn(OnBladeSpawn, HandPosition, HandRotation, spellCaster.mana.creature,
                !spellCaster.mana.creature.isPlayer);
        }

        if (!firing) return;
        
        if (activeBlade) {
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
            if (currentCharge > throwMinCharge && activeBlade) {
                if (spellCaster.ragdollHand.creature.isPlayer
                    && spellCaster.ragdollHand.playerHand.controlHand.gripPressed
                    && !spellCaster.ragdollHand.grabbedHandle) {
                    var blade = activeBlade;
                    ReleaseBlade();
                    blade.item.StopFlying();
                    blade.item.StopThrowing();
                    var bladeHandle = blade.item.GetMainHandle(spellCaster.ragdollHand.side);
                    spellCaster.ragdollHand.Grab(bladeHandle,
                        bladeHandle.GetNearestOrientation(spellCaster.ragdollHand.grip, spellCaster.ragdollHand.side),
                        0,
                        true);
                    blade.RunAfter(
                        () => blade.item.IgnoreRagdollCollision(spellCaster.ragdollHand.ragdoll,
                            spellCaster.ragdollHand.type), 0.1f);
                    return;
                }

                if (readyHaptic && activeBlade) return;
                readyHaptic = true;
                spellCaster.ragdollHand.HapticTick();
            }
        } else if (!quiver.FullyEmpty) {
            spellCaster.ragdollHand.HapticTick(currentCharge * 0.3f);
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
                spellCaster.Fire(false);
            }
        }
    }

    // IMBUE
    public override void Load(Imbue imbue) {
        base.Load(imbue);
        Init();
    }

    public override bool OnImbueCollisionStart(CollisionInstance hit) {
        bool fired = base.OnImbueCollisionStart(hit);
        if (hit.targetColliderGroup?.collisionHandler?.ragdollPart is not RagdollPart part
            || part.ragdoll.creature.isPlayer
            || part.ragdoll.creature.isKilled) return false;

        Debug.Log("Inflicting bleeding");
        part.ragdoll.creature.Inflict("Bleeding", this, Mathf.Infinity, part);
        return fired;
    }

    public override void Throw(Vector3 velocity) {
        base.Throw(velocity);
        // velocity = spellCaster.ragdollHand.Velocity() + spellCaster.ragdollHand.creature.currentLocomotion.velocity;
        if (spellCaster.mana.creature.isPlayer) {
            if (Vector3.Angle(velocity.normalized, spellCaster.mana.creature.ragdoll.headPart.transform.forward) < 30)
                velocity = Vector3.Slerp(velocity.normalized,
                               spellCaster.mana.creature.ragdoll.headPart.transform.forward, 0.5f)
                           * velocity.magnitude;
            if (!activeBlade || !activeBlade.item) return;
            foreach (var eachImbue in activeBlade.item.imbues) {
                if (eachImbue.spellCastBase is not SpellCastGravity) continue;
                velocity *= gravityForceCompensation;
                break;
            }
        } else {
            velocity *= aiThrowForce;
        }

        if (!activeBlade) return;
        activeBlade.transform.position = HandPosition;
        activeBlade.transform.rotation = HandRotation;

        spellCaster.ragdollHand.playerHand?.controlHand.HapticPlayClip(Catalog.gameData.haptics.bowShoot);
        
        throwEffectData?.Spawn(spellCaster.magicSource).Play();

        activeBlade.OnlyIgnoreRagdoll(spellCaster.mana.creature.ragdoll, true);
        activeBlade.AddForce(velocity * throwForce, ForceMode.VelocityChange, aimAssist, true, spellCaster.ragdollHand);
        activeBlade.item.lastHandler = spellCaster.ragdollHand;
        activeBlade.wasSlung = true;
        Blade.slung.Add(activeBlade);
        lastThrownBlade = activeBlade;
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

    public override bool OnCrystalSlam(CollisionInstance hit) {
        bool crystalSlammed = (imbue.colliderGroup.imbueShoot.position - hit.contactPoint).sqrMagnitude < 0.25;
        float halfCount = slamBladeCount / 2f;
        var creature = spellCaster?.mana.creature
            ? spellCaster?.mana.creature 
            : imbue.imbueCreature 
                ? imbue.imbueCreature 
                : imbue.colliderGroup.collisionHandler.item.lastHandler?.creature;
        
        RagdollPart headPart = imbue.colliderGroup.collisionHandler.item.lastHandler?.creature?.ragdoll?.headPart;
        if (headPart && crystalSlammed) {
            var toHitPoint = hit.contactPoint - headPart.transform.position;
            for (int i = 0; i < slamBladeCount; i++) {
                float normalizedRange = (i - halfCount) / halfCount;
                Vector3 sideDir = Vector3.Cross(toHitPoint, hit.contactNormal);
                Blade.Spawn(blade => {
                        blade.Quiver = quiver;
                        blade.Release(true, 1f);
                        blade.item.lastHandler = headPart.ragdoll.creature.handLeft;
                        var velocity = (Quaternion.AngleAxis(normalizedRange * 30, hit.contactNormal)
                                        * toHitPoint.normalized
                                        + hit.contactNormal)
                                       * slamThrowForce;
                        blade.AddForce(velocity, ForceMode.VelocityChange);
                        quiver?.InvokeBladeThrow(blade);
                    },
                    headPart.transform.position + hit.contactNormal + sideDir * normalizedRange * 1f,
                    Quaternion.LookRotation(toHitPoint, Vector3.up), creature);
            }
        } else {
            var center = headPart ? headPart.transform.position + Vector3.up * 1f : hit.contactPoint + hit.contactNormal;
            var up = headPart ? Vector3.up : hit.contactNormal;
            for (int i = 0; i < slamBladeCount; i++) {
                var vector = Quaternion.AngleAxis(360f / slamBladeCount * i, up)
                             * Vector3.Cross(up, Vector3.forward).normalized;
                Blade.Spawn(blade => {
                    if (creature) {
                        blade.Quiver = quiver;
                        blade.item.lastHandler = imbue.colliderGroup.collisionHandler.item.lastHandler;
                    }
                    blade.AddForce(vector * slamThrowForce, ForceMode.VelocityChange);
                    blade.Release(true, 1f);
                }, center + vector * 0.5f, Quaternion.LookRotation(vector), creature);
            }
        }

        return true;
    }
}