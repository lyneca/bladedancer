using ThunderRoad;
using UnityEngine;
using UnityEngine.Serialization;

namespace Bladedancer; 

public class GolemBladeRain : GolemBladeAbility {
    [SkillCategory("Golem", Category.Base)]
    [ModOptionFloatValues(0.1f, 2f, 0.1f)]
    [ModOptionSlider, ModOption("Golem Blade Rain Velocity", "How fast daggers are fired from the Golem's Blade Rain ability.")]
    public float velocityMult = 0.5f;
    
    public float fireDelay = 0.2f;
    public float lastBladeFire;

    public override void OnUpdate() {
        base.OnUpdate();
        if (state == State.Firing && Time.time - lastBladeFire > fireDelay && blades.Count > 0) {
            lastBladeFire = Time.time;
            var blade = blades[blades.Count - 1];
            blades.Remove(blade);
            blade.CancelMovement();
            blade.AddForce(
                Vector3.up * 15f
                + Vector3.ProjectOnPlane(golem.attackTarget.position - Root.position, Vector3.up).normalized * 5,
                ForceMode.VelocityChange, false, true);
            Blade.slung.Add(blade);
            blade.wasSlung = true;
            blade.RunAfter(() => OnBladeDelay(blade), 1f);
        }
    }

    public void OnBladeDelay(Blade blade) {
        if (golem.attackTarget == null) {
            blade.Release();
        }

        var vector = golem.attackTarget.transform.position - blade.transform.position;
        blade.AddForce(vector.normalized * 50f * velocityMult, ForceMode.VelocityChange, false, true);
    }
}
