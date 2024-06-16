using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Bladedancer.Skills;
using ThunderRoad;
using ThunderRoad.DebugViz;
using ThunderRoad.Skill.Spell;
using UnityEngine;
using UnityEngine.Serialization;

namespace Bladedancer; 

public class Blade : ThunderBehaviour {
    public Item item;
    public static ItemData itemData;
    public static List<Blade> all = new();
    public static List<Blade> slung = new();
    public static List<Blade> despawning = new();

    public delegate void HitEntityEvent(Blade blade, ThunderEntity entity, CollisionInstance hit);
    public event HitEntityEvent OnHitEntityEvent;
    public delegate void SlingEndEvent(Blade blade);
    public event SlingEndEvent OnSlingEndEvent;

    public delegate void PenetrationEvent(Blade blade, CollisionInstance hit, Damager damager);
    public event PenetrationEvent OnPenetrateEvent;
    public event PenetrationEvent OnUnPenetrateEvent;

    protected float freeStartTime;
    protected bool hasResetAfterFree;
    [FormerlySerializedAs("canDespawn")] public bool canFullDespawn = true;

    public override ManagedLoops EnabledManagedLoops => ManagedLoops.Update | ManagedLoops.FixedUpdate;
    
    private MoveTarget? moveTarget;

    public MoveTarget? MoveTarget {
        get => moveTarget;
        set {
            if (value == null) {
                freeStartTime = Time.time;
                hasResetAfterFree = false;
            }
            moveTarget = value;
        }
    }
    
    // PID move mode
    protected RBPID pid;
    protected bool pidActive;

    // Joint move mode
    protected Rigidbody jointTarget;
    protected Rigidbody customJointTarget;
    protected bool jointActive;
    protected Joint joint;
    protected bool intangible;
    protected bool autoDespawn;
    protected Coroutine despawnRoutine;
    public bool wasSlung;
    protected bool moveTargetDirty;

    public float throwTime;
    public Creature homingTarget;
    public Transform homingTargetPoint;
    public bool shouldRetrieve;
    public BoolHandler isDangerous;
    public const int UntilHit = 0;
    private Quiver _quiver;
    public Quiver lastQuiver;

    public HashSet<Ragdoll> ignoredRagdolls;
    
    public Bounds localBounds;
    public Axis largestAxis;
    public Axis smallestAxis;
    public float orgLargestAxisSize;
    public float orgMass;
    // public Vector3 orgCustomCenterOfMass;
    public float scaledMass;
    public float scaleRatio;
    
    // Guidance
    private Vector3 initialDirection;
    private bool guided;
    private RagdollHand guidanceHand;
    private float guidanceSpeed;
    private float guidanceDelay;
    private bool finishedScaling;
    private ScaleMode lastScaleMode;
    
    [SkillCategory("Debug", 1000000)]
    [ModOption("Show Movement Debug", "Draw lines between blade center and blade target position.")]
    public static bool showMoveDebug;

    private float retrieveDelay;

    public Quiver Quiver {
        get => _quiver;
        set {
            if (Quiver)
                lastQuiver = Quiver;
            if (value != null) {
                lastQuiver = value;
                OnSlingEndEvent?.Invoke(this);
                SetOwner(value);
            }
            _quiver = value;
        }
    }

    public Quiver OwningQuiver => Quiver ?? lastQuiver;

    public bool InQuiver => Quiver != null;
    public bool Dangerous => isDangerous || InQuiver && (Quiver?.isDangerous ?? false);

    public bool IsValid
        => item
           && item.transform
           && Player.local
           && (item.transform.position - Player.local.transform.position).sqrMagnitude < 500 * 500;
    
    public static void Spawn(
        Action<Blade, bool> callback,
        Vector3 position,
        Quaternion rotation,
        Creature creature,
        bool forceSpawn = false) {
        if (!forceSpawn
            && Quiver.TryGet(creature, out var quiver)
            && quiver.TryGetClosestBlade(position, out var quiverBlade)) {
            callback(quiverBlade, false);
            return;
        }

        itemData.SpawnAsync(item => {
            var blade = item.GetOrAddComponent<Blade>();
            item.transform.rotation
                = rotation * Quaternion.Inverse(item.transform.InverseTransformRotation(item.flyDirRef.rotation));
            callback(blade, true);
        }, position, rotation);
    }

    public static bool TryGetClosestSlungInRadius(Vector3 position, float radius, out Blade closest, Quiver ignoreQuiver = null) {
        float distance = radius * radius;
        closest = null;
        for (var i = 0; i < slung.Count; i++) {
            var blade = slung[i];
            if (ignoreQuiver != null && blade.lastQuiver == ignoreQuiver) continue;
            float bladeDistance = (blade.transform.position - position).sqrMagnitude;
            if (bladeDistance > radius) continue;
            closest = blade;
            radius = distance;
        }

        return closest;
    }

