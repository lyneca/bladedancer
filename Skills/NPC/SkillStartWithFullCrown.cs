using System;
using System.Collections;
using ThunderRoad;
using ThunderRoad.Skill;
using UnityEngine;

namespace Bladedancer.Skills; 

public class SkillStartWithFullCrown : AISkillData {
    public static float spawnDelay = 0.1f;

    public override void OnSpellLoad(SpellData spell, SpellCaster caster = null) {
        base.OnSpellLoad(spell, caster); 
        if (spell is not SpellCastSlingblade blade || caster == null) return;
        blade.quiver.StartCoroutine(FillCrown(blade.quiver));
    }
    
    public IEnumerator FillCrown(Quiver quiver) {
        while (quiver.creature && !quiver.creature.isKilled && quiver && !quiver.IsFull) {
            yield return new WaitForSeconds(spawnDelay);
            Blade.Spawn((blade, _) => blade.ReturnToQuiver(quiver, true),
                quiver.creature.ragdoll.targetPart.transform.position + Vector3.up,
                Quaternion.LookRotation(Vector3.up, quiver.creature.ragdoll.targetPart.transform.forward),
                quiver.creature, true);
        }
    }
}