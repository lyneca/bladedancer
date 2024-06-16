using ThunderRoad;
using ThunderRoad.Skill;

namespace Bladedancer.Skills;

public class SkillTwinBladeMaestro : SpellSkillData {
    public static float shootVelocityThreshold = 4f;
    public static float throwMult = 2f;
    public static bool enabled = false;

    public delegate void SkillEnableEvent(SkillData data, Creature creature);

    public static event SkillEnableEvent OnSkillEnableEvent;
    public static event SkillEnableEvent OnSkillDisableEvent;
    
    public override void OnSkillLoaded(SkillData skillData, Creature creature) {
        base.OnSkillLoaded(skillData, creature);
        enabled = true;
        OnSkillEnableEvent?.Invoke(skillData, creature);
    }

    public override void OnSkillUnloaded(SkillData skillData, Creature creature) {
        base.OnSkillUnloaded(skillData, creature);
        enabled = false;
        OnSkillDisableEvent?.Invoke(skillData, creature);
    }
}
