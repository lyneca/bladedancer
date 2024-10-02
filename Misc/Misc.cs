using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ThunderRoad;
using ThunderRoad.Skill.Spell;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace Bladedancer; 

public class SkillCategory : ModOptionCategory {
    public SkillCategory(string name, Category category, int tier = 0, int skill = 1) : base(name, (int)category << 5 + tier << 3 + skill) {}
    public SkillCategory(string name, int tier = 0, int skill = 0) : base(name, tier << 3 + skill) {}
}

[Flags]
public enum Category {
    Base      = 0b000001,
    Fire      = 0b000010,
    Lightning = 0b000100,
    Gravity   = 0b001000,
    Mind      = 0b010000,
    Body      = 0b100000,
}

public static class Fixes {
    public static ThunderEntity AimAssist(
        Ray ray,
        float maxDistance,
        float maxAngle,
        out Transform targetPoint,
        Func<Creature, bool> filter = null,
        CreatureType weakpointFilter = 0,
        Creature ignoredCreature = null,
        float minDistance = 0.1f) {
        float sqrMinDistance = minDistance * minDistance;
        float sqrMaxDistance = maxDistance * maxDistance;
        float largestRightSqrDistance = Mathf.Infinity;
        ThunderEntity outputCreature = null;
        targetPoint = null;
        for (var i = 0; i < Creature.allActive.Count; i++) {
            var creature = Creature.allActive[i];
            if (creature == null || !(filter?.Invoke(creature) ?? true) || creature == ignoredCreature) continue;

            var toCreature = creature.ragdoll.targetPart.transform.position - ray.origin;

            if (weakpointFilter.HasFlag(creature.data.type) && creature is { weakpoints: { Count: > 0 } }) {
                for (var j = 0; j < creature.weakpoints.Count; j++) {
                    var weakpoint = creature.weakpoints[j];
                    var toWeakpoint = weakpoint.position - ray.origin;
                    if (toWeakpoint.sqrMagnitude > sqrMaxDistance
                        || toWeakpoint.sqrMagnitude < sqrMinDistance
                        || Vector3.Angle(toWeakpoint, ray.direction) > maxAngle) continue;
                    float distance = toWeakpoint.magnitude;
                    float rightSqrDistance
                        = (ray.GetPoint(distance) - weakpoint.position).sqrMagnitude;
                    if (rightSqrDistance >= largestRightSqrDistance) continue;
                    largestRightSqrDistance = rightSqrDistance;
                    outputCreature = creature;
                    targetPoint = weakpoint;
                }
            } else {
                if (toCreature.sqrMagnitude > sqrMaxDistance
                    || toCreature.sqrMagnitude < sqrMinDistance
                    || Vector3.Angle(toCreature, ray.direction) > maxAngle) continue;

                float distance = toCreature.magnitude;
                float rightSqrDistance
                    = (ray.GetPoint(distance) - creature.ClosestPoint(ray.GetPoint(distance))).sqrMagnitude;
                if (rightSqrDistance >= largestRightSqrDistance) continue;
                largestRightSqrDistance = rightSqrDistance;
                outputCreature = creature;
            }
        }

        targetPoint ??= ((Creature)outputCreature)?.ragdoll.targetPart.transform;

        if (ThunderRoad.Golem.local != null || ThunderRoad.Golem.local is not { isKilled: false } golem)
            return outputCreature;
        var toGolem = golem.Center - ray.origin;
        if (weakpointFilter.HasFlag(CreatureType.Golem) && golem.weakpoints is { Count: > 0 }) {
            for (var j = 0; j < golem.weakpoints.Count; j++) {
                var weakpoint = golem.weakpoints[j];
                var toWeakpoint = weakpoint.position - ray.origin;
                if (toWeakpoint.sqrMagnitude > sqrMaxDistance
                    || toWeakpoint.sqrMagnitude < sqrMinDistance
                    || Vector3.Angle(toWeakpoint, ray.direction) > maxAngle) continue;
                float distance = toWeakpoint.magnitude;
                float rightSqrDistance
                    = (ray.GetPoint(distance) - weakpoint.position).sqrMagnitude;
                if (rightSqrDistance >= largestRightSqrDistance) continue;
                largestRightSqrDistance = rightSqrDistance;
                outputCreature = golem;
                targetPoint = weakpoint;
            }
        } else if (toGolem.sqrMagnitude <= sqrMaxDistance
                   && toGolem.sqrMagnitude >= sqrMinDistance
                   && Vector3.Angle(toGolem, ray.direction) <= maxAngle) {

            float distanceGolem = toGolem.magnitude;
            float rightSqrDistanceGolem
                = (ray.GetPoint(distanceGolem) - golem.ClosestPoint(ray.GetPoint(distanceGolem))).sqrMagnitude;
            if (rightSqrDistanceGolem < largestRightSqrDistance) {
                outputCreature = golem;
            }
        }

        return outputCreature;
    }
    
}

