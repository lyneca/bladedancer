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
        var detonation = SkillCatalog.Data<SkillRemoteDetonation>();
        effectData = detonation.explosionEffectData;
        detonation.OnDetonateHitEvent -= OnDetonateHit;
        detonation.OnDetonateHitEvent += OnDetonateHit;
    }

    public override void OnSkillUnloaded(SkillData skillData, Creature creature) {
        base.OnSkillUnloaded(skillData, creature);
        var detonation = SkillCatalog.Data<SkillRemoteDetonation>();
        detonation.OnDetonateHitEvent -= OnDetonateHit;
    }

    private void OnDetonateHit(
        ItemMagicProjectile projectile,
        SpellCastProjectile spell,
        ThunderEntity entity,
        Vector3 closestPoint,
        float distance) {
        if (entity is Item && entity.GetComponent<Blade>() is Blade blade) {
            entity.StartCoroutine(Detonate(blade, Random.Range(minTime, maxTime)));
        }
    }

    public IEnumerator Detonate(Blade blade, float time) {
        blade.AllowDespawn(false);
        yield return new WaitForSeconds(time);
        blade.AllowDespawn(true);
        Explode(blade.transform.position);
        blade.Despawn();
    }

    public void Explode(Vector3 position) {
        effectData.Spawn(position, Quaternion.identity).Play();
        var skill = SkillCatalog.Data<SkillRemoteDetonation>();
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
                    obj.physicBody.AddExplosionForce(skill.force, position, skill.radius, 0.5f, skill.forceMode);
                    break;
            }
        }
    }
}