using ThunderRoad;
using ThunderRoad.Skill;

namespace Bladedancer.Skills; 

public class SkillCustomBlade : AISkillData {
    public string itemId;
    public override void OnSkillLoaded(SkillData skillData, Creature creature) {
        base.OnSkillLoaded(skillData, creature);
        creature.SetVariable(Blade.BladeItem, Catalog.GetData<ItemData>(itemId));
    }

    public override void OnSkillUnloaded(SkillData skillData, Creature creature) {
        base.OnSkillUnloaded(skillData, creature);
        creature.SetVariable(Blade.BladeItem, Blade.defaultBladeId);
    }
}
