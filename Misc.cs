using System;
using ThunderRoad;
using UnityEngine;

namespace Bladedancer; 

public class SkillCategory : ModOptionCategory {
    public SkillCategory(string name, Category category, int tier = 0, int skill = 1) : base(name, (int)category << 5 + tier << 3 + skill) {}
    public SkillCategory(string name, int tier = 0, int skill = 0) : base(name, tier << 3 + skill) {}
}

public class ModOptionFloatValuesDefault : ModOptionFloatValues {
    public float defaultValue;
    public ModOptionFloatValuesDefault(float start, float end, float step, float defaultValue) : base(start, end, step) {
        this.defaultValue = Mathf.Clamp(defaultValue, start, end);
    }

    public override void Process() {
        base.Process();
        modOption.defaultValueIndex = Mathf.FloorToInt((defaultValue - startRange) / step);
    }
}

public class ModOptionIntValuesDefault : ModOptionIntValues {
    public int defaultValue;
    public ModOptionIntValuesDefault(int start, int end, int step, int defaultValue) : base(start, end, step) {
        this.defaultValue = Mathf.Clamp(defaultValue, start, end);
    }

    public override void Process() {
        base.Process();
        modOption.defaultValueIndex = Mathf.FloorToInt((defaultValue - startRange) / (float)step);
    }
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

public static class Extension {
    public static float GetAxis(this Vector3 vector, Axis axis) => axis switch {
        Axis.X => vector.x,
        Axis.Y => vector.y,
        Axis.Z => vector.z
    };

    public static Vector3 GetUnitAxis(this Transform transform, Axis axis) => axis switch {
        Axis.X => transform.right,
        Axis.Y => transform.up,
        Axis.Z => transform.forward
    };
}

public enum Axis {
    X, Y, Z
}