    public void Awake() {
        item = GetComponent<Item>();
        item.OnGrabEvent += OnGrab;
        item.OnUngrabEvent += OnUnGrab;
        item.OnFlyEndEvent += OnFlyEnd;
        item.OnDespawnEvent += OnDespawn;
        item.OnImbuesChangeEvent += OnImbuesChange;
        item.mainCollisionHandler.OnCollisionStartEvent += OnCollisionStart;
        item.DisallowDespawn = true;
        item.data.moduleAI = null;

        lastScaleMode = ScaleMode.FullSize;
        
        localBounds = item.GetLocalBounds();
        largestAxis = localBounds.size.x > localBounds.size.y ? Axis.X : Axis.Y;
        largestAxis = localBounds.size.GetAxis(largestAxis) > localBounds.size.z ? largestAxis : Axis.Z;

        orgLargestAxisSize = localBounds.size.GetAxis(largestAxis);
        scaleRatio = Mathf.Clamp01(SkillVersatility.targetWeaponSize / orgLargestAxisSize);
        orgMass = item.physicBody.mass;
        // orgCustomCenterOfMass = item.customCenterOfMass;
        scaledMass = item.physicBody.mass * scaleRatio;
        
        smallestAxis = localBounds.size.x < localBounds.size.y ? Axis.X : Axis.Y;
        smallestAxis = localBounds.size.GetAxis(smallestAxis) < localBounds.size.z ? smallestAxis : Axis.Z;
        
        all.Add(this);

        ignoredRagdolls = new HashSet<Ragdoll>();
        
        RegisterPenetrationEvents();
        
        isDangerous = new BoolHandler(false);
        jointTarget = new GameObject().AddComponent<Rigidbody>();
        jointTarget.isKinematic = true;
        shouldRetrieve = true;
        item.ForceLayer(LayerName.MovingItem);
        pid = new RBPID(item.physicBody.rigidBody, forceMode: SpellCastSlingblade.pidForceMode,
                maxForce: SpellCastSlingblade.pidMaxForce, anchor: item.GetLocalCenter())
            .Position(10, 0, 1)
            .Rotation(30, 0, 3);
    }

    private void OnImbuesChange() {
        SetMoltenArc(!InQuiver);
    }

    private void OnCollisionStart(CollisionInstance hit) {
        if ((hit.targetColliderGroup?.collisionHandler?.item as ThunderEntity
             ?? hit.targetColliderGroup?.collisionHandler?.ragdollPart?.ragdoll?.creature) is ThunderEntity entity) {
            OnHitEntityEvent?.Invoke(this, entity, hit);
        }
    }

    public void OnDespawn(EventTime time) {
        if (time != EventTime.OnStart) return;
        all.Remove(this);
        if (wasSlung)
            OnSlingEndEvent?.Invoke(this);
        Quiver?.ForceRemoveFromQuiver(this);
        Destroy(this);
    }

    public void OnDestroy() {
        all.Remove(this);
        if (jointTarget)
            Destroy(jointTarget.gameObject);
    }

    private void SetOwner(Quiver newQuiver) {
        OnlyIgnoreRagdoll(newQuiver.creature.ragdoll, true);
        for (var i = 0; i < item.imbues.Count; i++) {
            var imbue = item.imbues[i];
            if (newQuiver.creature.HasSkill(imbue.spellCastBase)) {
                imbue.TempUnloadSpell();
                imbue.TempReloadSpell(newQuiver.creature.handLeft.caster);
            }

            imbue.imbueCreature = newQuiver.creature;
        }
    }

    public void ScaleInstantly(ScaleMode mode) {
        lastScaleMode = mode;
        transform.localScale = mode switch {
            ScaleMode.Scaled => Vector3.one * scaleRatio,
            ScaleMode.FullSize => Vector3.one,
            _ => transform.localScale
        };

        item.physicBody.mass = mode == ScaleMode.Scaled ? scaledMass : orgMass;
        item.ResetCenterOfMass();
        moveTargetDirty = true;
    }

    public void SetMoltenArc(bool active) {
        for (var i = 0; i < item.imbues.Count; i++) {
            var imbue = item.imbues[i];
            if (item.imbues[i].spellCastBase is not { hashId: var id }
                || id != SkillFiresGuidance.spellHashId) continue;
            if (!imbue.TryGetComponent<SkillMoltenArc.MoltenArc>(out var moltenArc)
                || moltenArc.effect is not { isPlaying: true }) continue;
            for (var j = 0; j < moltenArc.effect.effects.Count; j++) {
                if (moltenArc.effect.effects[i] is not EffectParticle particle) continue;
                if (active)
                    particle.Play();
                else
                    particle.Stop();
            }
        }
    }

