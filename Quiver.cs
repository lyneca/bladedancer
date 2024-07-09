using System;
using System.Collections.Generic;
using Bladedancer.Skills;
using ThunderRoad;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Bladedancer; 

public class Quiver : ThunderBehaviour {
    public static List<int> allowedQuiverItemHashIds;
    public const string MaxCount = "MaxCrownCount";
    
    [SkillCategory("Crown of Knives", Category.Base, 2)]
    [ModOptionIntValuesDefault(0, 12, 1, 4)]
    [ModOptionSlider, ModOption("Max Blade Count", "Maximum number of blades the Crown of Knives can store at once.")]
    public static int baseQuiverCount;

    [SkillCategory("Crown of Knives", Category.Base, 2)]
    [ModOptionFloatValuesDefault(30, 90, 30, 60)]
    [ModOptionSlider, ModOption("Crown Spread Angle", "Spread angle for daggers in the crown.")]
    public static float quiverSpread;

    public Creature creature;
    public Mode mode;
    public Transform target;
    public Vector3 lookDirection;

    public static FloatHandler GetMaxCountHandler(Creature creature) {
        if (creature.TryGetVariable(MaxCount, out FloatHandler handler))
            return handler;
        handler = new FloatHandler(baseQuiverCount);
        creature.SetVariable(MaxCount, handler);
        return handler;
    }

    public FloatHandler MaxCountHandler => GetMaxCountHandler(creature);

    public BoolHandler ignoreSelf;
    public BoolHandler ignoreCap;
    public BoolHandler isDangerous;
    public BoolHandler preventBlock;


    public bool IsFull => !ignoreCap && Count >= Max;
    public int Max => Mathf.FloorToInt(MaxCountHandler);

    public static Quiver Main => Player.currentCreature ? Player.currentCreature.GetOrAddComponent<Quiver>() : null;
    public static bool TryGet(Creature creature, out Quiver quiver) {
        quiver = null;
        if (!creature) return false;
        quiver = creature.GetOrAddComponent<Quiver>();
        return true;
    }

    public static Quiver Get(Creature creature) => creature ? creature.GetOrAddComponent<Quiver>() : null;
    
    public delegate void BladeThrow(Quiver quiver, Blade blade);
    public event BladeThrow OnBladeThrow;

    public delegate void CountChangeEvent(Quiver quiver);
    public event CountChangeEvent OnCountChangeEvent;

    public delegate void BladeEvent(Quiver quiver, Blade blade);
    public event BladeEvent OnBladeAddEvent;
    public event BladeEvent OnBladeRemovedEvent;

    public override ManagedLoops EnabledManagedLoops => ManagedLoops.Update;

    public void Awake() {
        if (ModOptions.TryGetOption("Max Blade Count (TEMP)", out var maxBladeCountOption)) {
            maxBladeCountOption.ValueChanged += obj => {
                MaxCountHandler.baseValue = obj is int count ? count : 6;
                MaxCountHandler.Add(maxBladeCountOption, 1);
                MaxCountHandler.Remove(maxBladeCountOption);
                RefreshQuiver();
            };
        }

        if (ModOptions.TryGetOption("Crown Spread Angle", out var crownSpreadAngleOption)) {
            crownSpreadAngleOption.ValueChanged += _ => RefreshQuiver();
        }
        
        creature = GetComponent<Creature>();
        creature.OnDespawnEvent += Despawn;
        creature.OnKillEvent += OnKill;
        
        creature.ragdoll.OnStateChange += OnRagdollStateChange;

        preventBlock = new BoolHandler(false);
        ignoreCap = new BoolHandler(false);
        ignoreSelf = new BoolHandler(false);
        ignoreSelf.OnChangeEvent += OnIgnoreSelfChange;
        isDangerous = new BoolHandler(false);
        
        isUnconscious = creature.brain.isUnconscious;
        isCrouching = creature.currentLocomotion.isCrouched;
        
        ignoreCap.OnChangeEvent -= OnQuiverIgnoreCapChange;
        ignoreCap.OnChangeEvent += OnQuiverIgnoreCapChange;
        MaxCountHandler.OnChangeEvent -= OnQuiverMaxChange;
        MaxCountHandler.OnChangeEvent += OnQuiverMaxChange;
    }

