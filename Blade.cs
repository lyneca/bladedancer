using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ThunderRoad;
using ThunderRoad.DebugViz;
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

    protected float freeStartTime;
    protected bool hasResetAfterFree;

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
    protected bool jointActive;
    protected ConfigurableJoint joint;
    protected bool intangible;
    protected bool autoDespawn;
    protected Coroutine despawnRoutine;
    public bool wasSlung;

    public float throwTime;
    public Creature homingTarget;
    public bool shouldRetrieve;
    public BoolHandler isDangerous;
    public const int UntilHit = 0;
    protected Quiver _quiver;
    public Quiver lastQuiver;

    public HashSet<Ragdoll> ignoredRagdolls;

    public Quiver quiver {
        get => _quiver;
        set {
            if (quiver)
                lastQuiver = quiver;
            if (value != null)
                lastQuiver = value;
            _quiver = value;
        }
    }

    public Quiver OwningQuiver => quiver ?? lastQuiver;

    public bool InQuiver => quiver != null;
    public bool Dangerous => isDangerous || InQuiver && (quiver?.isDangerous ?? false);

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

    
    public void Awake() {
        item = GetComponent<Item>();
        item.OnGrabEvent -= OnGrab;
        item.OnGrabEvent += OnGrab;
        item.OnUngrabEvent -= OnUnGrab;
        item.OnUngrabEvent += OnUnGrab;
        item.OnFlyEndEvent -= OnFlyEnd;
        item.OnFlyEndEvent += OnFlyEnd;
        item.OnDespawnEvent -= OnDespawn;
        item.OnDespawnEvent += OnDespawn;
        item.mainCollisionHandler.OnCollisionStartEvent += OnCollisionStart;
        item.DisallowDespawn = true;
        item.data.moduleAI = null;
        all.Add(this);

        ignoredRagdolls = new HashSet<Ragdoll>();
        
        isDangerous = new BoolHandler(false);
        jointTarget = new GameObject().AddComponent<Rigidbody>();
        jointTarget.isKinematic = true;
        shouldRetrieve = true;
        item.ForceLayer(LayerName.MovingItem);
        pid = new RBPID(item.physicBody.rigidBody, forceMode: SpellCastSlingblade.pidForceMode, maxForce: SpellCastSlingblade.pidMaxForce)
            .Position(10, 0, 1)
            .Rotation(30, 0, 3);
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
        quiver?.ForceRemoveFromQuiver(this);
        Destroy(this);
    }

    public void OnDestroy() {
        all.Remove(this);
        if (jointTarget)
            Destroy(jointTarget.gameObject);
    }

    public bool IsFree => !item.IsHeld() && item.holder == null && !item.isGripped && !item.isTelekinesisGrabbed;

    public void IgnoreRagdoll(Ragdoll ragdoll, bool ignore = true, bool force = false) {
        var current = item.ignoredRagdoll;
        if (ignore && (ignoredRagdolls.Add(ragdoll) || force)) {
            item.IgnoreRagdollCollision(ragdoll);
            item.ignoredRagdoll = current;
        } else if (ignoredRagdolls.Remove(ragdoll) || force) {
            item.ignoredRagdoll = ragdoll;
            item.ResetRagdollCollision();
            if (current != ragdoll)
                item.ignoredRagdoll = current;
        }
    }

    public void OnlyIgnoreRagdoll(Ragdoll ragdoll, bool force = false) {
        item.ResetRagdollCollision();
        IgnoreRagdoll(ragdoll, true, true);
        var ragdolls = ignoredRagdolls.ToList();
        foreach (var each in ragdolls) {
            if (each != ragdoll)
                IgnoreRagdoll(ragdoll, false);
        }
    }

    public void ResetIgnoredRagdolls(bool force = false) {
        item.ResetRagdollCollision();
        var ragdolls = ignoredRagdolls.ToList();
        foreach (var ragdoll in ragdolls) {
            IgnoreRagdoll(ragdoll, false);
        }
    }

    public void OnFlyEnd(Item _) {
        if (!autoDespawn || !this) return;
        homingTarget = null;
        try {
            StartCoroutine(UnSling());
            if (despawnRoutine != null) StopCoroutine(despawnRoutine);
            if (IsFree) despawnRoutine = StartCoroutine(DespawnRoutine());
        } catch (NullReferenceException e) {
            Debug.LogWarning("Something weird happened with OnFlyEnd...");
            Debug.LogException(e);
        }
    }

    public void OnGrab(Handle handle, RagdollHand hand) {
        StopDespawn();
    }

    public void OnUnGrab(Handle handle, RagdollHand hand, bool throwing) {
        if (throwing) return;
        Release();
    }

    public void StopDespawn() {
        despawning.Remove(this);
        if (despawnRoutine != null && this != null) StopCoroutine(despawnRoutine);
    }

    private IEnumerator UnSling() {
        isDangerous.Remove(UntilHit);
        if (!wasSlung) yield break;
        yield return 0;
        OnSlingEndEvent?.Invoke(this);
        wasSlung = false;
    }

    public IEnumerator DespawnRoutine() {
        despawning.Add(this);
        yield return new WaitForSeconds(SpellCastSlingblade.collectTime);
        despawning.Remove(this);
        if (IsFree) DespawnOrReturn(quiver ?? lastQuiver);
    }

    public void AllowDespawn(bool enabled) {
        if (enabled == autoDespawn) return;
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
        Despawn();
    }

    public void Despawn() {
        quiver?.RemoveFromQuiver(this);
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
        if (MoveTarget == null && !hasResetAfterFree && Time.time - freeStartTime > 1f) {
            ResetIgnoredRagdolls();
            hasResetAfterFree = true;
        }
    }

    protected override void ManagedFixedUpdate() {
        base.ManagedFixedUpdate();
        Move();

        if (InQuiver)
            SetIntangible(MoveTarget is MoveTarget target
                          && (transform.position - target.Get.position).sqrMagnitude
                          > SpellCastSlingblade.intangibleThreshold * SpellCastSlingblade.intangibleThreshold);

        if (MoveTarget == null && homingTarget != null) {
            item.physicBody.velocity = Quaternion.Slerp(Quaternion.identity,
                                           Quaternion.FromToRotation(item.physicBody.velocity,
                                               homingTarget.ragdoll.targetPart.transform.position
                                               - item.transform.position),
                                           Time.deltaTime
                                           * 30f
                                           * Mathf.Clamp01((Time.time - throwTime) / 0.5f))
                                       * item.physicBody.velocity.normalized
                                       * 20;
        }
    }

    public void MoveTo(MoveTarget? target, MoveTarget? alt = default) {
        MoveTarget = target;
        item.data.drainImbueWhenIdle = false;
    }

    public void CancelMovement() {
        MoveTarget = null;
    }
    
    public void Release(bool allowRetrieve = true) {
        MoveTarget = null;
        shouldRetrieve = allowRetrieve;
        item.data.drainImbueWhenIdle = true;
        AllowDespawn(true);
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
        SetJoint(target.mode == MoveMode.Joint);
        if (target.mode == MoveMode.PID && !pidActive) {
            pid.Reset();
            pidActive = true;
        }

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
            case MoveMode.PID:
                pid.Update(position, rotation * Quaternion.Inverse(item.transform.InverseTransformRotation(item.flyDirRef.rotation)),
                    target.speed);
                break;
        }
    }

    public void SetJoint(bool enabled) {
        if (jointActive == enabled) return;
        if (enabled)
            CreateJoint();
        else
            DestroyJoint();
        jointActive = enabled;
    }

    public void CreateJoint() {
        jointTarget.transform.SetPositionAndRotation(item.transform.position, item.transform.rotation);

        if (item.flyDirRef)
            jointTarget.transform.rotation = item.flyDirRef.rotation;
        else if (item.holderPoint)
            jointTarget.transform.rotation = item.holderPoint.rotation * Quaternion.AngleAxis(180, Vector3.up);
        else
            jointTarget.transform.rotation = Quaternion.LookRotation(item.transform.up, item.transform.forward);
        
        joint = jointTarget.gameObject.AddComponent<ConfigurableJoint>();
        joint.autoConfigureConnectedAnchor = false;
        joint.connectedBody = item.physicBody.rigidBody;
        joint.targetRotation = Quaternion.identity;
        joint.rotationDriveMode = RotationDriveMode.Slerp;
        joint.xMotion = ConfigurableJointMotion.Free;
        joint.yMotion = ConfigurableJointMotion.Free;
        joint.zMotion = ConfigurableJointMotion.Free;
        joint.xDrive = joint.yDrive = joint.zDrive = joint.slerpDrive = new JointDrive {
            positionSpring = SpellCastSlingblade.jointSpring,
            positionDamper = SpellCastSlingblade.jointDamper,
            maximumForce = SpellCastSlingblade.jointMaxForce
        };
        joint.anchor = Vector3.zero;
        joint.connectedAnchor = Vector3.zero;
        joint.massScale = 1 / SpellCastSlingblade.jointMassScale;
        joint.connectedMassScale = SpellCastSlingblade.jointMassScale;
    }
    public void DestroyJoint() {
        Destroy(joint);
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
        item.StopFlying();
        item.StopThrowing();
        item.Throw(1, Item.FlyDetection.Forced);
        // item.IgnoreRagdollCollision(hand?.creature.ragdoll ?? Player.currentCreature.ragdoll);
        // item.Invoke(nameof(Item.ResetRagdollCollision), 0.3f);
    }

    public void HomeTo(Creature creature) {
        homingTarget = creature;
    }
}

public enum MoveMode {
    PID,
    Joint,
    Lerp,
    Teleport
}

public struct MoveTarget {
    private Transform parent;
    private Transform lookTarget;
    private Vector3? lookUpDir;
    private bool lookAway;
    private Vector3 position;
    private Quaternion rotation;
    public MoveMode mode;
    private bool localRotation = true;
    public float speed;
    private Func<(Vector3 position, Quaternion rotation)> func;

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
    
    public (Vector3 position, Quaternion rotation) Get {
        get {
            (Vector3? pos, Quaternion? rot) = func?.Invoke() ?? (Position, Rotation);
            return (pos ?? Position, rot ?? Rotation);
        }
    }

    private Vector3 Position => parent != null
        ? localRotation ? parent.TransformPoint(position) : parent.transform.position + position
        : position;

    private Quaternion Rotation => lookTarget
        ?
        lookAway
            ? Quaternion.LookRotation(Position - lookTarget.position, lookUpDir ?? Vector3.up)
            : Quaternion.LookRotation(lookTarget.position - Position, lookUpDir ?? Vector3.up)
        : parent != null && localRotation
            ? parent.transform.rotation * rotation
            : rotation;
}