    public bool IsFree => !item.IsHeld() && item.holder == null && !item.isGripped && !item.isTelekinesisGrabbed;

    public void IgnoreRagdoll(Ragdoll ragdoll, bool ignore = true, bool force = false) {
        if (ragdoll == null) return;
        var current = item.ignoredRagdoll;
        switch (ignore) {
            case true when !ignoredRagdolls.Contains(ragdoll) || force:
                item.ignoredRagdoll = null;
                item.IgnoreRagdollCollision(ragdoll);
                item.ignoredRagdoll = current;
                ignoredRagdolls.Add(ragdoll);
                break;
            case false when ignoredRagdolls.Contains(ragdoll) || force:
                item.ignoredRagdoll = ragdoll;
                item.ResetRagdollCollision();
                ignoredRagdolls.Remove(ragdoll);
                if (current != ragdoll)
                    item.ignoredRagdoll = current;
                break;
        }
    }

    public void OnlyIgnoreRagdoll(Ragdoll ragdollToIgnore, bool force = false) {
        item.ResetRagdollCollision();
        IgnoreRagdoll(ragdollToIgnore, true, force);
        var ragdolls = ignoredRagdolls.ToList();
        foreach (var ragdoll in ragdolls) {
            if (ragdoll && ragdoll != ragdollToIgnore)
                IgnoreRagdoll(ragdoll, false, force);
        }
    }

    public void ResetIgnoredRagdolls(bool force = false) {
        item.ResetRagdollCollision();
        var ragdolls = ignoredRagdolls.ToList();
        foreach (var ragdoll in ragdolls) {
            IgnoreRagdoll(ragdoll, false);
        }
    }
    
    public void RegisterPenetrationEvents() {
        for (var i = 0; i < item.mainCollisionHandler.damagers.Count; i++) {
            var damager = item.mainCollisionHandler.damagers[i];
            if (damager.type is Damager.Type.Blunt) continue;
            damager.OnPenetrateEvent -= OnPenetrate;
            damager.OnPenetrateEvent += OnPenetrate;
            damager.OnUnPenetrateEvent -= OnUnPenetrate;
            damager.OnUnPenetrateEvent += OnUnPenetrate;
        }
    }
    
    private void OnPenetrate(Damager damager, CollisionInstance collision) {
        OnPenetrateEvent?.Invoke(this, collision, damager);
    }

    private void OnUnPenetrate(Damager damager, CollisionInstance collision) {
        OnUnPenetrateEvent?.Invoke(this, collision, damager);
    }

    public void OnFlyEnd(Item _) {
        homingTarget = null;
        homingTargetPoint = null;
        Debug.Log("OnFlyEnd StartDespawn");
        StartDespawn();
    }

    public void OnGrab(Handle handle, RagdollHand hand) {
        StopDespawn();
    }

    public void OnUnGrab(Handle handle, RagdollHand hand, bool throwing) {
        if (throwing) return;
        Release();
    }

    public void StartDespawn() {
        if (!autoDespawn || !this) return;
        try {
            if (wasSlung)
                StartCoroutine(UnSling());
            if (despawnRoutine != null) StopCoroutine(despawnRoutine);
            if (IsFree) despawnRoutine = StartCoroutine(DespawnRoutine());
        } catch (NullReferenceException e) {
            Debug.LogWarning("Something weird happened with OnFlyEnd...");
            Debug.LogException(e);
        }
    }
    public void StopDespawn() {
        despawning.Remove(this);
        if (despawnRoutine != null && this != null) StopCoroutine(despawnRoutine);
    }

    private IEnumerator UnSling() {
        if (!wasSlung) yield break;
        isDangerous.Remove(UntilHit);
        yield return 0;
        OnSlingEndEvent?.Invoke(this);
        wasSlung = false;
    }

    public IEnumerator DespawnRoutine() {
        despawning.Add(this);
        Debug.Log(retrieveDelay);
        yield return new WaitForSeconds(retrieveDelay == -1 ? SpellCastSlingblade.collectTime : retrieveDelay);
        retrieveDelay = -1;
        despawning.Remove(this);
        if (IsFree) DespawnOrReturn(Quiver ? Quiver : lastQuiver);
    }

