using ThunderRoad;
using ThunderRoad.Skill;
using UnityEngine;

namespace Bladedancer.Skills; 

public class SkillSeekerTwin : SpellSkillData {
    [SkillCategory("Seeker Twin", Category.Base, 2)]
    [ModOptionFloatValues(0, 1, 0.1f)]
    [ModOptionSlider, ModOption("Seeker Twin Delay", "How long after a hit before the twin fires", defaultValueIndex = 2)]
    public static float delay = 0.2f;
    
    protected float lastHit;

    public override void OnSpellLoad(SpellData spell, SpellCaster caster = null) {
        base.OnSpellLoad(spell, caster);
        if (spell is not SpellCastSlingblade slingblade) return;
        slingblade.OnHitEntityEvent -= OnHitEntity;
        slingblade.OnHitEntityEvent += OnHitEntity;
    }

    public override void OnSpellUnload(SpellData spell, SpellCaster caster = null) {
        base.OnSpellUnload(spell, caster);
        if (spell is SpellCastSlingblade slingblade) {
            slingblade.OnHitEntityEvent -= OnHitEntity;
        }
    }

    private void OnHitEntity(SpellCastSlingblade spell, Blade blade, ThunderEntity entity, CollisionInstance hit) {
        if (entity is not Creature creature) return;
        if (Time.realtimeSinceStartup - lastHit < delay
            || creature == spell.spellCaster.mana.creature
            || creature.isKilled
            || hit.sourceColliderGroup?.collisionHandler?.item?.GetComponent<Blade>()?.wasSlung != true
            || !spell.quiver.FireAtCreature(creature)) return;
        lastHit = Time.realtimeSinceStartup;
    }
}
