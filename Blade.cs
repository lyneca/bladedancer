using System;
using System.Collections;
using System.Collections.Generic;

using System.Linq;
using Bladedancer.Skills;
using ThunderRoad;
using ThunderRoad.DebugViz;
using ThunderRoad.Skill;
using ThunderRoad.Skill.Spell;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace Bladedancer; 

public class Blade : ThunderBehaviour, IStringable {
    public const string ContainerName = "DeleteMeIfYouUninstallBladedancer";
    public const string BladeItem = "BladeItem";

    [SkillCategory("General", Category.Base)]
    [ModOption("Blade Spawning", "Whether a blade will be spawned when needed if you have none on your person.")]
    public static bool allowSpawn;
    
    public Item item;
    public static string defaultBladeId;
    public static ItemData defaultItemData;
    public static List<Blade> all = new();
    public static List<Blade> slung = new();
    public static List<Blade> despawning = new();

    public Dictionary<ColliderGroup, bool> isMetal;
    public delegate void BladeDelegate(Blade blade);
    public delegate void HitEntity(Blade blade, ThunderEntity entity, CollisionInstance hit);
    public event HitEntity OnHitEntity;
    public event BladeDelegate OnSlingEnd;
    public event BladeDelegate OnDespawn;
    public event BladeDelegate OnImbuesChanged;

    public delegate void GuidanceDelegate(Blade blade, bool ungrab);
    public event GuidanceDelegate OnGuidanceStop;

    public delegate void PenetrationDelegate(Blade blade, CollisionInstance hit, Damager damager);
    public event PenetrationDelegate OnPenetrate;
    public event PenetrationDelegate OnUnPenetrate;

    public delegate void QuiverEvent(Blade blade, Quiver quiver);

    public event QuiverEvent OnAddToQuiver;
    public event QuiverEvent OnRemoveFromQuiver;

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
    protected Rigidbody customJointTarget;
    protected bool jointActive;
    protected Joint joint;
    protected bool intangible;
    protected bool autoDespawn;
    protected Coroutine despawnRoutine;
    public bool wasSlung;
    protected bool moveTargetDirty;

    public float throwTime;
    public ThunderEntity homingTarget;
    public Transform homingTargetPoint;
    public bool shouldRetrieve;
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
    public bool guided;
    public RagdollHand guidanceHand;
    private float guidanceSpeed;
    private bool finishedScaling;
    private ScaleMode lastScaleMode;
    
    [SkillCategory("Debug", 1000000)]
    [ModOption("Show Movement Debug", "Draw lines between blade center and blade target position.")]
    public static bool showMoveDebug;

    private float retrieveDelay;
    private bool vizEnabled;

    protected NavMeshPath navPath;
    public bool wandering;
    private float wanderRadius;
    private Vector3 wanderCenter;
    private Vector3 wanderTarget;
    private int wanderIndex;

    public Quiver Quiver {
        get => _quiver;
        set {
            if (Quiver)
                lastQuiver = Quiver;
            if (value != null && value != lastQuiver) {
                lastQuiver = value;
                OnSlingEnd?.Invoke(this);
                SetOwner(value);
            }
            _quiver = value;
        }
    }

    public Quiver OwningQuiver => Quiver ?? lastQuiver;

    public bool InQuiver => Quiver != null && Quiver.Has(this);
    
    public static void SetCustomBlade(string id) {
        Debug.Log($"Clearing blade before re-setting to {id}...");
        ClearCustomBlade(false);
        Debug.Log($"Setting custom blade to {id}");
        Player.currentCreature.SetVariable(BladeItem, Catalog.GetData<ItemData>(id));
        Player.characterData.AddToContainer(ContainerName, new CustomBladeContent(id));
        Player.characterData.SaveAsync();
    }
    public static void ClearCustomBlade(bool save = true) {
        Debug.Log($"Clearing custom blade{(save ? " and saving" : " without saving")}");
        if (Player.characterData.TryGetContainer(ContainerName, out var contents)) {
            for (var i = contents.Count - 1; i >= 0; i--) {
                if (contents[i] is CustomBladeContent)
                    contents.RemoveAt(i);
            }
        }
        Player.currentCreature.SetVariable(BladeItem, Catalog.GetData<ItemData>(defaultBladeId));
        if (save)
            Player.characterData.SaveAsync();
    }
    public static ItemData LoadSavedCustomBlade() {
        if (Player.characterData == null
            || !Player.characterData.TryGetContainer(ContainerName, out var contents)) return null;

        for (var i = 0; i < contents.Count; i++) {
            if (contents[i] is not CustomBladeContent bladeContent
                || !Catalog.TryGetData(bladeContent.referenceID, out ItemData item)) continue;
            Debug.Log($"Loaded custom blade {item.data.id}");
            return item;
        }

        return null;
    }