    public void AllowDespawn(bool enabled, float retrieveDelay = -1) {
        if (enabled == autoDespawn) return;
        Debug.Log($"AllowDespawn {enabled} with {retrieveDelay}");
        this.retrieveDelay = retrieveDelay;
        autoDespawn = enabled;
        if (!autoDespawn) StopDespawn();
    }

    public void ReturnToQuiver(Quiver quiver, bool randomIndex = false, bool force = false) {
        transform.localScale = Vector3.one;
        StartCoroutine(UnSling());
        item.StopThrowing();
        item.StopFlying();
        item.lastHandler = quiver.creature.handRight;
        if ((shouldRetrieve || force)
            && quiver
            && quiver.creature
            && !quiver.creature.isKilled
            && quiver.AddToQuiver(this, randomIndex)) return;
        Release();
    }
    
    public void IgnoreItem(Item other, bool ignore = true) {
        if (ignore) {
            var ignored = item.ignoredItem;
            item.IgnoreItemCollision(other);
            item.ignoredItem = ignored;
        } else {
            var ignored = item.ignoredItem;
            item.ignoredItem = item;
            item.ResetObjectCollision();
            item.ignoredItem = ignored;
        }
    }

    public void IgnoreBlade(Blade other, bool ignore = true) {
        IgnoreItem(other.item, ignore);
    }
    public void IgnoreBlades(List<Blade> blades, bool ignore = true) {
        foreach (var blade in blades) IgnoreBlade(blade, ignore);
    }

    public void DespawnOrReturn(Quiver quiver, bool randomIndex = false) {
        if (shouldRetrieve
            && quiver
            && quiver.AddToQuiver(this, randomIndex)) return;

        // Couldn't add to quiver, despawn this blade
        if (canFullDespawn) Despawn();
        else {
            Debug.Log("DespawnOrReturn StartDespawn");
            StartDespawn();
        }
    }

    public void Despawn() {
        Quiver?.RemoveFromQuiver(this);
        CancelMovement(true);
        all.Remove(this);
        slung.Remove(this);
        despawning.Remove(this);
        if (!item) return;
        foreach (var handle in item.handles) {
            handle.Release();
            handle.ReleaseAllTkHandlers();
        }
        item.Despawn();
    }

    protected override void ManagedUpdate() {
        base.ManagedUpdate();
        if (showMoveDebug && moveTarget is MoveTarget target) {
            Viz.Lines(this).Color(target.mode switch {
                MoveMode.Joint => Color.red,
                MoveMode.PID => Color.blue,
                MoveMode.Lerp => Color.green,
                MoveMode.Teleport => Color.white,
            }).SetPoints(item.Center, target.Get.position).Show();
        } else {
            Viz.Lines(this).Hide();
        }
        if (MoveTarget != null || hasResetAfterFree || !(Time.time - freeStartTime > 1f)) return;
        ResetIgnoredRagdolls();
        hasResetAfterFree = true;
    }

    protected override void ManagedFixedUpdate() {
        base.ManagedFixedUpdate();
        Move();

        if (InQuiver)
            SetIntangible(MoveTarget is MoveTarget target
                          && (transform.position - target.Get.position).sqrMagnitude
                          > SpellCastSlingblade.intangibleThreshold * SpellCastSlingblade.intangibleThreshold);

        if (MoveTarget == null && homingTarget != null) {
            var rotationToFaceTarget = Quaternion.FromToRotation(item.physicBody.velocity,
                (homingTargetPoint ? homingTargetPoint : homingTarget.ragdoll.targetPart.transform).position
                - item.transform.position); 
            item.physicBody.velocity = Quaternion.Slerp(Quaternion.identity, rotationToFaceTarget,
                                           Time.deltaTime
                                           * 30f
                                           * Mathf.Clamp01((Time.time - throwTime) / 0.5f))
                                       * item.physicBody.velocity.normalized
                                       * 20;
        }

        if (MoveTarget == null && guided && guidanceHand != null && guidanceSpeed > 0) {
            var direction = (guidanceHand.caster.rayDir.position
                             - (guidanceHand.upperArmPart.transform.position
                                + new Vector3(0.0f,
                                    guidanceHand.creature.morphology.armsToEyesHeight
                                    * guidanceHand.creature.transform.localScale.y, 0.0f))).normalized;

            item.physicBody.velocity = Time.time - throwTime > guidanceDelay
                ? direction * guidanceSpeed
                : Vector3.Lerp(initialDirection * guidanceSpeed, direction * guidanceSpeed,
                    (Time.time - throwTime) / guidanceDelay);
        }
    }

    public void MoveTo(MoveTarget? target) {
        MoveTarget = target;
        if (moveTarget is MoveTarget newTarget && newTarget.scale != lastScaleMode) finishedScaling = false;
        item.data.drainImbueWhenIdle = false;
    }

