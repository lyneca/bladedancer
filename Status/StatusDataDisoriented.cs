using ThunderRoad;

namespace Bladedancer; 

public class StatusDataDisoriented : StatusData {
    [SkillCategory("Psyblades", Category.Mind, 2)]
    [ModOptionFloatValues(0, 1, 0.1f)]
    [ModOptionSlider, ModOption("Disoriented Strength Multiplier", "Attack strength multiplier on disoriented enemies")]
    public static float strengthMult = 0.5f;
    
    [SkillCategory("Psyblades", Category.Mind, 2)]
    [ModOptionFloatValues(0, 1, 0.1f)]
    [ModOptionSlider, ModOption("Disoriented Trip Chance", "Chance for a disoriented enemy to trip when they dodge an attack")]
    public static float tripChance = 0.4f;

    [SkillCategory("Psyblades", Category.Mind, 2)]
    [ModOptionFloatValues(0, 5, 0.1f)]
    [ModOptionSlider, ModOption("Disoriented Inaccuracy Amount", "How inaccurate disoriented enemies with ranged weapons become. Bigger is less accurate.")]
    public static float inaccuracy = 2f;
    
    [SkillCategory("Psyblades", Category.Mind, 2)]
    [ModOptionFloatValues(0, 1, 0.1f)]
    [ModOptionSlider, ModOption("Disoriented Target Switch Chance", "Chance for a disoriented enemy to switch targets.")]
    public static float targetSwitchChance = 0.4f;
    
    [SkillCategory("Psyblades", Category.Mind, 2)]
    [ModOptionFloatValues(0, 5, 0.1f)]
    [ModOptionSlider, ModOption("Disoriented Target Switch Delay", "Delay between checks for a disoriented enemy to switch targets.")]
    public static float targetSwitchDelay = 3f;
}