public interface IStringable {
    public string Stringify();
}

public static class Misc {
    public static string HexHash(int id) {
        return id.ToString("x8").Substring(2);
    }
}

public class Debug {
    public static string Stringify(object obj) {
        switch (obj) {
            case IStringable stringable:
                return stringable.Stringify();
            case IList argList:
                var list = new List<string>();
                for (var i = 0; i < argList.Count; i++) {
                    list.Add(Stringify(argList[i]));
                }

                return "[ " + string.Join(", ", list) + " ]";
            case IDictionary argDict:
                var pairs = new List<string>();
                foreach (var key in argDict.Keys) {
                    pairs.Add($"{key}: {argDict[key]}");
                }

                return "{ " + string.Join(", ", pairs) + " }";
            default:
                return obj.ToString();
        }
    }

    public static string Stringify(object[] args) {
        var list = new List<string>();
        for (var i = 0; i < args.Length; i++) {
            list.Add(Stringify(args[i]));
        }

        return string.Join(" ", list);
    }

public static void Log(params object[] args) {
        UnityEngine.Debug.Log("[Bladedancer] " + Stringify(args));
    }

    public static void LogWarning(params object[] args) {
        UnityEngine.Debug.LogWarning("[Bladedancer] " + Stringify(args));
    }

    public static void LogException(Exception exception) {
        UnityEngine.Debug.LogException(exception);
    }

    public static void LogError(params object[] args) {
        UnityEngine.Debug.LogError("[Bladedancer] " + Stringify(args));
    }
}

public static class Extension {
    public static void BackupSet<T>(ref T original, ref T backup, T value) {
        backup = original;
        original = value;
    }
    public static Vector3 ForwardVector(this Item item) {
        if (item.flyDirRef) {
            return item.flyDirRef.forward;
        }

        if (item.holderPoint) {
            return item.transform.rotation
                   * Quaternion.Inverse(item.transform.InverseTransformRotation(item.holderPoint.rotation
                       * Quaternion.AngleAxis(180, Vector3.up))) * Vector3.forward;
        }

        return item.transform.up;
    }

    public static ItemData GetBladeItemData(this Creature creature)
        => creature.TryGetVariable(Blade.BladeItem, out ItemData data) ? data : Blade.defaultItemData;

    public static float GetAxis(this Vector3 vector, Axis axis) => axis switch {
        Axis.X => vector.x,
        Axis.Y => vector.y,
        Axis.Z => vector.z,
    };

    public static Vector3 GetUnitAxis(this Transform transform, Axis axis) => axis switch {
        Axis.X => transform.right,
        Axis.Y => transform.up,
        Axis.Z => transform.forward
    };