    /// <summary>
    /// Cancels any controlled movement of the blade, but does not start the despawn timer.
    /// </summary>
    /// <param name="now">Immediately stop all joints and movement, versus doing so at the next update loop.</param>
    public void CancelMovement(bool now = false) {
        MoveTarget = null;
        if (now) Move();
    }

    /// <summary>
    /// Force this blade to refresh its MoveTarget
    /// </summary>
    /// <remarks>
    /// This forces joints to be remade (helpful if you change the joint parameters), and resets PID movement.
    /// </remarks>
    public void ForceRefreshMoveTarget() {
        moveTargetDirty = true;
    }

    /// <summary>
    /// Stop blade guidance
    /// </summary>
    public void StopGuidance() {
        guided = false;
        guidanceSpeed = 0;
        guidanceHand = null;
    }

    /// <summary>
    /// Starts guidance of the blade (like fireballs)
    /// </summary>
    /// <param name="hand">Hand that controls the blade</param>
    /// <param name="speed">Blade flight speed</param>
    public void StartGuidance(RagdollHand hand, float speed, float delay) {
        initialDirection = item.physicBody.velocity.normalized;
        guided = true;
        guidanceSpeed = speed;
        guidanceDelay = delay;
        guidanceHand = hand;
    }
    
    /// <summary>
    /// Stop all controlled movement of the blade and start the despawn timer
    /// </summary>
    /// <remarks>
    /// When the timer runs out, the blade will attempt to return to its last quiver, or otherwise despawn.
    /// </remarks>
    /// <param name="allowRetrieve">Whether the blade should attempt to return to its quiver after the timer has run out</param>
    public void Release(bool allowRetrieve = true, float retrieveDelay = -1) {
        CancelMovement();
        shouldRetrieve = allowRetrieve;
        item.data.drainImbueWhenIdle = true;
        Debug.Log($"Released with {retrieveDelay}");
        AllowDespawn(true, retrieveDelay);
        SetTouch(true);
        item.Throw();
        item.lastHandler ??= Player.currentCreature.handLeft;
    }

    public void SetTouch(bool enabled) {
        foreach (var handle in item.handles) {
            handle.SetTouch(enabled);
            handle.SetTelekinesis(enabled);
        }
    }

    public void SetIntangible(bool active) {
        if (intangible == active) return;
        intangible = active;
        item.SetColliders(!intangible);
    }

