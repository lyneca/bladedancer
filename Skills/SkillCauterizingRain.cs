using System.Collections;
using ThunderRoad;
using UnityEngine;

namespace Bladedancer.Skills; 

public class SkillCauterizingRain : SpellMergeData {
    [SkillCategory("Cauterizing Rain", Category.Base | Category.Fire, 3)]
    [ModOptionFloatValues(0.05f, 0.3f, 0.05f)]
    [ModOptionSlider, ModOption("Cauterizing Spawn Rate", "How fast daggers are replenished", defaultValueIndex = 5)]
    public static float spawnCooldown = 0.3f;
    protected float lastSpawn;

    public string spellId = "Fire";
    protected SpellCastCharge spellData;

    protected Transform centerPoint;
    
    protected bool started = false;

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
            started = false;
        } else {
            quiver.SetMode(Mode.Crown);
            SkillDoubleTrouble.InvokeOnMergeEnd(this);
            started = false;
        }
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
                blade.isDangerous.Add(Blade.UntilHit);
                blade.AddForce(
                    (blade.transform.position - quiver.creature.ragdoll.headPart.transform.position).normalized
                    * 15f, ForceMode.VelocityChange, false, true);
            }

            yield return new WaitForSeconds(0.1f);
        }

        SkillDoubleTrouble.InvokeOnMergeEnd(this);
        quiver.SetMode(Mode.Crown);
    }

    public void OnMergeStart() {
        if (!mana.casterLeft.isFiring || !mana.casterRight.isFiring) return;
        currentCharge = 0;
        started = true;
        for (var i = 0; i < 2; i++) {
            var spell = mana.GetCaster((Side)i).spellInstance;
            if (spell == null) continue;
            if (spell.id == spellId) {
                spellData = spell as SpellCastCharge;
            } else if (spell is SpellCastSlingblade blade) {
                blade.OnCastStop();
            }
        }
        SkillDoubleTrouble.InvokeOnMergeStart(this);
        if (!Quiver.TryGet(mana.creature, out var quiver)) return;
        quiver.RetrieveNearby(true);
        quiver.target = centerPoint;
        quiver.lookDirection = Vector3.forward;
        quiver.SetMode(Mode.Rain, true);
    }

    public override void Unload() {
        base.Unload();
        started = false;
        SkillDoubleTrouble.InvokeOnMergeEnd(this);
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
        
        if (!started) OnMergeStart();
        if (currentCharge < 0.1f || !Quiver.TryGet(mana.creature, out var quiver)) return;
        if (Time.time - lastSpawn > spawnCooldown && !quiver.IsFull) {
            lastSpawn = Time.time;
            mana.casterLeft.ragdollHand.HapticTick();
            mana.casterRight.ragdollHand.HapticTick();
            Blade.Spawn((spawnedBlade, _) => {
                spawnedBlade.ReturnToQuiver(quiver, true);
            }, mana.mergePoint.position, Quaternion.LookRotation(Vector3.up), mana.creature, true);
        }

        quiver.ImbueOverTime(spellData, 1.5f);
    }
}
