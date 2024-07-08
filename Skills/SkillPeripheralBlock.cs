using System;
using System.Collections.Generic;
using System.Linq;
using ThunderRoad;
using ThunderRoad.DebugViz;
using ThunderRoad.Skill;
using ThunderRoad.Skill.Spell;
using UnityEngine;
using Object = UnityEngine.Object;
using Plane = UnityEngine.Plane;

namespace Bladedancer.Skills; 

public class SkillPeripheralBlock : SkillData {
    [SkillCategory("Peripheral Block", Category.Base, 2)]
    [ModOptionFloatValues(2f, 5f, 1f)]
    [ModOptionSlider, ModOption("Block Radius", "Projectile detection radius", defaultValueIndex = 1)]
    public static float radius = 3f;

    [SkillCategory("Peripheral Block", Category.Base, 2)]
    [ModOption("Destroy on Block", "Whether daggers destroy themselves after a successful block", defaultValueIndex = 1)]
    public static bool destroyOnBlock = true;

    public delegate void BlockEvent(SkillPeripheralBlock skill, Blade blade, Item item, CollisionInstance hit);

    public event BlockEvent OnBlockEvent;

    public float minimumVelocity = 3f;
    public float velocityThreatAngle = 40f;

    public override void OnCatalogRefresh() {
        base.OnCatalogRefresh();
        EventManager.onLevelLoad += OnLevelLoad;
    }

    private void OnLevelLoad(LevelData level, LevelData.Mode mode, EventTime time) {
        if (time == EventTime.OnEnd) return;
        trackedItems = new Dictionary<Item, Blade>();
    }

    public override void OnSkillLoaded(SkillData skillData, Creature creature) {
        base.OnSkillLoaded(skillData, creature);
        if (!creature.gameObject.TryGetOrAddComponent<PeripheralBlocker>(out var component))
            return;
        component.skill = this;
        component.OnThreatFound -= OnThreatFound;
        component.OnThreatFound += OnThreatFound;
        component.OnThreatRemoved -= OnThreatRemoved;
        component.OnThreatRemoved += OnThreatRemoved;
    }

    public override void OnSkillUnloaded(SkillData skillData, Creature creature) {
        base.OnSkillUnloaded(skillData, creature);
      if (!creature.gameObject.TryGetComponent<PeripheralBlocker>(out var component))
        return;
      component.Clear();
      component.OnThreatFound -= OnThreatFound;
      component.OnThreatRemoved -= OnThreatRemoved;
      Object.Destroy(component);
    }

    public static Dictionary<Item, Blade> trackedItems = new();

    public void OnThreatFound(Creature creature, Item item) {
        if (trackedItems.ContainsKey(item) || Quiver.Get(creature).preventBlock) return;
        if (SkillDiscombobulate.CreatureStunned(creature)) return;
        if (!Quiver.TryGet(creature, out var quiver)
            || !quiver.TryGetClosestBlade(item.transform.position, out var blade, ItemData.Type.Shield)) return;
        trackedItems[item] = blade;
        var startPos = blade.transform.position;
        var startRot = blade.transform.rotation;
        var startRay = new Ray(item.transform.position, item.Velocity.normalized);
        var startPlane = new Plane(item.Velocity,
            creature.ragdoll.targetPart.transform.position - item.Velocity.normalized * 0.5f);

        startPlane.Raycast(startRay, out float startEnter);
        var direction
            = creature.ragdoll.targetPart.transform.InverseTransformDirection(startRay.GetPoint(startEnter)
                                                                              - blade.transform.position);
        blade.item.mainCollisionHandler.OnCollisionStartEvent -= OnCollision;
        blade.item.mainCollisionHandler.OnCollisionStartEvent += OnCollision;
        blade.MoveTo(new MoveTarget(MoveMode.Joint, 0)
            .At(() => {
                var ray = new Ray(item.transform.position, item.Velocity.normalized);
                var plane = new Plane(item.Velocity,
                    creature.ragdoll.targetPart.transform.position - item.Velocity.normalized * 0.5f);

                plane.Raycast(ray, out float enter);

                return (Vector3.Lerp(startPos, ray.GetPoint(enter), Mathf.InverseLerp(radius, 0.3f, enter)),
                    Quaternion.Slerp(startRot,
                        Quaternion.LookRotation(
                            Vector3.ProjectOnPlane(creature.ragdoll.targetPart.transform.TransformDirection(direction),
                                ray.direction), -item.Velocity), Mathf.InverseLerp(radius, 0.3f, enter)));
            }));
    }

