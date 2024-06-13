using ThunderRoad;
using ThunderRoad.Skill.Spell;

namespace Bladedancer.Skills;

public class SkillBladePunch : SkillSpellPunch {
    public override void OnPunchHit(RagdollHand hand, CollisionInstance hit, bool fist) {
        base.OnPunchHit(hand, hit, fist);
        if (!fist) return;
        if (hit.targetColliderGroup?.collisionHandler?.ragdollPart?.ragdoll.creature is {
                isPlayer: false
            } creature) {
            hand.creature.GetComponent<Quiver>()?.FireAtCreature(creature);
        }
    }
}