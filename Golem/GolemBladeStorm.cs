using System.Collections.Generic;
using ThunderRoad;
using ThunderRoad.Skill;
using ThunderRoad.Skill.Spell;
using UnityEngine;

namespace Bladedancer; 

public class GolemBladeStorm : GolemBladeAbility {
    public float fireDelay = 0.2f;
    public float lastFired;

    public override void OnQuiverFull() {
        base.OnQuiverFull();
        int count = blades.Count;
        for (var i = 0; i < blades.Count; i++) {
            var rotatedPosition = Quaternion.AngleAxis(360f / count * i, -Vector3.right)
                                  * new Vector3(-1, 0, -2f);
            blades[i].MoveTo(new MoveTarget(MoveMode.PID, 12)
                .Parent(Root)
                .Scale(ScaleMode.FullSize)
                .At(rotatedPosition)
                .LookAt(Root, true));
        }
    }

    public override void OnUpdate() {
        base.OnUpdate();
        if (state != State.Firing || blades.Count == 0 || Time.time - lastFired < fireDelay) return;
        lastFired = Time.time;
        var blade = blades[blades.Count - 1];
        Fire(blade);
        blades.Remove(blade);
    }

    public virtual void Fire(Blade blade) {
        var vector = golem.attackTarget.position - blade.transform.position;
        blade.Release();
        Blade.slung.Add(blade);
        blade.wasSlung = true;
        blade.AddForce(vector.normalized * Mathf.Lerp(25, 50, Mathf.InverseLerp(5, 10, vector.sqrMagnitude)),
            ForceMode.VelocityChange, false, true);
        if (blade.item.GetComponent<Electromagnet>() is Electromagnet magnet)
            magnet.target = Player.currentCreature.ragdoll.targetPart.transform;
    }
}
