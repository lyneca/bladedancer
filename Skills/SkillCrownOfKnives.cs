using ThunderRoad;
using ThunderRoad.Skill;

namespace Bladedancer.Skills; 

public class SkillCrownOfKnives : SpellSkillData {
    public const string HasCrown = "HasCrown";

    public override void OnSkillLoaded(SkillData skillData, Creature creature) {
        base.OnSkillLoaded(skillData, creature);
        creature.SetVariable(HasCrown, true);
    }

    public override void OnSkillUnloaded(SkillData skillData, Creature creature) {
        base.OnSkillUnloaded(skillData, creature);
        creature.SetVariable(HasCrown, false);
        if (Quiver.TryGet(creature, out var quiver))
            quiver.DumpAll();
    }
}