    public static ItemData GetBladeItemData(Creature creature) {
        if (creature && creature.TryGetVariable(BladeItem, out ItemData data)) return data;
        return defaultItemData;
    }

    public bool IsValid
        => item
           && item.loaded
           && item.isUsed
           && item.transform
           && !float.IsNaN(item.transform.position.x)
           && !float.IsNaN(item.transform.position.y)
           && !float.IsNaN(item.transform.position.z)
           && !float.IsInfinity(item.transform.position.x)
           && !float.IsInfinity(item.transform.position.y)
           && !float.IsInfinity(item.transform.position.z)
           && (item.transform.position - Player.local.transform.position).sqrMagnitude < 500 * 500;

    public static bool GetOrSpawn(
        Action<Blade, bool> callback,
        Vector3 position,
        Quaternion rotation,
        Creature creature,
        bool forceSpawn = false) {
        if (Quiver.TryGet(creature, out var quiver)
            && quiver.TryGetClosestBlade(position, out var quiverBlade)) {
            callback(quiverBlade, false);
            return true;
        }
        
        if (allowSpawn || forceSpawn)
            return Spawn(blade => callback(blade, true), position, rotation, creature);
        return false;
    }
    public static bool Spawn(
        Action<Blade> callback,
        Vector3 position,
        Quaternion rotation,
        Creature creature) {
        
        var data = GetBladeItemData(creature);
        if (data == null) {
            Debug.LogWarning($"Blade item data is null for creature '{creature}'?");
            return false;
        }
        data.SpawnAsync(item => {
            var blade = item.GetOrAddComponent<Blade>();
            item.transform.rotation
                = rotation * Quaternion.Inverse(item.transform.InverseTransformRotation(blade.ForwardRotation));
            blade.item.SetOwner(Item.Owner.None);
            if (!SpellCastBlade.allowRangedExpert)
                blade.item.data.flags &= ~ItemFlags.Piercing;
            callback(blade);
        }, position, rotation);
        return true;
    }

