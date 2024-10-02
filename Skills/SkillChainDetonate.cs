using System.Collections;
using ThunderRoad;
using ThunderRoad.Skill.Spell;
using UnityEngine;

namespace Bladedancer.Skills;

public class SkillChainDetonate : SkillData {
    public float minTime = 0.5f;
    public float maxTime = 1f;
    public EffectData effectData;

    public override void OnLateSkillsLoaded(SkillData skillData, Creature creature) {
        base.OnLateSkillsLoaded(skillData, creature);
        var detonation = Catalog.GetData<SkillRemoteDetonation>("RemoteDetonation");
        effectData = detonation.explosionEffectData;
        detonation.OnDetonateHitEvent -= OnDetonateHit;
        detonation.OnDetonateHitEvent += OnDetonateHit;
        if (Quiver.TryGet(creature, out var quiver))
            quiver.OnBladeThrow += OnBladeThrow;
    }

    public void OnBladeThrow(Quiver quiver, Blade blade) {
        if (!blade.ImbuedWith("Fire")) return;
        blade.item.OnFlyEndEvent -= OnFlyEnd;
        blade.item.OnFlyEndEvent += OnFlyEnd;
    }

    public void OnFlyEnd(Item item) {
        item.OnFlyEndEvent -= OnFlyEnd;
        if (item.isPenetrating && item.GetComponent<Blade>() is Blade blade) {
            for (var j = 0; j < item.mainCollisionHandler.collisions.Length; j++) {
                var collision = item.mainCollisionHandler.collisions[j];
                if (collision.damageStruct.penetration == DamageStruct.Penetration.None
                    || collision.targetColliderGroup == null
                    || collision.targetColliderGroup.collisionHandler is not
                        { ragdollPart.ragdoll.creature: Creature }) continue;
                ExplodeBlade(blade, 0.5f, true);
                break;
            }
        }
    }
    
    public override void OnSkillUnloaded(SkillData skillData, Creature creature) {
        base.OnSkillUnloaded(skillData, creature);
        var detonation = Catalog.GetData<SkillRemoteDetonation>("RemoteDetonation");
        detonation.OnDetonateHitEvent -= OnDetonateHit;
        if (Quiver.TryGet(creature, out var quiver))
            quiver.OnBladeThrow -= OnBladeThrow;
    }

    private void OnDetonateHit(
        ItemMagicProjectile projectile,
        SpellCastProjectile spell,
        ThunderEntity entity,
        Vector3 closestPoint,
        float distance) {
        if (entity is Item && entity.GetComponent<Blade>() is { InQuiver: false } blade) {
            ExplodeBlade(blade);
        }
    }

    public void ExplodeBlade(Blade blade, float time = -1, bool chain = false) {
        if (time < 0) time = Random.Range(minTime, maxTime);
        blade.StartCoroutine(Detonate(blade, time, chain));
    }

    public IEnumerator Detonate(Blade blade, float time, bool chain = false) {
        blade.AllowDespawn(false);
        yield return new WaitForSeconds(time);
        blade.AllowDespawn(true);
        Explode(blade.transform.position, chain);
        blade.Despawn();
    }

    public void Explode(Vector3 position, bool chain = false) {
        effectData.Spawn(position, Quaternion.identity).Play();
        var skill = Catalog.GetData<SkillRemoteDetonation>("RemoteDetonation");
        foreach (var (thunderEntity, closestPoint) in ThunderEntity.InRadiusClosestPoint(
                     position, skill.radius)) {
            float magnitude = (closestPoint - position).magnitude;
            switch (thunderEntity) {
                case Creature hitEntity:
                    float num = Mathf.InverseLerp(skill.radius / 2, 0.0f, magnitude);
                    if (hitEntity.isPlayer) {
                        hitEntity.Damage(skill.playerDamage / 3 * num);
                        break;
                    }

                    hitEntity.Damage(skill.enemyDamage / 3 * num);
                    if (magnitude < (double)skill.pushMinRadius)
                        hitEntity.TryPush(Creature.PushType.Magic,
                            hitEntity.ragdoll.targetPart.transform.position - position, 1);
                    hitEntity.ragdoll.targetPart.physicBody.AddExplosionForce(skill.force, position, skill.radius, 0.5f,
                        skill.forceMode);
                    break;
                case Item obj:
                    if (chain && obj.GetComponent<Blade>() is { InQuiver: false } blade) ExplodeBlade(blade);
                    obj.physicBody.AddExplosionForce(skill.force, position, skill.radius, 0.5f, skill.forceMode);
                    break;
            }
        }
    }
}