    private void OnRagdollStateChange(
        Ragdoll.State prevState,
        Ragdoll.State nextState,
        Ragdoll.PhysicStateChange physicStateChange,
        EventTime time) {
        if (time == EventTime.OnStart || nextState is not (Ragdoll.State.Destabilized or Ragdoll.State.Standing)) return;
        RefreshQuiver();
    }

    private void OnIgnoreSelfChange(bool oldValue, bool newValue) {
        IgnoreBetweenBlades(newValue);
    }

    public void IgnoreBetweenBlades(bool ignore) {
        for (int i = 0; i < blades.Count - 1; i++) {
            for (int j = i + 1; j < blades.Count; j++) {
                blades[i].IgnoreBlade(blades[j], ignore);
            }
        }
    }

    private void OnQuiverIgnoreCapChange(bool oldValue, bool newValue) => RefreshQuiver();
    private void OnQuiverMaxChange(float oldValue, float newValue) => RefreshQuiver();

    private void OnKill(CollisionInstance hit, EventTime time) {
        if (time == EventTime.OnEnd) return;
        creature.OnKillEvent -= OnKill;
        Despawn(EventTime.OnStart);
    }

    public void Despawn(EventTime time) {
        if (time != EventTime.OnStart) return;
        DumpAll();
        Destroy(this);
    }

    public List<Blade> blades = new();
    private bool isUnconscious;
    private bool isCrouching;
    private bool isIncapacitated;

    public void InvokeBladeThrow(Blade blade) => OnBladeThrow?.Invoke(this, blade);

    public void ForAllBlades(Action<Blade> action) {
        foreach (var blade in new List<Blade>(blades)) {
            action?.Invoke(blade);
        }
    }

    public bool Has(Blade blade) => blades.Contains(blade);

    public void RetrieveNearby(bool everything = false, float radius = 5) {
        if (IsFull) return;
        if (everything) {
            for (int i = Blade.all.Count - 1; i >= 0; i--) {
                if (Blade.all[i] is { MoveTarget: null, IsFree: true } blade) {
                    if ((blade.transform.position - transform.position).sqrMagnitude > radius * radius) continue;
                    blade.ReturnToQuiver(this);
                }

                if (IsFull) return;
            }
        } else {
            for (int i = Blade.despawning.Count - 1; i >= 0; i--) {
                if (IsFull) return;
                if (!Blade.despawning[i] || Blade.despawning[i] is not Blade blade) {
                    Blade.despawning.RemoveAt(i);
                    continue;
                }

                if ((blade.transform.position - transform.position).sqrMagnitude > radius * radius) continue;
                blade.ReturnToQuiver(this, true);
            }
        }
    }

    public bool AddToQuiver(Blade blade, bool randomIndex = false) {
        if (!blade
            || blade == null
            || blades.Contains(blade)) return false;

        if (IsFull) {
            if (!TryGetClosestFreeHolster(blade, out var holder) || !blade.TryDepositIn(holder)) return false;
            blade.Quiver = this;
            blade.OnAddToQuiver(this);
            blade.AllowDespawn(false);
            blade.SetTouch(false);
            blade.item.StopFlying();
            blade.item.StopThrowing();
            blade.StopGuidance();
            return true;
        }

        if (!creature.TryGetVariable(SkillCrownOfKnives.HasCrown, out bool hasCrown) || !hasCrown) return false;

        if (randomIndex)
            blades.Insert(Random.Range(0, blades.Count), blade);
        else
            blades.Add(blade);
        if (ignoreSelf) {
            blade.IgnoreBlades(blades);
        }

        blade.Quiver = this;
        blade.OnAddToQuiver(this);
        blade.AllowDespawn(false);
        blade.SetTouch(false);
        blade.item.StopFlying();
        blade.item.StopThrowing();
        blade.StopGuidance();
        
        if (blade.item.isPenetrating) {
            foreach (var handler in blade.item.mainCollisionHandler.penetratedObjects) {
                blade.IgnoreItem(handler.item);
                blade.RunAfter(() => blade.IgnoreItem(handler.item, false), 0.3f);
            }
        }

        blade.item.FullyUnpenetrate();
        blade.item.mainCollisionHandler.RemoveAllPenetratedObjects();
        if ((blade.transform.position - creature.ragdoll.targetPart.transform.position).sqrMagnitude
            > SpellCastBlade.intangibleThreshold * SpellCastBlade.intangibleThreshold) {
            blade.SetIntangible(true);
        }

        RefreshQuiver();
        OnCountChangeEvent?.Invoke(this);
        OnBladeAddEvent?.Invoke(this, blade);
        return true;
    }

