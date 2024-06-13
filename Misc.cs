using System;
using ThunderRoad;
namespace Bladedancer; 

public class SkillCategory : ModOptionCategory {
    public SkillCategory(string name, Category category, int tier = 0, int skill = 0) : base(name, (int)category << 5 + tier << 3 + skill) {}
    public SkillCategory(string name, int tier = 0, int skill = 0) : base(name, tier << 3 + skill) {}
}

[Flags]
public enum Category {
    Base      = 0b000001,
    Fire      = 0b000010,
    Lightning = 0b000100,
    Gravity   = 0b001000,
    Mind      = 0b010000,
    Body      = 0b100000,
}
