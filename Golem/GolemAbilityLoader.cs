using System.Collections;
using System.Collections.Generic;
using ThunderRoad;
using ThunderRoad.Modules;

namespace Bladedancer; 

public class GolemAbilityLoader : GameModeModule {
    public List<GolemAbility> abilities;

    public override IEnumerator OnLoadCoroutine() {
        yield return base.OnLoadCoroutine();
        Golem.OnLocalGolemSet += OnLocalGolemSet;
    }

    private void OnLocalGolemSet() {
        if (abilities != null)
            Golem.local.abilities.AddRange(abilities);
    }
}