    public bool ForceRemoveFromQuiver(Blade blade) {
        bool result = blades.Remove(blade);
        if (ignoreSelf)
            blade.IgnoreBlades(blades, false);
        RefreshQuiver(true);
        OnBladeRemovedEvent?.Invoke(this, blade);
        return result;
    }

    public bool RemoveFromQuiver(Blade blade, bool refresh = true) {
        if (blade == null) {
            blades.Remove(blade);
            return false;
        }

        if (ignoreSelf)
            blade.IgnoreBlades(blades, false);

        var inventory = UIInventory.Instance.transform.GetComponentInChildren<Item>();
        if (inventory)
            blade.IgnoreItem(inventory);
        
        blade.OnRemoveFromQuiver(this);
        blade.ScaleInstantly(ScaleMode.FullSize);

        if (!blades.Remove(blade)) return false;
        blade.AllowDespawn(true);
        if (refresh)
            RefreshQuiver(true);
        OnBladeRemovedEvent?.Invoke(this, blade);
        return true;
    }

    public void DumpAll() {
        for (var i = blades.Count - 1; i >= 0; i--) {
            var blade = blades[i];
            RemoveFromQuiver(blade, false);
            blade.Release();
        }
        RefreshQuiver();
    }

    public void Fill() {
        int count = Max - blades.Count;
        for (var i = 0; i < count; i++)
            Blade.Spawn((blade, _) => blade.ReturnToQuiver(this), creature.ragdoll.headPart.transform.position,
                Quaternion.LookRotation(Vector3.up), creature, true);
    }

    public void ImbueOverTime(SpellCastCharge spell, float ratio) {
        if (blades == null) return;
        foreach (var blade in blades) {
            if (blade.item.colliderGroups.Count == 0) continue;
            for (var i = 0; i < blade.item.colliderGroups.Count; i++) {
                var imbue = blade.item.colliderGroups[i].imbue;
                if (imbue == null) continue;
                imbue.Transfer(spell, imbue.maxEnergy * ratio * Time.unscaledDeltaTime, creature);
            }
        }
    }

    public void MaxImbue(SpellCastCharge spell) {
        if (blades == null || spell == null) return;
        foreach (var blade in blades) {
            if (blade.item.colliderGroups.Count == 0) continue;
            for (var i = 0; i < blade.item.colliderGroups.Count; i++) {
                var imbue = blade.item.colliderGroups[i].imbue;
                if (imbue == null) continue;
                if (imbue.spellCastBase is SpellCastCharge currentSpell
                    && currentSpell.hashId != spell.hashId) {
                    imbue.SetEnergyInstant(0);
                }

                imbue.Transfer(spell, imbue.maxEnergy, creature);
            }
        }
    }

    public bool TryGetBlade(out Blade blade, bool refresh = true, ItemData.Type? preferredType = null) {
        List<Blade> bladesToCheck = null;

        if (preferredType != null) {
            bladesToCheck = new List<Blade>();
            for (var i = 0; i < blades.Count; i++) {
                if (blades[i].item.data.type == preferredType) {
                    bladesToCheck.Add(blades[i]);
                }
            }
        }

        if (bladesToCheck is null or { Count: 0 })
            bladesToCheck = blades;
        
        blade = null;
        if (bladesToCheck.Count == 0) return false;
        blade = bladesToCheck[blades.Count - 1];
        return RemoveFromQuiver(blade, refresh);
    }