    public void Move() {
        if (MoveTarget is not { Get: (position: var position, rotation: var rotation) } target) {
            SetJoint(false);
            SetIntangible(false);
            item.RemovePhysicModifier(this);
            pidActive = false;
            return;
        }

        if (intangible && (transform.position - position).sqrMagnitude < SpellCastSlingblade.intangibleThreshold)
            SetIntangible(false);
        item.SetPhysicModifier(this, 0);

        if (moveTargetDirty && moveTarget is MoveTarget newTarget && newTarget.scale != lastScaleMode) {
            finishedScaling = false;
            lastScaleMode = newTarget.scale;
        }

        if (!finishedScaling) {
            switch (target.scale) {
                case ScaleMode.Scaled:
                    if (transform.localScale.x.IsApproximately(scaleRatio)) {
                        item.ResetCenterOfMass();
                        moveTargetDirty = true;
                        finishedScaling = true;
                        break;
                    }

                    if (target.scaleInstantly) {
                        transform.localScale = Vector3.one * scaleRatio;
                        item.physicBody.mass = scaledMass;
                        // item.customCenterOfMass = orgCustomCenterOfMass * scaleRatio;
                    } else {
                        transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * scaleRatio,
                            Time.unscaledDeltaTime * SpellCastSlingblade.scaleSpeed);
                        item.physicBody.mass = Mathf.Lerp(item.physicBody.mass, scaledMass,
                            Time.unscaledDeltaTime * SpellCastSlingblade.scaleSpeed);
                        // item.customCenterOfMass = orgCustomCenterOfMass * scaleRatio;
                    }

                    break;
                case ScaleMode.FullSize:
                    if (transform.localScale.x.IsApproximately(1)) {
                        item.ResetCenterOfMass();
                        moveTargetDirty = true;
                        finishedScaling = true;
                        break;
                    }

                    if (target.scaleInstantly) {
                        transform.localScale = Vector3.one;
                        item.physicBody.mass = orgMass;
                        // item.customCenterOfMass = orgCustomCenterOfMass;
                    } else {
                        transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one,
                            Time.unscaledDeltaTime * SpellCastSlingblade.scaleSpeed);
                        item.physicBody.mass = Mathf.Lerp(item.physicBody.mass, orgMass,
                            Time.unscaledDeltaTime * SpellCastSlingblade.scaleSpeed);
                        // item.customCenterOfMass = orgCustomCenterOfMass;
                    }

                    item.ResetCenterOfMass();
                    moveTargetDirty = true;
                    break;
            }
        }

        SetJoint(target.mode == MoveMode.Joint, moveTargetDirty);
        if (target.mode == MoveMode.PID && (!pidActive || moveTargetDirty)) {
            pid.Reset();
            pidActive = true;
        }
        
        moveTargetDirty = false;
        switch (target.mode) {
            case MoveMode.Joint:
                if (target.speed == 0) {
                    jointTarget.transform.SetPositionAndRotation(position, rotation);
                } else {
                    jointTarget.transform.SetPositionAndRotation(
                        Vector3.Lerp(jointTarget.transform.position, position, Time.unscaledDeltaTime * target.speed),
                        Quaternion.Slerp(jointTarget.transform.rotation, rotation,
                            Time.unscaledDeltaTime * target.speed));
                }
                break;
            case MoveMode.Teleport:
                transform.SetPositionAndRotation(position, rotation);
                break;
            case MoveMode.Lerp:
                transform.SetPositionAndRotation(
                    Vector3.Lerp(transform.position, position, Time.unscaledDeltaTime * target.speed),
                    Quaternion.Slerp(transform.rotation, rotation, Time.unscaledDeltaTime * target.speed));
                break;
            case MoveMode.Homing:
                var rotationToFaceTarget = Quaternion.FromToRotation(item.physicBody.velocity,
                    -item.transform.position);
                item.physicBody.velocity = Quaternion.Slerp(Quaternion.identity, rotationToFaceTarget,
                                               Time.deltaTime
                                               * 30f
                                               * Mathf.Clamp01((Time.time - throwTime) / 0.5f))
                                           * item.physicBody.velocity.normalized
                                           * target.speed;
                break;
            case MoveMode.PID:
                // pid.Update(position, rotation * Quaternion.Inverse(item.transform.InverseTransformRotation(item.flyDirRef.rotation)),
                //     target.speed);

                pid.Update(position,
                    rotation
                    * Quaternion.Inverse(item.transform.InverseTransformRotation(
                        Quaternion.LookRotation(item.flyDirRef.forward, item.transform.GetUnitAxis(smallestAxis)))),
                    target.speed);
                break;
        }
    }

    public Vector3 ToTarget => moveTarget?.Get.position is Vector3 pos ? pos - item.Center : Vector3.zero;
    public float DistanceToTarget => ToTarget.magnitude;

    public void SetJoint(bool enabled, bool force = false) {
        if (jointActive
            && (moveTargetDirty
                || (enabled
                    && ((moveTarget is MoveTarget target && target.jointBody != customJointTarget)
                        || (customJointTarget != null && moveTarget?.jointBody == null))))) {
            // Switch joint to or from the MoveTarget's custom body
            DestroyJoint();
            jointActive = false;
        }

        if (jointActive == enabled) return;
        if (enabled)
            CreateJoint();
        else
            DestroyJoint();
        jointActive = enabled;
    }

    public void CreateJoint() {
        var targetBody = moveTarget?.jointBody ?? jointTarget;
        
        var target = moveTarget.GetValueOrDefault();
        bool isCustomJoint = moveTarget != null && target.jointBody;
        customJointTarget = moveTarget?.jointBody;

        Vector3 savedPosition;
        Quaternion savedRotation;
        if (isCustomJoint) {
            savedPosition = item.transform.position;
            savedRotation = item.transform.rotation;

            var (pos, rot) = target.Get;
            item.transform.rotation = rot;
            item.transform.position = pos + (item.transform.position - item.Center);
        } else {
            savedPosition = targetBody.transform.position;
            savedRotation = targetBody.transform.rotation;
            targetBody.transform.SetPositionAndRotation(item.Center, item.transform.rotation);
        }

        // to the lyneca of tomorrow: good luck understanding this
        if (item.flyDirRef) {
            var lookDir = Quaternion.LookRotation(item.flyDirRef.forward, item.transform.GetUnitAxis(smallestAxis));
            var inverseLocalFlyDirRef = Quaternion.Inverse(item.transform.InverseTransformRotation(lookDir));
            if (isCustomJoint)
                item.transform.rotation *= inverseLocalFlyDirRef;
            else
                targetBody.transform.rotation = lookDir;
        } else if (item.holderPoint) {
            if (isCustomJoint)
                item.transform.rotation
                    *= Quaternion.Inverse(item.transform.InverseTransformRotation(item.holderPoint.rotation
                        * Quaternion.AngleAxis(180, Vector3.up)));
            else
                targetBody.transform.rotation = item.holderPoint.rotation * Quaternion.AngleAxis(180, Vector3.up);
        } else {
            if (!isCustomJoint)
                targetBody.transform.rotation = Quaternion.LookRotation(item.transform.up, item.transform.forward);
        }

        if (!isCustomJoint || target.jointType is JointType.Config) {
            var configJoint = targetBody.gameObject.AddComponent<ConfigurableJoint>();
            configJoint.targetRotation = Quaternion.identity;
            configJoint.rotationDriveMode = RotationDriveMode.Slerp;
            configJoint.xMotion = configJoint.yMotion = configJoint.zMotion = ConfigurableJointMotion.Free;

            configJoint.xDrive = configJoint.yDrive = configJoint.zDrive = configJoint.slerpDrive = new JointDrive {
                positionSpring = moveTarget?.jointSpring ?? SpellCastSlingblade.jointSpring,
                positionDamper = moveTarget?.jointDamper ?? SpellCastSlingblade.jointDamper,
                maximumForce = moveTarget?.jointMaxForce ?? SpellCastSlingblade.jointMaxForce
            };
            joint = configJoint;
        } else {
            switch (target.jointType) {
                // case JointType.Fixed:
                //     var fixedJoint = targetBody.gameObject.AddComponent<FixedJoint>();
                //     joint = fixedJoint;
                //     break;
                case JointType.Spring:
                    var springJoint = targetBody.gameObject.AddComponent<SpringJoint>();
                    springJoint.spring = moveTarget?.jointSpring ?? SpellCastSlingblade.jointSpring;
                    springJoint.damper = moveTarget?.jointDamper ?? SpellCastSlingblade.jointDamper;
                    joint = springJoint;
                    break;
            }
        }

        joint.autoConfigureConnectedAnchor = false;
        joint.connectedBody = item.physicBody.rigidBody;
        joint.connectedAnchor = item.GetLocalCenter();

        if (isCustomJoint) {
            joint.anchor = target.jointBody.transform.InverseTransformPoint(target.Get.position);
        } else {
            joint.anchor = Vector3.zero;
        }

        float massScale = moveTarget?.jointMassScale ?? SpellCastSlingblade.jointMassScale;
        joint.massScale = 1 / massScale;
        joint.connectedMassScale = massScale;
        
        if (isCustomJoint) {
            item.transform.SetPositionAndRotation(savedPosition, savedRotation);
        }
    }
    public void DestroyJoint() {
        Destroy(joint);
        customJointTarget = null;
    }

    public void AddForce(Vector3 force, ForceMode forceMode, bool aimAssist = false, bool resetVelocity = false, RagdollHand hand = null) {
        throwTime = Time.time;
        if (resetVelocity) {
            item.physicBody.velocity = Vector3.zero;
        }

        var position = hand?.caster.magicSource.position ?? transform.position;

        if (aimAssist) {
            var ray = new Ray(position, force);
            var target = Creature.AimAssist(ray, 20, 30, out var targetPoint, Filter.EnemyOf(item.lastHandler?.creature), CreatureType.Animal | CreatureType.Golem | CreatureType.Human) as Creature;
            if (target != null) {
                // hack to enable extra weakpoints
                var oldList = target.weakpoints;
                target.weakpoints = new List<Transform> {
                    target.ragdoll.headPart.transform,
                    target.handLeft.transform,
                    target.handRight.transform,
                    target.footLeft.transform,
                    target.footRight.transform,
                    target.footLeft.upperLegBone.transform,
                    target.footRight.upperLegBone.transform
                };
                if (Creature.AimAssist(ray, 20, 30, out targetPoint, Filter.EnemyOf(item.lastHandler?.creature),
                        CreatureType.Animal | CreatureType.Golem | CreatureType.Human)) {
                    force = (targetPoint.position - transform.position).normalized * force.magnitude;
                }
                target.weakpoints = oldList;
            }
        }
        item.AddForce(force, ForceMode.VelocityChange);
        item.Throw(1, Item.FlyDetection.Forced);
    }

    public void HomeTo(Creature creature, Transform point) {
        homingTarget = creature;
        homingTargetPoint = point;
    }

    public bool ImbuedWith(string spellId)
        => !string.IsNullOrEmpty(spellId) && ImbuedWith(Animator.StringToHash(spellId.ToLower()));

    public bool ImbuedWith(int spellHashId) {
        if (item.imbues == null) return false;
        for (var i = 0; i < item.imbues.Count; i++) {
            if (item.imbues[i].spellCastBase is SpellCastCharge spell && spell.hashId == spellHashId) return true;
        }

        return false;
    }

    public void MaxImbue(SpellCastCharge spell, Creature creature) {
        if (item.colliderGroups.Count == 0) return;
        for (var i = 0; i < item.colliderGroups.Count; i++) {
            var imbue = item.colliderGroups[i].imbue;
            if (imbue == null) continue;
            if (imbue.spellCastBase is SpellCastCharge currentSpell
                && currentSpell.hashId != spell.hashId) {
                imbue.SetEnergyInstant(0);
            }

            imbue.Transfer(spell, imbue.maxEnergy, creature);
        }
    }
}

