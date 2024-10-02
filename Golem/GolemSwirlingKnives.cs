using ThunderRoad;
using UnityEngine;

namespace Bladedancer; 

public class GolemSwirlingKnives : GolemBladeAbility {
    protected override void OnDeployed() {
        base.OnDeployed(); 
        int count = blades.Count;
        for (int i = count - 1; i >= 0; i--) {
            var blade = blades[i];
            blade.CancelMovement();
            blade.AddForce(
                (blade.transform.position - golem.transform.position + golem.transform.forward * 0.5f).normalized
                * 10, ForceMode.VelocityChange);
            blade.Wander(Player.local.transform.position, 5);
            blade.RunAfter(() => blade.Release(false, 1f), 10f);
        }
    }
}