    public bool TryGetClosestBlade(Vector3 position, out Blade blade, ItemData.Type? preferredType = null) {
        float distance = Mathf.Infinity;
        List<Blade> bladesToCheck = null;

        if (preferredType != null) {
            bladesToCheck = new List<Blade>();
            for (var i = 0; i < blades.Count; i++) {
                if (blades[i].item.data.type == preferredType) {
                    bladesToCheck.Add(blades[i]);
                }
            }
        }

        if (bladesToCheck is null or { Count: 0 })
            bladesToCheck = blades;

        if (blades.Count == 0) {
            if (TryGetBladeFromHolsters(position, out var holsterBlade)) {
                blade = holsterBlade;
                return true;
            }
        }


        blade = null;
        for (var i = 0; i < bladesToCheck.Count; i++) {
            var thisBlade = bladesToCheck[i];
            float thisDistance = (thisBlade.transform.position - position).sqrMagnitude;
            if (!(thisDistance < distance)) continue;
            distance = thisDistance;
            blade = thisBlade;
        }

        if (!blade) return false;

        RemoveFromQuiver(blade);
        return true;
    }

    public bool TryGetBladeFromHolsters(Vector3 position, out Blade blade) {
        float minDistance = Mathf.Infinity;
        blade = null;
        for (var i = 0; i < creature.holders.Count; i++) {
            var eachHolder = creature.holders[i];
            float distance = (eachHolder.transform.position - position).sqrMagnitude;
            if (distance > minDistance || eachHolder.items.Count <= 0 || eachHolder.items[0] is not Item item) continue;
            if (item.GetComponent<Blade>() is Blade holsteredBlade) {
                holsteredBlade.item.holder?.UnSnap(holsteredBlade.item);
                blade = holsteredBlade;
                minDistance = distance;
            } else if (item.data.hashId == Blade.itemData.hashId) {
                eachHolder.UnSnap(item);
                item.gameObject.TryGetOrAddComponent(out blade);
                minDistance = distance;
            } else if (item.childHolders is { Count: > 0 }
                && allowedQuiverItemHashIds.Contains(item.data.hashId)
                && item.childHolders[0].items.Count > 0
                && item.childHolders[0].items[0] is Item quiverHolsteredItem) {
                minDistance = distance;
                item.childHolders[0].UnSnap(quiverHolsteredItem);
                quiverHolsteredItem.gameObject.TryGetOrAddComponent(out blade);
            }
        }

        if (blade != null) blade.Quiver = this;

        return blade != null;
    }

    public bool TryGetClosestFreeHolster(Blade blade, out Holder holder) {
        float minDistance = Mathf.Infinity;
        holder = null;
        for (var i = 0; i < creature.holders.Count; i++) {
            var eachHolder = creature.holders[i];
            float distance = (eachHolder.transform.position - blade.transform.position).sqrMagnitude;
            if (distance > minDistance || eachHolder.items.Count <= 0 || eachHolder.items[0] is not Item item) continue;
            if (item.childHolders is { Count: > 0 }
                && allowedQuiverItemHashIds.Contains(item.data.hashId)
                && item.childHolders[0] is Holder childHolder
                && childHolder.items.Count < childHolder.GetMaxQuantity()
                && childHolder.data.SlotAllowed(blade.item.data.slot)) {
                minDistance = distance;
                holder = item.childHolders[0];
            }
        }

        return holder != null;
    }
    
    public void SetMode(Mode mode, bool isDangerous = false) {
        if (this.mode == mode) return;
        this.mode = mode;
        if (isDangerous) this.isDangerous.Add(this);
        else this.isDangerous.Remove(this);
        RefreshQuiver();
    }

    protected override void ManagedUpdate() {
        base.ManagedUpdate();
        if (creature.brain.isUnconscious == isUnconscious
            && creature.brain.isIncapacitated == isIncapacitated
            && creature.currentLocomotion.isCrouched == isCrouching) return;
        isIncapacitated = creature.brain.isIncapacitated;
        isUnconscious = creature.brain.isUnconscious;
        isCrouching = creature.currentLocomotion.isCrouched;
        RefreshQuiver();
    }