    public static bool TryGetClosestSlungInRadius(Vector3 position, float radius, out Blade closest, Quiver ignoreQuiver = null) {
        float distance = radius * radius;
        closest = null;
        for (var i = 0; i < slung.Count; i++) {
            var blade = slung[i];
            if (blade == null) continue;
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
        item.OnDespawnEvent += OnItemDespawn;
        item.OnImbuesChangeEvent += OnImbuesChange;
        item.mainCollisionHandler.OnCollisionStartEvent += OnCollisionStart;
        item.DisallowDespawn = true;
        item.data.moduleAI = null;
        isMetal = new Dictionary<ColliderGroup, bool>();
        for (var i = 0; i < item.colliderGroups.Count; i++) {
            isMetal[item.colliderGroups[i]] = item.colliderGroups[i].isMetal;
        }

        lastScaleMode = ScaleMode.FullSize;
        
        localBounds = item.GetLocalBounds();
        largestAxis = localBounds.size.x > localBounds.size.y ? Axis.X : Axis.Y;
        largestAxis = localBounds.size.GetAxis(largestAxis) > localBounds.size.z ? largestAxis : Axis.Z;

        orgLargestAxisSize = localBounds.size.GetAxis(largestAxis);
        scaleRatio = Mathf.Clamp01(Quiver.targetWeaponSize / orgLargestAxisSize);
        orgMass = item.physicBody.mass;
        // orgCustomCenterOfMass = item.customCenterOfMass;
        scaledMass = item.physicBody.mass * scaleRatio;
        
        smallestAxis = localBounds.size.x < localBounds.size.y ? Axis.X : Axis.Y;
        smallestAxis = localBounds.size.GetAxis(smallestAxis) < localBounds.size.z ? smallestAxis : Axis.Z;
        
        all.Add(this);

        ignoredRagdolls = new HashSet<Ragdoll>();
        
        RegisterPenetrationEvents();
        
        EnsureJointTarget();
        shouldRetrieve = true;
        item.ForceLayer(LayerName.MovingItem);
        pid = new RBPID(item.physicBody.rigidBody, forceMode: SpellCastBlade.pidForceMode,
                maxForce: SpellCastBlade.pidMaxForce, anchor: item.GetLocalCenter())
            .Position(10, 0, SpellCastBlade.pidDamping)
            .Rotation(30, 0, 3);
        // hack to get blades that are fire imbued on start to not have molten arc
        this.RunAfter(() => SetMoltenArc(!InQuiver), 0.1f);
    }

    private void OnImbuesChange() {
        SetMoltenArc(!InQuiver);
        OnImbuesChanged?.Invoke(this);
    }

    private void OnCollisionStart(CollisionInstance hit) {
        if ((hit.targetColliderGroup?.collisionHandler?.item as ThunderEntity
             ?? hit.targetColliderGroup?.collisionHandler?.ragdollPart?.ragdoll?.creature) is ThunderEntity entity) {
            OnHitEntity?.Invoke(this, entity, hit);
        }

        if (item.IsFree && !InQuiver) {
            homingTarget = null;
            homingTargetPoint = null;
            StartDespawn();
        }
    }

    public void OnItemDespawn(EventTime time) {
        if (time != EventTime.OnStart) return;
        OnDespawn?.Invoke(this);
        item.OnDespawnEvent -= OnItemDespawn;
        all.Remove(this);
        if (wasSlung)
            OnSlingEnd?.Invoke(this);
        slung.Remove(this);
        Quiver?.ForceRemoveFromQuiver(this);
        Destroy(this);
    }

    public void OnDestroy() {
        all.Remove(this);
        if (jointTarget) {
            Destroy(jointTarget.gameObject);
            jointTarget = null;
        }
    }

    private void SetOwner(Quiver newQuiver) {
        OnlyIgnoreRagdoll(newQuiver.creature.ragdoll, true);
        for (var i = 0; i < item.imbues.Count; i++) {
            var imbue = item.imbues[i];
            if (imbue.spellCastBase is SpellCastBlade blade) continue;
            if (newQuiver.creature.HasSkill(imbue.spellCastBase)) {
                imbue.imbueCreature ??= newQuiver.creature;
                imbue.imbueCreature.mana.InvokeOnImbueUnload(imbue.spellCastBase, imbue);
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

    public void InvokeAddToQuiver(Quiver quiver) {
        SetMoltenArc(false);
        SetIsMetal(false);
        OnAddToQuiver?.Invoke(this, quiver);
    }

    public void InvokeRemoveFromQuiver(Quiver quiver) {
        SetMoltenArc(true);
        SetIsMetal(true);
        OnRemoveFromQuiver?.Invoke(this, quiver);
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

    public void SetIsMetal(bool active) {
        if (active) {
            foreach (var kvp in isMetal) {
                if (kvp.Key == null) continue;
                kvp.Key.isMetal = kvp.Value;
            }
        } else {
            foreach (var kvp in isMetal) {
                if (kvp.Key == null) continue;
                kvp.Key.isMetal = false;
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
            damager.OnPenetrateEvent -= OnBladePenetrate;
            damager.OnPenetrateEvent += OnBladePenetrate;
            damager.OnUnPenetrateEvent -= OnBladeUnPenetrate;
            damager.OnUnPenetrateEvent += OnBladeUnPenetrate;
        }
    }
    
    private void OnBladePenetrate(Damager damager, CollisionInstance collision) {
        OnPenetrate?.Invoke(this, collision, damager);
    }

    private void OnBladeUnPenetrate(Damager damager, CollisionInstance collision) {
        OnUnPenetrate?.Invoke(this, collision, damager);
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
        yield return 0;
        OnSlingEnd?.Invoke(this);
        wasSlung = false;
    }

    public IEnumerator DespawnRoutine() {
        despawning.Add(this);
        yield return new WaitForSeconds(retrieveDelay == -1 ? SpellCastBlade.collectTime : retrieveDelay);
        retrieveDelay = -1;
        despawning.Remove(this);
        if (IsFree) ReturnOrDespawn(Quiver ? Quiver : lastQuiver);
    }

    public void AllowDespawn(bool enabled, float retrieveDelay = -1) {
        if (enabled == autoDespawn) return;
        this.retrieveDelay = retrieveDelay;
        autoDespawn = enabled;
        if (!autoDespawn) StopDespawn();
    }

    public bool ReturnToQuiver(Quiver quiver, bool force = false) {
        transform.localScale = Vector3.one;
        StartCoroutine(UnSling());
        AllowDespawn(false);
        item.StopThrowing();
        item.StopFlying();
        item.lastHandler = quiver.creature.handRight;
        item.FullyUnpenetrate();
        if ((shouldRetrieve || force)
            && quiver
            && quiver.creature
            && !quiver.creature.isKilled
            && quiver.AddToQuiver(this)) return true;
        Release();
        return false;
    }

    public bool TryDepositIn(Holder holder) {
        if (!holder.ObjectAllowed(item) || !holder.HasSlotFree()) return false;
        if (InQuiver) Quiver.RemoveFromQuiver(this);
        OnlyIgnoreRagdoll(Quiver.creature.ragdoll);
        MoveTo(new MoveTarget(MoveMode.PID, 16)
            .Intangible(true)
            .Parent(holder.transform)
            .LookAt(holder.transform)
            .OnReach(blade => {
                blade.Release();
                holder.Snap(blade.item);
                if (blade && blade.item && blade.item.holder != null)
                    blade.StartDespawn();
            }));
        return true;
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

    public void IgnoreCollider(Collider collider, bool ignore = true) {
        for (var i = 0; i < item.colliderGroups.Count; i++) {
            for (var j = 0; j < item.colliderGroups[i].colliders.Count; j++) {
                Physics.IgnoreCollision(collider, item.colliderGroups[i].colliders[j], ignore);
            }
        }
    }

    public void ReturnOrDespawn(Quiver quiver) {
        if (shouldRetrieve
            && quiver
            && ReturnToQuiver(quiver)) return;

        // Couldn't add to quiver, despawn this blade
        Despawn();
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

        if (!IsValid) {
            Debug.LogError($"WARNING: Blade {this} no longer valid! See following logs for diagnosis.");
            Debug.Log("ALL of the following need to be true:");
            try {
                Debug.Log($"- (bool)item: {(bool)item}\n"
                          + $"- item.loaded: {item.loaded}\n"
                          + $"- item.isUsed: {item.isUsed}\n"
                          + $"- (bool)item.transform: {(bool)item.transform}\n"
                          + $"- !float.IsNaN(item.transform.position.x): {!float.IsNaN(item.transform.position.x)}\n"
                          + $"- !float.IsNaN(item.transform.position.y): {!float.IsNaN(item.transform.position.y)}\n"
                          + $"- !float.IsNaN(item.transform.position.z): {!float.IsNaN(item.transform.position.z)}\n"
                          + $"- !float.IsInfinity(item.transform.position.x): {!float.IsInfinity(item.transform.position.x)}\n"
                          + $"- !float.IsInfinity(item.transform.position.y): {!float.IsInfinity(item.transform.position.y)}\n"
                          + $"- !float.IsInfinity(item.transform.position.z): {!float.IsInfinity(item.transform.position.z)}\n"
                          + $"- (item.transform.position - Player.local.transform.position).sqrMagnitude < 500 * 500: {(item.transform.position - Player.local.transform.position).sqrMagnitude < 500 * 500}");
            } catch (Exception e) {
                Debug.Log("Could not check validity conditions due to exception. Wait, how did we get here?");
                Debug.LogException(e);
            }
            
            Debug.Log($"My movetarget is {moveTarget}.");
            if (moveTarget is MoveTarget nonNullTarget) {
                Debug.Log($"- mode: {nonNullTarget.mode}");
                Debug.Log($"- parent: {nonNullTarget.parent}");
                Debug.Log($"- speed: {nonNullTarget.speed}");
                Debug.Log($"- pos: {nonNullTarget.Get.position}");
                Debug.Log($"- rot: {nonNullTarget.Get.rotation}");
            }
            
            try {
                Debug.Log($"My position is {transform.position} and my rotation is {transform.rotation}");
            } catch (Exception e) {
                Debug.LogWarning("Could not get transform due to error:");
                Debug.LogException(e);
            }
            try {
                Debug.Log($"My velocity is {item.physicBody.velocity}");
            } catch (Exception e) {
                Debug.LogWarning("Could not get velocity due to error:");
                Debug.LogException(e);
            }
            try {
                Debug.Log($"Here are the components on me: {string.Join(", ", gameObject.GetComponents<Component>().Select(component => component.GetType().Name))}");
            } catch (Exception e) {
                Debug.LogWarning("Could not get active components due to error:");
                Debug.LogException(e);
            }
            // Debug.Log("Now I shall commit seppuku for the health of the game. Goodnight, sweet prince...");
            // Despawn();
        }

        if (!item.loaded) return;
        
        if (showMoveDebug && moveTarget is MoveTarget target) {
            vizEnabled = true;
            Viz.Lines(this).Color(target.mode switch {
                MoveMode.Joint => Color.red,
                MoveMode.PID => Color.blue,
                MoveMode.Lerp => Color.green,
                MoveMode.Teleport => Color.white,
            }).SetPoints(item.Center, target.Get.position).Show();
            if (target.jointBody != null) {
                Viz.Dot(this, target.jointBody.transform.position);
            }
        } else if (vizEnabled) {
            vizEnabled = false;
            Viz.Lines(this).Hide();
            Viz.Dot(this, transform.position).Hide();
        }
        if (MoveTarget != null || hasResetAfterFree || !(Time.time - freeStartTime > 1f)) return;
        ResetIgnoredRagdolls();
        hasResetAfterFree = true;
    }

    public void RefreshWander() {
        var circle = Random.insideUnitCircle * wanderRadius;
        wanderTarget = wanderCenter + new Vector3(circle.x, 2, circle.y);
        navPath = new NavMeshPath();
        var start = NavMesh.SamplePosition(transform.position, out var hitStart, 10, NavMesh.AllAreas)
            ? hitStart.position
            : transform.position;
        var end = NavMesh.SamplePosition(wanderTarget, out var hitEnd, 10, NavMesh.AllAreas)
            ? hitEnd.position
            : wanderTarget;

        NavMesh.CalculatePath(start, end, NavMesh.AllAreas, navPath);

        if (navPath == null) {
            wandering = false;
            Debug.Log("Null nav path");
            return;
        }

        if (navPath.status is NavMeshPathStatus.PathInvalid || navPath.corners.Length < 2) {
            Release();
            Debug.Log($"Invalid nav path ({navPath.status}), length {navPath.corners.Length}");
            return;
        }

        wanderIndex = 1;
        MoveTo(
            new MoveTarget(MoveMode.PID, 3).At(navPath.corners[wanderIndex] + Vector3.up * 0.5f), false);
    }

    protected override void ManagedFixedUpdate() {
        base.ManagedFixedUpdate();
        if (item == null || !item.loaded) return;

        if (wandering) {
            if (navPath == null || Vector3.Distance(transform.position, wanderTarget) < 2) RefreshWander();
            if (navPath != null && Vector3.Distance(transform.position, wanderTarget) < 2) {
                if (++wanderIndex >= navPath.corners.Length)
                    RefreshWander();
                MoveTo(
                    new MoveTarget(MoveMode.PID, 3).At(navPath.corners[wanderIndex] + Vector3.up * 0.5f), false);
            }
        }

        Move();

        if (InQuiver || MoveTarget?.intangible == true)
            SetIntangible(MoveTarget is MoveTarget target
                          && (transform.position - target.Get.position).sqrMagnitude
                          > SpellCastBlade.intangibleThreshold * SpellCastBlade.intangibleThreshold);

        if (MoveTarget == null && homingTarget != null) {
            var target = homingTarget is Creature creature ? creature.ragdoll.targetPart.transform : homingTarget.RootTransform;
            var rotationToFaceTarget = Quaternion.FromToRotation(item.physicBody.velocity,
                (homingTargetPoint ? homingTargetPoint : target).position - item.transform.position);
            item.physicBody.velocity = Quaternion.Slerp(Quaternion.identity, rotationToFaceTarget,
                                           Time.deltaTime
                                           * 30f
                                           * Mathf.Clamp01((Time.time - throwTime) / 0.5f) * 2)
                                       * item.physicBody.velocity.normalized
                                       * 20;
        }

        if (MoveTarget == null && guided && guidanceHand != null && guidanceSpeed > 0) {
            var direction = (guidanceHand.caster.rayDir.position
                             - (guidanceHand.upperArmPart.transform.position
                                + new Vector3(0.0f,
                                    guidanceHand.creature.morphology.armsToEyesHeight
                                    * guidanceHand.creature.transform.localScale.y, 0.0f))).normalized;
            guidanceHand.HapticTick(Mathf.Lerp(0.01f, 1f,
                Mathf.InverseLerp(0, SkillFiresGuidance.hapticAngleAmount,
                    Vector3.Angle(item.physicBody.velocity, direction))));
            
            item.physicBody.velocity = direction * guidanceSpeed;
        }
    }

    public void MoveTo(MoveTarget? target, bool stopWandering = true) {
        if (stopWandering) wandering = false;
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
        wandering = false;
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
    public void StopGuidance(bool fromUnGrab = false) {
        OnGuidanceStop?.Invoke(this, fromUnGrab);
        guided = false;
        guidanceSpeed = 0;
        guidanceHand = null;
    }

    /// <summary>
    /// Starts guidance of the blade (like fireballs)
    /// </summary>
    /// <param name="hand">Hand that controls the blade</param>
    /// <param name="speed">Blade flight speed</param>
    public void StartGuidance(RagdollHand hand, float speed) {
        guided = true;
        guidanceSpeed = speed;
        guidanceHand = hand;
    }
    
    /// <summary>
    /// Stop all controlled movement of the blade and start the despawn timer
    /// </summary>
    /// <remarks>
    /// When the timer runs out, the blade will attempt to return to its last quiver, or otherwise despawn.
    /// </remarks>
    /// <param name="allowRetrieve">Whether the blade should attempt to return to its quiver after the timer has run out</param>
    public void Release(bool allowRetrieve = true, float retrieveDelay = -1, bool resetScale = true) {
        CancelMovement();
        shouldRetrieve = allowRetrieve;
        item.data.drainImbueWhenIdle = true;
        AllowDespawn(true, retrieveDelay);
        SetTouch(true);
        if (resetScale)
            ScaleInstantly(ScaleMode.FullSize);
        item.Throw();
        item.lastHandler ??= Player.currentCreature.handLeft;
    }

    public void Wander(Vector3 position, float radius) {
        wanderCenter = position;
        wanderRadius = radius;
        wandering = true;
        RefreshWander();
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
        EnsureJointTarget();
        if (MoveTarget is not { Get: (position: var position, rotation: var rotation) } target) {
            SetJoint(false);
            SetIntangible(false);
            item.RemovePhysicModifier(this);
            pidActive = false;
            return;
        }

        if (target is { hasReached: false, actionOnReach: Action<Blade> action } && DistanceToTarget <= target.reachRadius) {
            target.hasReached = true;
            action.Invoke(this);
        }

        if (intangible && (transform.position - position).sqrMagnitude < SpellCastBlade.intangibleThreshold)
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
                        finishedScaling = true;
                        // item.customCenterOfMass = orgCustomCenterOfMass * scaleRatio;
                    } else {
                        transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * scaleRatio,
                            Time.unscaledDeltaTime * SpellCastBlade.scaleSpeed);
                        item.physicBody.mass = Mathf.Lerp(item.physicBody.mass, scaledMass,
                            Time.unscaledDeltaTime * SpellCastBlade.scaleSpeed);
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
                        finishedScaling = true;
                        // item.customCenterOfMass = orgCustomCenterOfMass;
                    } else {
                        transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one,
                            Time.unscaledDeltaTime * SpellCastBlade.scaleSpeed);
                        item.physicBody.mass = Mathf.Lerp(item.physicBody.mass, orgMass,
                            Time.unscaledDeltaTime * SpellCastBlade.scaleSpeed);
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
                pid.Update(position,
                    rotation
                    * Quaternion.Inverse(item.transform.InverseTransformRotation(ForwardRotation)),
                    target.speed);
                break;
        }
    }

    public void EnsureJointTarget() {
        if (jointTarget != null) return;
        jointTarget = new GameObject().AddComponent<Rigidbody>();
        jointTarget.isKinematic = true;
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

    public Quaternion ForwardRotation {
        get {
            if (item.flyDirRef) {
                var lookDir = Quaternion.LookRotation(item.flyDirRef.forward, item.transform.GetUnitAxis(smallestAxis));
                return lookDir;
            }

            if (item.holderPoint) {
                return item.transform.rotation
                       * Quaternion.Inverse(item.transform.InverseTransformRotation(item.holderPoint.rotation
                           * Quaternion.AngleAxis(180, Vector3.up)));
            }

            return Quaternion.LookRotation(item.transform.up, item.transform.forward);
        }
    }

    public void CreateJoint() {
        var targetBody = moveTarget?.jointBody ? moveTarget?.jointBody : jointTarget;
        if (targetBody == null) return;
        
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
                positionSpring = moveTarget?.jointSpring ?? SpellCastBlade.jointSpring,
                positionDamper = moveTarget?.jointDamper ?? SpellCastBlade.jointDamper,
                maximumForce = moveTarget?.jointMaxForce ?? SpellCastBlade.jointMaxForce
            };
            joint = configJoint;
        } else {
            switch (target.jointType) {
                case JointType.Spring:
                    var springJoint = targetBody.gameObject.AddComponent<SpringJoint>();
                    springJoint.spring = moveTarget?.jointSpring ?? SpellCastBlade.jointSpring;
                    springJoint.damper = moveTarget?.jointDamper ?? SpellCastBlade.jointDamper;
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

        float massScale = moveTarget?.jointMassScale ?? SpellCastBlade.jointMassScale;
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

    public static Creature AimAssist(
        Ray ray,
        float distance,
        float angle,
        out Transform targetPoint,
        Func<Creature, bool> filter,
        CreatureType weakpointFilter) {
        var target = Creature.AimAssist(ray, distance, angle, out targetPoint, filter, weakpointFilter) as Creature;
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
            if (Creature.AimAssist(ray, distance, angle, out targetPoint, filter, weakpointFilter)) {
                target.weakpoints = oldList;
                return target;
            }

            target.weakpoints = oldList;
        }

        return null;
    }

    public void AddForce(Vector3 force, ForceMode forceMode, bool aimAssist = false, bool resetVelocity = false, RagdollHand hand = null) {
        throwTime = Time.time;
        if (resetVelocity) {
            item.physicBody.velocity = hand?.creature.currentLocomotion.velocity ?? Vector3.zero;
        }

        var position = hand?.caster.magicSource.position ?? transform.position;

        if (aimAssist) {
            var ray = new Ray(position, force);
            Creature owner = null;
            if (item.lastHandler != null)
                owner = item.lastHandler.creature;
            if (owner == null && Quiver != null)
                owner = Quiver.creature;

            if (owner != null) {
                if (AimAssist(ray, 20, 30, out var targetPoint, Filter.EnemyOf(owner),
                        CreatureType.Animal | CreatureType.Golem | CreatureType.Human)) {
                    force = (targetPoint.position - transform.position).normalized * force.magnitude;
                }
            }
        }

        item.AddForce(force, ForceMode.VelocityChange);
        item.Throw(1, Item.FlyDetection.Forced);
        Quiver?.InvokeBladeThrow(this);
    }

    public void HomeTo(ThunderEntity entity, Transform point) {
        homingTarget = entity;
        homingTargetPoint = point;
    }

    public bool ImbuedWith(string spellId)
        => !string.IsNullOrEmpty(spellId) && ImbuedWith(Animator.StringToHash(spellId.ToLower()));

    public bool TryGetImbue(string spellId, out Imbue imbue) {
        imbue = null;
        if (string.IsNullOrEmpty(spellId)) return false;
        int hashId = Animator.StringToHash(spellId.ToLower());
        if (item.imbues == null) return false;
        for (var i = 0; i < item.imbues.Count; i++) {
            if (item.imbues[i].spellCastBase is SpellCastCharge spell && spell.hashId == hashId) {
                imbue = item.imbues[i];
                return true;
            }
        }
        return false;
    }

    public bool ImbuedWith(int spellHashId) {
        if (item.imbues == null) return false;
        for (var i = 0; i < item.imbues.Count; i++) {
            if (item.imbues[i].spellCastBase is SpellCastCharge spell && spell.hashId == spellHashId) return true;
        }

        return false;
    }

    public void MaxImbue(SpellCastCharge spell, Creature creature, List<SpellSkillData> forceLoadSkills = null) {
        if (item.colliderGroups.Count == 0) return;
        for (var i = 0; i < item.colliderGroups.Count; i++) {
            var imbue = item.colliderGroups[i].imbue;
            if (imbue == null) continue;
            if (imbue.spellCastBase is SpellCastCharge currentSpell
                && currentSpell.hashId != spell.hashId) {
                imbue.SetEnergyInstant(0);
            }
            
            imbue.Transfer(spell, imbue.maxEnergy, creature);

            if (forceLoadSkills == null) continue;
            for (var j = 0; j < forceLoadSkills.Count; j++) {
                forceLoadSkills[i].OnImbueLoad(imbue.spellCastBase, imbue);
            }
        }
    }

    public string Stringify() {
        var imbue = "";
        for (var i = 0; i < item.imbues.Count; i++) {
            if (item.imbues[i].spellCastBase != null) {
                imbue = $"{item.imbues[i].spellCastBase.id} ";
            }
        }

        return $"{imbue}Blade #{Misc.HexHash(GetInstanceID())}";
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
    public bool intangible;
    private Vector3 position;
    private Quaternion rotation;
    public MoveMode mode;
    private bool localRotation = true;
    public float speed;
    private Func<(Vector3 position, Quaternion rotation)> func;
    public ScaleMode scale = ScaleMode.FullSize;
    public bool scaleInstantly = false;

    public Action<Blade> actionOnReach = null;
    public float reachRadius = 0.1f;
    public bool hasReached;
    
    public Rigidbody jointBody;
    public JointType? jointType = null;
    public float? jointMassScale = null;
    public float? jointSpring = null;
    public float? jointDamper = null;
    public float? jointMaxForce = null;
    private Vector3? lookTargetPos;

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

    public MoveTarget Intangible(bool intangible) {
        this.intangible = intangible;
        return this;
    }
    
    public MoveTarget LookAt(Transform lookTarget, bool lookAway = false, Vector3? lookUpDir = null) {
        this.lookTarget = lookTarget;
        this.lookUpDir = lookUpDir;
        this.lookAway = lookAway;
        return this;
    }
    
    public MoveTarget LookAt(Vector3 lookTarget, bool lookAway = false, Vector3? lookUpDir = null) {
        lookTargetPos = lookTarget;
        this.lookUpDir = lookUpDir;
        this.lookAway = lookAway;
        return this;
    }

    public MoveTarget OnReach(Action<Blade> action, float radius = 0.1f) {
        actionOnReach = action;
        reachRadius = radius;
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
    private Quaternion Rotation {
        get {
            var target = lookTarget?.position ?? lookTargetPos;
            if (target is not Vector3 targetPos)
                return parent != null && localRotation
                    ? parent.transform.rotation * rotation
                    : rotation;
            return lookAway
                ? Quaternion.LookRotation(Position - targetPos, LookUpDir)
                : Quaternion.LookRotation(targetPos - Position, LookUpDir);
        }
    }
}

public enum ReferenceFrame {
    None,
    Player,
    Hand
}

public enum JointType {
    Config,
    Spring
}

public enum ScaleMode {
    FullSize,
    Scaled
}