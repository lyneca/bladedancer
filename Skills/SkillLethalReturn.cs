using ThunderRoad;
using UnityEngine;

namespace Bladedancer.Skills; 

public class SkillLethalReturn : SkillData {
    public const string FreeCharge = "BladeFreeCharge";
    public override void OnSkillLoaded(SkillData skillData, Creature creature) {
        base.OnSkillLoaded(skillData, creature); 
        EventManager.onCreatureKill -= OnCreatureKillEvent;
        EventManager.onCreatureKill += OnCreatureKillEvent;
    }

    public override void OnSkillUnloaded(SkillData skillData, Creature creature) {
        base.OnSkillUnloaded(skillData, creature);
        EventManager.onCreatureKill -= OnCreatureKillEvent;
    }

    public void OnCreatureKillEvent(Creature creature, Player player, CollisionInstance collisionInstance, EventTime eventTime) {
        if (collisionInstance?.sourceColliderGroup?.collisionHandler?.item?.GetComponent<Blade>() is
            { wasSlung: true, item.lastHandler: RagdollHand hand })
            hand.creature.SetVariable(FreeCharge, true);
    }
}