    public static void FireBoltFixed(
        this SkillThunderbolt bolt,
        Transform start,
        Vector3 direction,
        SpellCastLightning lightning = null,
        float angleMult = 1,
        bool reflect = true,
        Creature caster = null) {
        if (!bolt.TryGetPrivate("impactEffectData", out EffectData impactEffectData)) return;
        if (!bolt.TryGetPrivate("mainBoltEffectData", out EffectData mainBoltEffectData)) return;
        if (!bolt.TryGetPrivate("statusData", out StatusData statusData)) return;
        if (!bolt.TryGetPrivate("statusDuration", out float statusDuration)) return;

        var ray = new Ray(start.position, direction);
        float boltRange = bolt.Range(lightning);

        caster ??= lightning?.spellCaster.ragdollHand.creature;
        var filter = caster?.isPlayer == false
            ? Filter.EnemyOf(caster, false)
            : Filter.NPCs;

        var foundEntity = Creature.AimAssist(ray.origin, ray.direction, boltRange, bolt.angle * angleMult,
            out var targetPoint, filter, CreatureType.Golem);
        lightning?.ResetBoltColor();
        lightning?.ForceChargeSappingLoop();
        var effect = mainBoltEffectData?.Spawn(start);
        effect?.SetMainGradient(lightning?.boltColorOverride ?? bolt.defaultBoltGradient);
        if (lightning != null && lightning.spellCaster?.mana.creature.isPlayer == true)
            effect?.SetHaptic(lightning.spellCaster.side, Catalog.gameData.haptics.telekinesisThrow);

        var targetTransform = new GameObject().transform;
        if (effect != null) {
            effect.SetSource(start);
            effect.SetTarget(targetTransform);
            effect.onEffectFinished += OnFinished;
        }

        ThunderEntity target = null;
        var breakable = targetPoint?.GetComponentInParent<SimpleBreakable>();
        if (foundEntity is not Creature foundCreature) {
            var foundItem = ThunderEntity.AimAssist(ray, boltRange, bolt.angle * angleMult, Filter.Items) as Item;

            RaycastHit rayHit = default;
            var rayDidHit = false;
            if (foundItem == null) {
                rayDidHit = Physics.Raycast(ray, out rayHit, boltRange,
                    bolt.layerMask,
                    QueryTriggerInteraction.Ignore);
                var point = rayDidHit
                    ? rayHit.point
                    : ray.GetPoint(boltRange);
                foundItem = rayHit.rigidbody?.GetComponentInParent<Item>();
                breakable = rayHit.transform?.GetComponentInParent<SimpleBreakable>();
                targetTransform.position = point;
            }

            if (foundItem != null) {
                targetTransform.SetParent(foundItem.transform);
                targetTransform.localPosition = foundItem.GetLocalCenter();
                targetTransform.localRotation = Quaternion.identity;
                target = foundItem;
            } else if (breakable != null) {
                targetTransform.SetParent(breakable.transform);
                targetTransform.localPosition = Vector3.zero;
                targetTransform.localRotation = Quaternion.identity;
            } else if (rayDidHit && reflect && bolt.allowSurfaceBounce) {
                if (effect != null) {
                    effect.Play();
                }

                bolt.FireBoltFixed(targetTransform, Vector3.Reflect(ray.direction, rayHit.normal), lightning, 1, false);

                return;
            }
        } else {
            if (breakable != null) {
                targetTransform.SetParent(breakable.transform);
            }

            targetTransform.SetParent(foundCreature.ragdoll.targetPart != null
                ? foundCreature.ragdoll.targetPart.meshBone.transform
                : foundCreature.RootTransform);
            targetTransform.transform.localPosition = Vector3.zero;

            targetTransform.localRotation = Quaternion.identity;

            target = foundCreature;
        }

        var actualRay = new Ray(start.position, targetTransform.position - start.position);
        if (target is Creature {
                isKilled: false, ragdoll: { state: Ragdoll.State.NoPhysic } ragdoll
            }) ragdoll.SetState(Ragdoll.State.Standing);

        // Test for deflection
        if (Physics.SphereCast(actualRay, 0.1f, out var hit, boltRange, bolt.layerMask, QueryTriggerInteraction.Ignore)) {
            var hitEntity = hit.collider.GetComponentInParent<ThunderEntity>();
            if (hitEntity == null || hitEntity != target) {
                if (hitEntity is Item { IsFree: false } item) {
                    targetTransform.position = hit.point;
                    targetTransform.SetParent(hit.rigidbody.transform);
                    if (effect != null) {
                        effect.Play();
                    }

                    if (reflect) {
                        item.Haptic(1, true);
                        var holdingCreature = item.mainHandler && item.mainHandler.creature.isPlayer
                                              || item.isTelekinesisGrabbed
                            ? Player.currentCreature
                            : null;
                        var reflectDirection = holdingCreature?.isPlayer == true
                            ? Vector3.Lerp(Vector3.Reflect(actualRay.direction, hit.normal),
                                Player.local.head.transform.forward, 0.5f)
                            : Vector3.Reflect(actualRay.direction, hit.normal);
                        bolt.FireBoltFixed(targetTransform, reflectDirection, lightning, 2, false, holdingCreature);
                    } else {
                        impactEffectData?.Spawn(targetTransform).Play();
                    }

                    return;
                }

                target = hitEntity;
            }
        }

        if (breakable) {
            breakable.Hit(5, SimpleBreakable.DamageSource.Thunderbolt);
        } else {
            switch (target) {
                case Item item: {
                    item.AddForce(ray.direction * bolt.itemForce, ForceMode.VelocityChange);
                    if (item.colliderGroups.Count > 0)
                        lightning?.Hit(item.colliderGroups[0], targetTransform.position,
                            targetTransform.position - ray.origin,
                            (targetTransform.position - ray.origin).normalized * 0.2f, 1, true, null, 0);
                    item.Haptic(1, true);
                    if (item.breakable is { contactBreakOnly: false })
                        item.breakable.Explode(bolt.breakForce, item.GetWorldBounds().ClosestPoint(ray.origin), 2, 0,
                            ForceMode.VelocityChange);
                    if (statusData != null)
                        item.Inflict(statusData, bolt, statusDuration);
                    break;
                }
                case Creature creature: {
                    creature.AddForce(ray.direction * bolt.creatureForce, ForceMode.VelocityChange);
                    creature.TryPush(Creature.PushType.Magic, direction, 3);
                    creature.Damage(
                        (creature.isPlayer ? bolt.damagePlayer : bolt.damageNpc)
                        * (lightning?.GetModifier(Modifier.Intensity) ?? 1), DamageType.Lightning);
                    if (creature.isPlayer) {
                        creature.handLeft.playerHand.controlHand.HapticPlayClip(Catalog.gameData.haptics
                            .telekinesisThrow);
                        creature.handRight.playerHand.controlHand.HapticPlayClip(Catalog.gameData.haptics
                            .telekinesisThrow);
                    }

                    lightning?.Hit(creature.ragdoll.targetPart.colliderGroup,
                        creature.ragdoll.targetPart.transform.position, -ray.direction,
                        ray.direction * 0.2f, 1,
                        true);

                    if (statusData != null)
                        creature.Inflict(statusData, statusDuration);
                    break;
                }
            }
        }

        if (effect is { isPlaying: false }) {
            effect.Play();
        }

        impactEffectData?.Spawn(targetTransform).Play();

        if (breakable || lightning == null || caster?.isPlayer != true) return;
        var chainEntities = ThunderEntity.InRadiusNaive(targetTransform.position,
            bolt.chainRadius * lightning.GetModifier(Modifier.Range), Filter.AllButPlayer);
        for (var i = 0; i < chainEntities.Count; i++) {
            switch (chainEntities[i]) {
                case Creature chainCreature:
                    var toCreature = chainCreature.ragdoll.targetPart.transform.position - targetTransform.position;
                    lightning.Hit(chainCreature.ragdoll.targetPart.colliderGroup,
                        chainCreature.ragdoll.targetPart.transform.position, -toCreature.normalized,
                        toCreature.normalized * 0.2f, 1,
                        true);
                    lightning.PlayBolt(chainCreature.ragdoll.targetPart.transform, targetTransform,
                        gradient: lightning.boltColorOverride);
                    break;
                case Item { IsFree: true, hasMetal: true } hitItem:
                    var toItem = hitItem.transform.position - ray.origin;
                    var group = hitItem.metalColliderGroups[Random.Range(0, hitItem.metalColliderGroups.Count)];
                    lightning.Hit(group, group.transform.position, -toItem.normalized, toItem.normalized * 0.2f,
                        1, true);
                    lightning.PlayBolt(group.transform, targetTransform.transform,
                        gradient: lightning.boltColorOverride);

                    break;
            }
        }

        return;

        void OnFinished(EffectInstance instance) {
            if (targetTransform == null) return;
            Object.Destroy(targetTransform.gameObject, 0.2f);
            targetTransform = null;
        }
    }

    public static bool TryGetPrivate<T>(this object obj, string name, out T value) {
        value = default;
        if (obj == null) return false;
        if (obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance) is not FieldInfo info) {
            Debug.LogWarning($"Could not find field {name} on type {obj.GetType()}");
            return false;
        }

        var data = info.GetValue(obj);
        if (data != null)
            if (data is not T) {
                Debug.LogWarning(
                    $"Could not get value of field {info.Name} on object {obj}. Result was {data} ({data.GetType()})");
                return false;
            }

        value = data is T output ? output : default;
        return true;
    }
}

public enum Axis {
    X, Y, Z
}