    public void RefreshQuiver(bool changed = false) {
        // Fix broken daggers
        for (int i = blades.Count - 1; i >= 0; i--) {
            var blade = blades[i];
            if (blade == null || !blade.IsValid) blade.Despawn();
        }
        while (IsFull && Count > Max && Max >= 0) {
            if (TryGetBlade(out var blade, false)) {
                blade.Release();
                changed = true;
            } else {
                break;
            }
        }

        var newQuiver = new List<Blade>();
        for (var i = 0; i < blades.Count; i++) {
            if (blades[i] != null && blades[i].gameObject != null) {
                newQuiver.Add(blades[i]);
            }
        }

        blades = newQuiver;
        for (var i = 0; i < blades.Count; i++) {
            blades[i].MoveTo(GetQuiverTarget(i));
            blades[i].OnlyIgnoreRagdoll(creature.ragdoll);
        }

        if (changed) {
            OnCountChangeEvent?.Invoke(this);
        }
    }

    public bool FireAtCreature(Creature creature) {
        if (!TryGetClosestBlade(creature.transform.position, out var blade)) return false;
        blade.Release();
        blade.isDangerous.Add(Blade.UntilHit);
        var vector = creature.ragdoll.targetPart.transform.position - blade.transform.position;
        blade.AddForce(vector.normalized * Mathf.Lerp(15, 40, Mathf.InverseLerp(5, 10, vector.sqrMagnitude)),
            ForceMode.VelocityChange, false, true);
        return true;
    }

    public bool Fire(Blade blade, Vector3 velocity, bool retrieve = true) {
        if (!RemoveFromQuiver(blade)) return false;
        blade.Release(retrieve);
        blade.isDangerous.Add(Blade.UntilHit);
        blade.AddForce(velocity, ForceMode.VelocityChange, false, true);
        return true;
    }

    public bool Fire(Vector3 velocity, out Blade blade, bool aimAssist = false, bool retrieve = true) {
        if (!TryGetBlade(out blade)) return false;
        blade.Release(retrieve);
        blade.isDangerous.Add(Blade.UntilHit);
        blade.AddForce(velocity, ForceMode.VelocityChange, aimAssist, true);
        return true;
    }

    public void FireAll(Vector3 velocity, bool retrieve = true, Action<Blade> callback = null) {
        while (Fire(velocity, out var blade, retrieve: retrieve)) {
            callback?.Invoke(blade);
        }
    }

