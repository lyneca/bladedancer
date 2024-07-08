using ThunderRoad;
using ThunderRoad.Skill;
using ThunderRoad.Skill.Spell;
using UnityEngine;

namespace Bladedancer.Skills;

public class SkillForceBlades : SpellSkillData {
    [SkillCategory("Force Blades", Category.Base | Category.Gravity, 1)]
    [ModOptionFloatValuesDefault(0.0f, 2f, 0.5f, 1.5f)]
    [ModOptionSlider, ModOption("Force Blades Force Multiplier", "How much force is added with Force Blades")]
    public static float multiplier = 1.5f;

    public string hitEffectId;
    protected EffectData hitEffectData;

    public override void OnCatalogRefresh() {
        base.OnCatalogRefresh();
        hitEffectData = Catalog.GetData<EffectData>(hitEffectId);
    }

    public override void OnSkillLoaded(SkillData skillData, Creature creature) {
        base.OnSkillLoaded(skillData, creature);
        if (!Quiver.TryGet(creature, out var quiver)) return;
        quiver.OnBladeThrow -= OnBladeThrow;
        quiver.OnBladeThrow += OnBladeThrow;
    }

    public override void OnSkillUnloaded(SkillData skillData, Creature creature) {
        base.OnSkillUnloaded(skillData, creature);
        if (!Quiver.TryGet(creature, out var quiver)) return;
        quiver.OnBladeThrow -= OnBladeThrow;
    }

    public void OnBladeThrow(Quiver quiver, Blade blade) {
        if (!blade.ImbuedWith("Gravity")) return;
        blade.item.mainCollisionHandler.OnCollisionStartEvent -= CollisionStart;
        blade.item.mainCollisionHandler.OnCollisionStartEvent += CollisionStart;
        return;

        void CollisionStart(CollisionInstance hit) {
            blade.item.mainCollisionHandler.OnCollisionStartEvent -= CollisionStart;
            if (!blade.TryGetImbue("Gravity", out var imbue)) return;
            if (hit.targetColliderGroup == null) return;

            var entity = hit.targetColliderGroup.collisionHandler?.ragdollPart?.ragdoll.creature as ThunderEntity
                         ?? hit.targetColliderGroup.collisionHandler?.item;
            if (entity == null) return;

            hit.targetColliderGroup.collisionHandler?.ragdollPart?.ragdoll.creature?.MaxPush(Creature.PushType.Magic,
                hit.impactVelocity);

            hitEffectData?.Spawn(hit.contactPoint, Quaternion.identity)?.Play();
            entity.AddForce(hit.impactVelocity * multiplier * imbue.EnergyRatio, ForceMode.Impulse,
                hit.targetColliderGroup.collisionHandler);
        }
    }
}