    private void OnCollision(CollisionInstance hit) {
        var source = hit.sourceColliderGroup?.collisionHandler?.item;
        var target = hit.targetColliderGroup?.collisionHandler?.item;
        if (!source || !target) return;
        if (target.GetComponent<Blade>() is Blade targetBlade) TestBlock(targetBlade, source, hit);
        if (source.GetComponent<Blade>() is Blade sourceBlade) TestBlock(sourceBlade, target, hit);
    }

    public void TestBlock(Blade blade, Item item, CollisionInstance hit) {
        if (!trackedItems.TryGetValue(item, out var foundBlade) || foundBlade != blade) return;
        blade.item.mainCollisionHandler.OnCollisionStartEvent -= OnCollision;
        OnBlockEvent?.Invoke(this, blade, item, hit);
        if (!destroyOnBlock) return;
        blade.Release(false);
    }

    public void OnThreatRemoved(Creature creature, Item item) {
        if (!trackedItems.TryGetValue(item, out var blade)) return;
        trackedItems.Remove(item);
        if (!blade || !blade.item) return;
        blade.item.mainCollisionHandler.OnCollisionStartEvent -= OnCollision;
        if (blade.shouldRetrieve)
            blade.ReturnToQuiver(creature);
    }

    public void CleanTrackedItems(Creature creature) {
        var items = trackedItems.ToList();
        for (var i = items.Count - 1; i >= 0; i--) {
            if (items[i].Key != null) continue;
            items[i].Value.ReturnToQuiver(creature);
            trackedItems.Remove(items[i].Key);
        }
    }
}

public class PeripheralBlocker : ThunderBehaviour {
    public Creature creature;
    public SkillPeripheralBlock skill;
    public HashSet<Item> threats;

    public delegate void ThreatEvent(Creature creature, Item item);

    public event ThreatEvent OnThreatFound;
    public event ThreatEvent OnThreatRemoved;

    public override ManagedLoops EnabledManagedLoops => ManagedLoops.Update;

    private void Awake() {
        creature = GetComponent<Creature>();
        threats = new HashSet<Item>();
        creature.OnKillEvent += OnKill;
        creature.OnDespawnEvent += OnDespawn;
    }

    private void OnKill(CollisionInstance hit, EventTime time) {
        if (time == EventTime.OnEnd) return;
        Clear();
    }

    private void OnDespawn(EventTime time) {
        if (time == EventTime.OnEnd) return;
        Clear();
    }

    public bool IsThreat(Item item) {
        if (item == null
            || !item.loaded
            || !item.isFlying
            || !item.isThrowed
            || item.isGripped
            || item.mainHandler != null
            || item.holder != null
            || item.isTelekinesisGrabbed
            || creature?.ragdoll?.targetPart?.transform == null
            || Vector3.Angle(
                creature.ragdoll.targetPart.transform.position - item.transform.position, item.physicBody.velocity)
            >= skill.velocityThreatAngle
            || item.physicBody.velocity.sqrMagnitude
            <= skill.minimumVelocity * skill.minimumVelocity
            || !(item.lastHandler?.creature != creature))
            return false;
        return item.lastHandler != null;
    }

    protected override void ManagedUpdate() {
        base.ManagedUpdate();
        HashSet<Item> thisFrame = new();
        for (var i = 0; i < Item.allThrowed.Count; ++i) {
            var obj = Item.allThrowed[i];
            if (!IsThreat(obj)
                || (transform.position - obj.transform.position).sqrMagnitude > SkillPeripheralBlock.radius * SkillPeripheralBlock.radius) continue;
            thisFrame.Add(obj);
            DetectItem(obj);
        }

        var list = threats.Except(thisFrame).ToList();
        foreach (var item in list) {
            RemoveItem(item);
        }
    }

    public void DetectItem(Item item) {
        if (threats.Add(item))
            OnThreatFound?.Invoke(creature, item);
    }

    public void RemoveItem(Item item) {
        if (threats.Remove(item))
            OnThreatRemoved?.Invoke(creature, item);
    }

    public void Clear() {
        foreach (var threat in threats) {
            OnThreatRemoved?.Invoke(creature, threat);
        }
        threats.Clear();
    }

    private void OnDestroy() {
        Clear();
    }
}
