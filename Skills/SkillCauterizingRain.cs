using System.Collections;
using Bladedancer.Misc;
using ThunderRoad;
using UnityEngine;
using UnityEngine.PlayerLoop;

namespace Bladedancer.Skills; 

public class SkillCauterizingRain : SpellBladeMergeData {
    protected Transform centerPoint;

    public float spinSpeed = 360;
    protected float spinAmount = 0;
    protected float spinMult = 1;

    public override void Load(Mana mana) {
        base.Load(mana);
        centerPoint = new GameObject().transform;
    }

    public override void Merge(bool active) {
        base.Merge(active);
        if (active || !Quiver.TryGet(mana.creature, out var quiver)) return;
        var vector3 = Player.local.transform.rotation * PlayerControl.GetHand(Side.Left).GetHandVelocity();
        var from = Player.local.transform.rotation * PlayerControl.GetHand(Side.Right).GetHandVelocity();
        if (currentCharge > minCharge
                   && vector3.magnitude > SpellCaster.throwMinHandVelocity
                   && from.magnitude > SpellCaster.throwMinHandVelocity
                   && Vector3.Angle(vector3, mana.casterLeft.magicSource.position - mana.mergePoint.position) < 45
                   && Vector3.Angle(from, mana.casterRight.magicSource.position - mana.mergePoint.position) < 45) {
            quiver.StartCoroutine(FireAll(quiver));
        } else {
            OnMergeEnd(quiver);
        }
    }

    public override void OnMergeEnd(Quiver quiver) {
        base.OnMergeEnd(quiver);
        quiver?.SetMode(Mode.Crown);
    }

    public IEnumerator FireAll(Quiver quiver) {
        while (quiver.Count > 0) {
            var creature = Creature.InRadiusNaive(quiver.transform.position, 10f, Filter.EnemyOf(quiver.creature))
                .RandomChoice();
            if (creature)
                quiver.FireAtCreature(creature);
            else {
                if (!quiver.TryGetBlade(out var blade, false)) continue;
                blade.Release(false);
                blade.AddForce(
                    (blade.transform.position - quiver.creature.ragdoll.headPart.transform.position).normalized
                    * 15f, ForceMode.VelocityChange, false, true);
            }

            yield return new WaitForSeconds(0.1f);
        }
        OnMergeEnd(quiver);
    }

    public override void OnMergeStart(Quiver quiver) {
        base.OnMergeStart(quiver);
        quiver.target = centerPoint;
        quiver.lookDirection = Vector3.forward;
        quiver.SetMode(Mode.Rain);
    }

    public override Quaternion SpawnOrientation => Quaternion.LookRotation(Vector3.up,
        Vector3.Slerp(mana.casterLeft.ragdollHand.PointDir, mana.casterRight.ragdollHand.PointDir, 0.5f));

    public override void Unload() {
        base.Unload();
        if (!Quiver.TryGet(mana.creature, out var quiver)) return;
        quiver.SetMode(Mode.Crown);
    }

    public override void Update() {
        base.Update();
        if (!mana.mergeActive) return;
        spinAmount += spinSpeed * spinMult * Time.deltaTime;
        
        centerPoint.transform.SetPositionAndRotation(
            Player.local.head.transform.position,
            Quaternion.LookRotation(Vector3.up,
                Quaternion.AngleAxis(spinAmount, Vector3.up)
                * Vector3.ProjectOnPlane(Player.local.head.transform.forward, Vector3.up)));
    }
}
