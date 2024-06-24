using ThunderRoad;
using UnityEngine;
using UnityEngine.Serialization;

namespace Bladedancer; 

public class GolemBladeRain : GolemBladeAbility {
    public float fireDelay = 0.2f;
    public float lastBladeFire;

    protected override void OnDeployed() {
        base.OnDeployed();
    }

    public override void OnUpdate() {
        base.OnUpdate();
        if (state == State.Firing && Time.time - lastBladeFire > fireDelay) {
            lastBladeFire = Time.time;
            var blade = blades[blades.Count - 1];
            blades.Remove(blade);
            blade.CancelMovement();
            blade.AddForce(
                Vector3.up * 15f
                + Vector3.ProjectOnPlane(golem.attackTarget.position - Root.position, Vector3.up).normalized * 5,
                ForceMode.VelocityChange, false, true);
            blade.RunAfter(() => OnBladeDelay(blade), 1f);
        }
    }

    public void OnBladeDelay(Blade blade) {
        if (golem.attackTarget == null) {
            blade.Release();
        }

        var vector = golem.attackTarget.transform.position - blade.transform.position;
        blade.AddForce(vector.normalized * 50f, ForceMode.VelocityChange, false, true);
    }
}
