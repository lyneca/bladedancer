using ThunderRoad;

namespace Bladedancer.Skills; 

public class SkillArcOfDaeKvir : SkillData {
    [SkillCategory("Arc of Dae'Kvir", Category.Base, 3, 2)]
    [ModOptionFloatValuesDefault(1, 40, 1, 10)]
    [ModOptionSlider, ModOption("Arc of Dae'Kvir Duration", "How long Arc of Dae'Kvir lasts")]
    public static float duration;
    
    [SkillCategory("Arc of Dae'Kvir", Category.Base, 3, 2)]
    [ModOptionFloatValuesDefault(0.3f, 3, 0.1f, 1)]
    [ModOptionSlider, ModOption("Arc of Dae'Kvir Radius", "Blade orbit radius")]
    public static float radius;
    
    [SkillCategory("Arc of Dae'Kvir", Category.Base, 3, 2)]
    [ModOptionFloatValuesDefault(0, 10, 0.5f, 3f)]
    [ModOptionSlider, ModOption("Arc of Dae'Kvir Drag", "How fast the dagger cloud slows down after throw")]
    public static float drag;
    
    [SkillCategory("Arc of Dae'Kvir", Category.Base, 3, 2)]
    [ModOptionFloatValuesDefault(0, 720, 10, 360)]
    [ModOptionSlider, ModOption("Arc of Dae'Kvir Speed", "Blade orbit speed")]
    public static float speed;
    
    [SkillCategory("Arc of Dae'Kvir", Category.Base, 3, 2)]
    [ModOptionFloatValuesDefault(0, 20, 1, 5)]
    [ModOptionSlider, ModOption("Arc of Dae'Kvir Force", "Blade cloud throw force")]
    public static float force;
    
    public override void OnLateSkillsLoaded(SkillData skillData, Creature creature) {
        base.OnLateSkillsLoaded(skillData, creature);
        if (creature.TryGetSkill("Bladestorm", out SkillBladestorm bladestorm))
        {
            bladestorm.allowThrow = true;
        }
    }

    public override void OnSkillUnloaded(SkillData skillData, Creature creature) {
        base.OnSkillUnloaded(skillData, creature);
        if (creature.TryGetSkill("Bladestorm", out SkillBladestorm bladestorm))
        {
            bladestorm.allowThrow = false;
        }
    }
}