public enum MoveMode {
    PID,
    Joint,
    Homing,
    Lerp,
    Teleport
}

public struct MoveTarget {
    public Transform parent;
    private Transform lookTarget;
    private Vector3? lookUpDir = null;
    private bool lookAway;
    private Vector3 position;
    private Quaternion rotation;
    public MoveMode mode;
    private bool localRotation = true;
    public float speed;
    private Func<(Vector3 position, Quaternion rotation)> func;
    public ScaleMode scale = ScaleMode.FullSize;
    public bool scaleInstantly = false;
    
    public Rigidbody jointBody;
    public JointType? jointType = null;
    public float? jointMassScale = null;
    public float? jointSpring = null;
    public float? jointDamper = null;
    public float? jointMaxForce = null;

    public MoveTarget(MoveMode mode, float speed = -1) {
        parent = null;
        this.speed = speed;
        if (speed == -1) {
            this.speed = mode switch {
                MoveMode.Lerp => 20,
                MoveMode.Joint => 0,
                _ => 1f
            };
        }
        this.mode = mode;
    }
    
    public MoveTarget LookAt(Transform lookTarget, bool lookAway = false, Vector3? lookUpDir = null) {
        this.lookTarget = lookTarget;
        this.lookUpDir = lookUpDir;
        this.lookAway = lookAway;
        return this;
    }

