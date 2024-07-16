using ThunderRoad;
using UnityEngine;

namespace Bladedancer.Skills; 

public class SkillCaputMortuum : SkillData {
    public const string FreeCharge = "BladeFreeCharge";

    public string killEffectId;
    protected EffectData killEffectData;

    public override void OnCatalogRefresh() {
        base.OnCatalogRefresh();
        killEffectData = Catalog.GetData<EffectData>(killEffectId);
    }

    public override void OnSkillLoaded(SkillData skillData, Creature creature) {
        base.OnSkillLoaded(skillData, creature); 
        Quiver.Get(creature).OnBladeThrow += OnBladeThrow;
    }

    private void OnBladeThrow(Quiver quiver, Blade blade) {
        blade.OnHitEntity += OnBladeHit;
    }

    private void OnBladeHit(Blade blade, ThunderEntity entity, CollisionInstance hit) {
        if (entity is not Creature { isPlayer: false }
            || hit is not
                { targetColliderGroup: { collisionHandler.ragdollPart.type: RagdollPart.Type.Head } group }) return;
        blade.OnHitEntity -= OnBladeHit;
        killEffectData?.Spawn(hit.contactPoint, Quaternion.identity, group.transform).Play();
        blade.Quiver?.creature.SetVariable(FreeCharge, true);
        blade.item.lastHandler?.HapticTick(1, true);
    }

    public override void OnSkillUnloaded(SkillData skillData, Creature creature) {
        base.OnSkillUnloaded(skillData, creature);
        Quiver.Get(creature).OnBladeThrow -= OnBladeThrow;
    }

    public void OnCreatureKillEvent(Creature creature, Player player, CollisionInstance collisionInstance, EventTime eventTime) {
        if (collisionInstance?.sourceColliderGroup?.collisionHandler?.item?.GetComponent<Blade>() is
            { wasSlung: true, item.lastHandler: RagdollHand hand }) {
            killEffectData?.Spawn(collisionInstance.contactPoint, Quaternion.identity,
                collisionInstance.targetColliderGroup?.transform).Play();
            hand.creature.SetVariable(FreeCharge, true);
            hand.HapticTick(1, true);
        }
    }
}
