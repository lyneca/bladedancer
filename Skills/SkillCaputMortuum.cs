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
        Quiver.Get(creature).OnBladeHit += OnBladeHit;
    }

    private void OnBladeHit(Blade blade, ThunderEntity entity, CollisionInstance hit) {
        if (entity is not Creature { isPlayer: false }
            || hit is not
                { targetColliderGroup: { collisionHandler.ragdollPart.type: RagdollPart.Type.Head } group }) return;
        killEffectData?.Spawn(hit.contactPoint, Quaternion.identity, group.transform).Play();
        blade.Quiver?.creature.SetVariable(FreeCharge, true);
        blade.item.lastHandler?.HapticTick(1, true);
    }

    public override void OnSkillUnloaded(SkillData skillData, Creature creature) {
        base.OnSkillUnloaded(skillData, creature);
        Quiver.Get(creature).OnBladeHit -= OnBladeHit;
    }
}