    public MoveTarget At(Func<(Vector3 position, Quaternion rotation)> func) {
        this.func = func;
        return this;
    }

    public MoveTarget Parent(Transform parent, bool localRotation = true) {
        this.localRotation = localRotation;
        this.parent = parent;
        return this;
    }

    public MoveTarget At(Vector3 position, Quaternion? rotation = null) {
        this.position = position;
        this.rotation = rotation ?? Quaternion.identity;
        return this;
    }

    public MoveTarget AtWorld(Vector3 position, Quaternion? rotation = null) {
        if (parent == null) return At(position, rotation);
        this.position = parent.InverseTransformPoint(position);
        this.rotation = parent.InverseTransformRotation(rotation ?? Quaternion.identity);
        return this;
    }

    public MoveTarget Scale(ScaleMode scaleMode, bool instantly = false) {
        scale = scaleMode;
        scaleInstantly = instantly;
        return this;
    }

    public MoveTarget JointTo(
        PhysicBody body,
        JointType? type = JointType.Config,
        float? massScale = null,
        float? spring = null,
        float? damper = null,
        float? maxForce = null) => JointTo(body.rigidBody, type, massScale, spring, damper, maxForce);

    public MoveTarget JointTo(
        Rigidbody body,
        JointType? type = JointType.Config,
        float? massScale = null,
        float? spring = null,
        float? damper = null,
        float? maxForce = null) {
        jointBody = body;
        jointType = type;
        jointMassScale = massScale;
        jointSpring = spring;
        jointDamper = damper;
        jointMaxForce = maxForce;
        return this;
    }

    public (Vector3 position, Quaternion rotation) Get {
        get {
            (Vector3? pos, Quaternion? rot) = func?.Invoke() ?? (Position, Rotation);
            return (pos ?? Position, rot ?? Rotation);
        }
    }

    private Vector3 Position => parent != null
        ? localRotation ? parent.TransformPoint(position) : parent.transform.position + position
        : position;

    private Vector3 LookUpDir => lookUpDir is Vector3 dir
        ? parent != null && localRotation ? parent.transform.TransformDirection(dir) : dir
        : Vector3.up;
    private Quaternion Rotation => lookTarget
        ?
        lookAway
            ? Quaternion.LookRotation(Position - lookTarget.position, LookUpDir)
            : Quaternion.LookRotation(lookTarget.position - Position, LookUpDir)
        : parent != null && localRotation
            ? parent.transform.rotation * rotation
            : rotation;
}

public enum JointType {
    Config,
    Spring
}

public enum ScaleMode {
    FullSize,
    Scaled
}