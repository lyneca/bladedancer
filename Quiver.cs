using System;
using System.Collections.Generic;
using Bladedancer.Skills;
using ThunderRoad;
using ThunderRoad.Skill;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Bladedancer; 

public class Quiver : ThunderBehaviour {
    public const string MaxCount = "MaxCrownCount";
    
    [SkillCategory("Crown of Knives", Category.Base, 2)]
    [ModOptionIntValues(0, 12, 1)]
    [ModOptionSlider, ModOption("Max Blade Count", "Maximum number of blades the Crown of Knives can store at once.", defaultValueIndex = 6)]
    public static int baseQuiverCount = 6;

    [SkillCategory("Crown of Knives", Category.Base, 2)]
    [ModOptionFloatValues(30, 90, 30)]
    [ModOptionSlider, ModOption("Crown Spread Angle", "Spread angle for daggers in the crown.", defaultValueIndex = 1)]
    public static float quiverSpread = 60f;

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

    public delegate void CountChangeEvent(Quiver quiver);
    public event CountChangeEvent OnCountChangeEvent;

    public delegate void BladeEvent(Quiver quiver, Blade blade);
    public event BladeEvent OnBladeAddEvent;
    public event BladeEvent OnBladeRemovedEvent;

    public override ManagedLoops EnabledManagedLoops => ManagedLoops.Update;