    public MoveTarget? GetQuiverTarget(int i) {
        int count = blades.Count;
        float maxSpread = (float)count / Max * quiverSpread;
        float half = (blades.Count - 1f) / 2;
        float offset = blades.Count == 1 ? 0 : (i - half) / half;
        switch (mode) {
            case Mode.Crown when !creature.isPlayer && (creature.brain.isChoke || creature.IsBurning):
                var burningPosition = Quaternion.AngleAxis(360f / count * i, Vector3.up)
                                   * new Vector3(0, 0.5f, 0.3f);
                return new MoveTarget(MoveMode.PID, 6)
                    .Parent(creature.ragdoll.targetPart.transform, false)
                    .At(burningPosition)
                    .Scale(ScaleMode.Scaled)
                    .LookAt(creature.ragdoll.targetPart.transform, true);
            case Mode.Crown when !creature.isPlayer && creature.ragdoll.state is Ragdoll.State.Destabilized or Ragdoll.State.Inert:
                return null;
            case Mode.Crown when !creature.isPlayer && (creature.brain.isUnconscious || creature.brain.isIncapacitated):
                return new MoveTarget(MoveMode.PID, 2)
                    .Parent(creature.ragdoll.targetPart.transform, false)
                    .At(
                        Quaternion.AngleAxis(360f / count * i, Vector3.up)
                        * new Vector3(1.3f, Random.Range(-0.3f, 0.3f), 0),
                        Quaternion.LookRotation(Vector3.down))
                    .Scale(ScaleMode.Scaled)
                    .LookAt(creature.brain.isIncapacitated ? Player.local.head.transform : null);
            case Mode.Crown when creature.isPlayer && creature.currentLocomotion.isCrouched:
                return new MoveTarget(MoveMode.PID, 12)
                    .Parent(creature.ragdoll.targetPart.transform)
                    .At(Quaternion.AngleAxis(offset * maxSpread, Vector3.forward)
                        * new Vector3(0.1f, 0, -0.3f))
                    .Scale(ScaleMode.Scaled)
                    .LookAt(creature.ragdoll.headPart.transform, true);
            case Mode.Crown:
                return new MoveTarget(MoveMode.PID, 6)
                    .Parent(creature.ragdoll.targetPart.transform)
                    .At(Quaternion.AngleAxis(offset * maxSpread, Vector3.forward)
                        * new Vector3(-1, 0, -0.3f))
                    .Scale(ScaleMode.Scaled)
                    .LookAt(creature.ragdoll.headPart.transform);
            case Mode.Slicer:
                var rotatedPosition = Quaternion.AngleAxis(360f / count * i, Vector3.forward)
                                      * new Vector3(0, 0.15f, 0.05f);
                return new MoveTarget(MoveMode.PID, 12)
                    .Parent(target)
                    .Scale(ScaleMode.Scaled)
                    .At(rotatedPosition, Quaternion.LookRotation(lookDirection, -rotatedPosition));
            case Mode.Rain:
                var rainPosition = Quaternion.AngleAxis(360f / count * i, Vector3.forward)
                                   * new Vector3(0, 0.3f, 0.5f);
                return new MoveTarget(MoveMode.PID, 6)
                    .Parent(target)
                    .At(rainPosition)
                    .Scale(ScaleMode.Scaled)
                    .LookAt(target, true);
            case Mode.Blender:
                var blenderPosition = Quaternion.AngleAxis(360f / count * i, Vector3.forward)
                                   * new Vector3(0, SkillVortexBlender.spinDistance, 0);
                return new MoveTarget(MoveMode.Joint, 6)
                    .Parent(target)
                    .At(blenderPosition)
                    .Scale(ScaleMode.Scaled)
                    .LookAt(target, true, Vector3.forward);
            case Mode.Volley:
                TrianglePos(i, out int row, out int col, out int width);
                float xPos = (col - (width - 1) / 2f) * SkillStormVolley.size;
                float yPos = row * SkillStormVolley.height;
                return new MoveTarget(MoveMode.PID, 6)
                    .Parent(target)
                    .Scale(ScaleMode.FullSize)
                    .At(Vector3.forward * yPos + Vector3.right * xPos, Quaternion.LookRotation(Vector3.down, Vector3.forward));
            case Mode.VolleySpraying:
                int split = i / 2;
                float side = i % 2 * 2 - 1;
                TrianglePos(split, out int rowSplit, out int colSplit, out int widthSplit);
                float xSplit = (colSplit - (widthSplit - 1) / 2f) * SkillStormVolley.size * SkillStormVolley.spraySpreadMult;
                float ySplit = rowSplit * SkillStormVolley.height * SkillStormVolley.spraySpreadMult + SkillStormVolley.sprayDistanceFromPlayer;
                return new MoveTarget(MoveMode.PID, 6)
                    .Parent(target)
                    .Scale(ScaleMode.Scaled)
                    .At(Vector3.right * (ySplit * side) + Vector3.up * xSplit + Vector3.forward * 0.5f,
                        Quaternion.LookRotation(Vector3.forward));
        }

        return default;
    }

    public static void TrianglePos(int i, out int row, out int col, out int width) {
        row = Mathf.FloorToInt(Mathf.Sqrt(0.25f + 2 * i) - 0.5f);
        col = i - row * (row + 1) / 2;
        width = row + 1;
    }

    public int Count => blades.Count;
    
    public static implicit operator Quiver(Creature creature) => Get(creature);
}

