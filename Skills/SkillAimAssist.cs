using ThunderRoad;

namespace Bladedancer.Skills; 

public class SkillAimAssist : SkillData {
    public override void OnSkillLoaded(SkillData skillData, Creature creature) {
        base.OnSkillLoaded(skillData, creature);
        SpellCastBlade.aimAssist = true;
    }

    public override void OnSkillUnloaded(SkillData skillData, Creature creature) {
        base.OnSkillUnloaded(skillData, creature);
        SpellCastBlade.aimAssist = false;
    }
}