    public void Awake() {
        if (ModOptions.TryGetOption("Max Blade Count", out var maxBladeCountOption)) {
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

        preventBlock = new BoolHandler(false);
        ignoreCap = new BoolHandler(false);
        ignoreSelf = new BoolHandler(false);
        ignoreSelf.OnChangeEvent += OnIgnoreSelfChange;
        isDangerous = new BoolHandler(false);
        creature = GetComponent<Creature>();
        creature.OnDespawnEvent += Despawn;
        creature.OnKillEvent += OnKill;
        creature.ragdoll.OnStateChange += OnRagdollStateChange;
        
        isUnconscious = creature.brain.isUnconscious;
        
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
        for (int i = 0; i < quiver.Count - 1; i++) {
            for (int j = i + 1; j < quiver.Count; j++) {
                quiver[i].IgnoreBlade(quiver[j], ignore);
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
        ForAllQuiver(blade => blade.Release());
        Destroy(this);
    }

    public List<Blade> quiver = new();
    private bool isUnconscious;
    private bool isIncapacitated;

    public void ForAllQuiver(Action<Blade> action) {
        foreach (var blade in new List<Blade>(quiver)) {
            action?.Invoke(blade);
        }
    }

    public bool Has(Blade blade) => quiver.Contains(blade);

    public void RetrieveNearby(bool everything = false, float radius = 5) {
        if (IsFull) return;
        if (everything) {
            for (int i = Blade.all.Count - 1; i >= 0; i--) {
                if (Blade.all[i] is { MoveTarget: null } blade) {
                    if ((blade.transform.position - transform.position).sqrMagnitude > radius * radius) continue;
                    blade.ReturnToQuiver(this, true);
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
                blade.ReturnToQuiver(this, true, true);
            }
        }
    }

    public bool AddToQuiver(Blade blade, bool randomIndex = false) {
        if (!blade || blade == null || IsFull || quiver.Contains(blade)) return false;
        if (randomIndex)
            quiver.Insert(Random.Range(0, quiver.Count), blade);
        else
            quiver.Add(blade);
        blade.quiver = this;
        if (ignoreSelf) {
            blade.IgnoreBlades(quiver);
        }
        blade.OnlyIgnoreRagdoll(creature.ragdoll);
        blade.AllowDespawn(false);
        blade.SetTouch(false);
        blade.item.FullyUnpenetrate();
        if ((blade.transform.position - creature.ragdoll.targetPart.transform.position).sqrMagnitude
            > SpellCastSlingblade.intangibleThreshold * SpellCastSlingblade.intangibleThreshold) {
            blade.SetIntangible(true);
        }

        RefreshQuiver();
        OnCountChangeEvent?.Invoke(this);
        OnBladeAddEvent?.Invoke(this, blade);
        return true;
    }

    public bool ForceRemoveFromQuiver(Blade blade) {
        bool result = quiver.Remove(blade);
        if (ignoreSelf)
            blade.IgnoreBlades(quiver, false);
        RefreshQuiver(true);
        OnBladeRemovedEvent?.Invoke(this, blade);
        return result;
    }

    public bool RemoveFromQuiver(Blade blade, bool refresh = true) {
        if (blade == null) {
            quiver.Remove(blade);
            return false;
        }

        blade.quiver = null;
        if (ignoreSelf)
            blade.IgnoreBlades(quiver, false);

        var inventory = UIInventory.Instance.transform.GetComponentInChildren<Item>();
        if (inventory)
            blade.IgnoreItem(inventory);
        
        if (!quiver.Remove(blade)) return false;
        blade.AllowDespawn(true);
        if (refresh)
            RefreshQuiver(true);
        OnBladeRemovedEvent?.Invoke(this, blade);
        return true;
    }

    public void ImbueOverTime(SpellCastCharge spell, float ratio) {
        if (quiver == null) return;
        foreach (var blade in quiver) {
            if (blade.item.imbues.Count == 0) continue;
            blade.item.imbues[0].Transfer(spell, blade.item.imbues[0].maxEnergy * ratio * Time.unscaledDeltaTime,
                creature);
        }
    }

    public void MaxImbue(SpellCastCharge spell) {
        if (quiver == null || spell == null) return;
        foreach (var blade in quiver) {
            if (blade.item.colliderGroups.Count == 0 || blade.item.colliderGroups[0].imbue is not Imbue imbue) continue;
            if (imbue.spellCastBase is SpellCastCharge currentSpell
                && currentSpell.hashId != spell.hashId) {
                imbue.SetEnergyInstant(0);
            }

            imbue.Transfer(spell, imbue.maxEnergy, creature);
        }
    }

    public bool TryGetBlade(out Blade blade, bool refresh = true) {
        blade = null;
        if (quiver.Count == 0) return false;
        blade = quiver[quiver.Count - 1];
        return RemoveFromQuiver(blade, refresh);
    }

    public bool TryGetClosestBlade(Vector3 position, out Blade blade) {
        float distance = Mathf.Infinity;
        blade = null;
        for (var i = 0; i < quiver.Count; i++) {
            var thisBlade = quiver[i];
            float thisDistance = (thisBlade.transform.position - position).sqrMagnitude;
            if (!(thisDistance < distance)) continue;
            distance = thisDistance;
            blade = thisBlade;
        }

        if (!blade) return false;

        RemoveFromQuiver(blade);
        return true;
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
        if (creature.brain.isUnconscious != isUnconscious
            || creature.brain.isIncapacitated != isIncapacitated) {
            isIncapacitated = creature.brain.isIncapacitated;
            isUnconscious = creature.brain.isUnconscious;
            RefreshQuiver();
        }
    }

    public void RefreshQuiver(bool changed = false) {
        while (IsFull && Count > Max && Max >= 0) {
            if (TryGetBlade(out var blade, false)) {
                blade.Release();
                changed = true;
            } else {
                break;
            }
        }

        // Fix broken daggers
        for (int i = quiver.Count - 1; i >= 0; i--) {
            var blade = quiver[i];
            if (blade == null || !blade.IsValid) blade.Despawn();
        }

        var newQuiver = new List<Blade>();
        for (var i = 0; i < quiver.Count; i++) {
            if (quiver[i] != null && quiver[i].gameObject != null) {
                newQuiver.Add(quiver[i]);
            }
        }

        quiver = newQuiver;
        for (var i = 0; i < quiver.Count; i++) {
            quiver[i].MoveTo(GetQuiverTarget(i), GetQuiverTarget(i, true));
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

    public bool Fire(Vector3 velocity, out Blade blade, bool aimAssist = false, bool retrieve = true) {
        if (!TryGetBlade(out blade)) return false;
        blade.Release(retrieve);
        blade.isDangerous.Add(Blade.UntilHit);
        blade.AddForce(velocity, ForceMode.VelocityChange, aimAssist, true);
        return true;
    }

    public MoveTarget? GetQuiverTarget(int i, bool alt = false) {
        int count = quiver.Count;
        switch (mode) {
            case Mode.Crown when creature.brain.isChoke || creature.IsBurning:
                var burningPosition = Quaternion.AngleAxis(360f / count * i, Vector3.up)
                                   * new Vector3(0, 0.5f, 0.3f);
                return new MoveTarget(MoveMode.PID, 6)
                    .Parent(creature.ragdoll.targetPart.transform, false)
                    .At(burningPosition)
                    .LookAt(creature.ragdoll.targetPart.transform, true);
            case Mode.Crown when creature.ragdoll.state is Ragdoll.State.Destabilized or Ragdoll.State.Inert:
                return null;
            case Mode.Crown when creature.brain.isUnconscious
                                 || creature.brain.isIncapacitated:
                return new MoveTarget(MoveMode.PID, 2)
                    .Parent(creature.ragdoll.targetPart.transform, false)
                    .At(Quaternion.AngleAxis(360f / count * i, Vector3.up) * new Vector3(1.3f, 0, 0),
                        Quaternion.LookRotation(Vector3.down))
                    .LookAt(creature.brain.isIncapacitated ? Player.local.head.transform : null);
            case Mode.Crown:
                float maxSpread = (float)count / Max * quiverSpread;
                float half = (quiver.Count - 1f) / 2;
                float offset = quiver.Count == 1 ? 0 : (i - half) / half;
                return new MoveTarget(MoveMode.PID, 6)
                    .Parent(creature.ragdoll.targetPart.transform)
                    .At(Quaternion.AngleAxis(offset * maxSpread, Vector3.forward)
                        * new Vector3(-1, 0, alt ? -0.2f : -0.3f))
                    .LookAt(creature.ragdoll.headPart.transform);
            case Mode.Slicer:
                var rotatedPosition = Quaternion.AngleAxis(360f / count * i, Vector3.forward)
                                      * new Vector3(0, 0.15f, 0.05f);
                return new MoveTarget(MoveMode.PID, 12)
                    .Parent(target)
                    .At(rotatedPosition, Quaternion.LookRotation(lookDirection, -rotatedPosition));
            case Mode.Rain:
                var rainPosition = Quaternion.AngleAxis(360f / count * i, Vector3.forward)
                                   * new Vector3(0, 0.3f, 0.5f);
                return new MoveTarget(MoveMode.PID, 6)
                    .Parent(target)
                    .At(rainPosition)
                    .LookAt(target, true);
            case Mode.Blender:
                var blenderPosition = Quaternion.AngleAxis(360f / count * i, Vector3.forward)
                                   * new Vector3(0, SkillVortexBlender.spinDistance, 0);
                return new MoveTarget(MoveMode.Joint, 6)
                    .Parent(target)
                    .At(blenderPosition)
                    .LookAt(target, true, target.forward);
        }

        return default;
    }


    public int Count => quiver.Count;
    
    public static implicit operator Quiver(Creature creature) => Get(creature);
}

