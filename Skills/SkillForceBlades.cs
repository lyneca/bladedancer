using ThunderRoad;
using ThunderRoad.Skill;
using ThunderRoad.Skill.Spell;
using UnityEngine;

namespace Bladedancer.Skills;

public class SkillForceBlades : SpellSkillData {
    [SkillCategory("Force Blades", Category.Base | Category.Gravity, 1)]
    [ModOptionFloatValues(0.05f, 0.3f, 0.05f)]
    [ModOptionSlider, ModOption("Force Multiplier", "How fast Bladestorm replenishes daggers", defaultValueIndex = 5)]
    public static float multiplier = 1.5f;
    public int spellHashId;

    public override void OnCatalogRefresh() {
        base.OnCatalogRefresh();
        spellHashId = Catalog.GetData<SpellCastGravity>("Gravity").hashId;
    }

    public override void OnImbueLoad(SpellData spell, Imbue imbue) {
        base.OnImbueLoad(spell, imbue);
        if (spell.hashId != spellHashId
            || imbue.colliderGroup.collisionHandler.item.GetComponent<Blade>() == null) return;
        imbue.OnImbueHit -= OnHit;
        imbue.OnImbueHit += OnHit;
    }

    public override void OnImbueUnload(SpellData spell, Imbue imbue) {
        base.OnImbueUnload(spell, imbue);
        imbue.OnImbueHit -= OnHit;
    }

    public void OnHit(SpellCastCharge spell, float amount, bool fired, CollisionInstance hit, EventTime time) {
        if (hit.sourceColliderGroup?.collisionHandler?.item.GetComponent<Blade>()?.isDangerous != true) return;
        (hit.targetColliderGroup?.collisionHandler?.Entity as Creature)?.MaxPush(Creature.PushType.Magic,
            hit.impactVelocity);
        hit.targetColliderGroup?.collisionHandler?.Entity?.AddForce(hit.impactVelocity * multiplier, ForceMode.Impulse,
            hit.targetColliderGroup.collisionHandler);
    }
}