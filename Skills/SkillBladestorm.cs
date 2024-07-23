using System.Collections;
using System.Collections.Generic;
using Bladedancer.Misc;
using ThunderRoad;
using ThunderRoad.Skill.Spell;
using UnityEngine;

namespace Bladedancer.Skills;

public class SkillBladestorm : SpellBladeMergeData {
    [SkillCategory("Bladestorm", Category.Base, 3)]
    [ModOptionFloatValues(0.05f, 0.2f, 0.05f)]
    [ModOptionSlider, ModOption("Bladestorm Fire Rate", "How fast Bladestorm fires daggers")]
    public static float shootCooldown = 0.15f;
    
    public float sprayDaggerMinHandAngle = 20f;
    protected float lastShoot;

    public float spinSpeed = 360;
    protected float spinAmount = 0;
    protected float spinMult = 1;

    protected Transform centerPoint;
    protected bool spraying;

    public float velocity = 20f;

    public override void OnSkillLoaded(SkillData skillData, Creature creature) {
        base.OnSkillLoaded(skillData, creature);
        Quiver.GetMaxCountHandler(creature).Add(this, 1.5f);
    }

    public override void OnSkillUnloaded(SkillData skillData, Creature creature) {
        base.OnSkillUnloaded(skillData, creature);
        Quiver.GetMaxCountHandler(creature).Remove(this);
    }

    public override void Load(Mana mana) {
        base.Load(mana);
        centerPoint = new GameObject().transform;
    }
    
    public override void Merge(bool active) {
        base.Merge(active);
        spraying = false;
        if (active) return;
        OnMergeEnd(Quiver.Get(mana.creature));
    }

    public override void OnMergeStart(Quiver quiver) {
        base.OnMergeStart(quiver);
        spraying = false;
        quiver.target = centerPoint;
        quiver.lookDirection = Vector3.forward;
        quiver.SetMode(Mode.Slicer);
    }

    public override void OnMergeEnd(Quiver quiver) {
        base.OnMergeEnd(quiver);
        quiver?.SetMode(Mode.Crown);
    }

    public override void Throw(Vector3 velocity) {
        base.Throw(velocity);
        mana.StartCoroutine(ThrowRoutine(velocity));
    }

    public IEnumerator ThrowRoutine(Vector3 velocity) {
        var center = new GameObject("ArcOfDaeKvir");
        var rigidbody = center.AddComponent<Rigidbody>();
        rigidbody.freezeRotation = true;
        rigidbody.drag = SkillArcOfDaeKvir.drag;
        rigidbody.useGravity = false;
        
        rigidbody.AddForce(velocity * SkillArcOfDaeKvir.force, ForceMode.VelocityChange);
        var collider = center.AddComponent<SphereCollider>();
        collider.radius = 0.5f;
        center.layer = GameManager.GetLayer(LayerName.PlayerLocomotion);
        
        center.transform.position = Vector3.Slerp(mana.casterLeft.magicSource.position,
            mana.casterRight.magicSource.position, 0.5f);

        if (!Quiver.TryGet(mana.creature, out var quiver)) yield break;
        quiver.Fill();

        var arcBlades = new List<Blade>();
        int count = quiver.blades.Count;
        for (int i = quiver.blades.Count - 1; i >= 0; i--) {
            var blade = quiver.blades[i];
            quiver.RemoveFromQuiver(blade, false);
            var rotation = Quaternion.AngleAxis(360f * i / count, Vector3.up);
            var lookRotation = Quaternion.AngleAxis(360f * i / count - 60f, Vector3.up);

            blade.MoveTo(new MoveTarget(MoveMode.Joint, 0)
                .Parent(center.transform)
                .At(rotation * Vector3.right * SkillArcOfDaeKvir.radius + Vector3.up * Random.Range(-0.2f, 0.2f),
                    Quaternion.LookRotation(lookRotation * -Vector3.forward, Vector3.up)));

            arcBlades.Add(blade);
        }

        var lightningBlades = new List<Blade>();
        SpellCastLightning lightning = null;
        if (mana.creature.TryGetSkill("TeslaWires", out SkillTeslaWires teslaWires)) {
            for (var i = 0; i < arcBlades.Count; i++) {
                if (!arcBlades[i].ImbuedWith("Lightning")) continue;
                lightningBlades.Add(arcBlades[i]);
                if (lightning != null) continue;
                for (var j = 0; j < arcBlades[i].item.imbues.Count; j++) {
                    if (arcBlades[i].item.imbues[j].spellCastBase is not SpellCastLightning imbuedSpell) continue;
                    lightning = imbuedSpell;
                    break;
                }
            }
            
            float duration = teslaWires.duration;
            teslaWires.duration = SkillArcOfDaeKvir.duration;
            if (lightning != null && lightningBlades.Count > 1) {
                for (var i = 1; i < lightningBlades.Count; i++) {
                    var blade = lightningBlades[i];
                    var prevBlade = lightningBlades[i - 1];
                    var pos = blade.item.transform.position;
                    blade.transform.position = prevBlade.transform.position;
                    teslaWires.OnBolt(lightning, prevBlade.item, blade.item,
                        new CollisionInstance { contactPoint = prevBlade.transform.position });
                    blade.transform.position = pos;
                }
            }

            if (lightning != null && lightningBlades.Count > 2) {
                var blade = lightningBlades[0];
                var prevBlade = lightningBlades[lightningBlades.Count - 1];
                var pos = blade.item.transform.position;
                blade.transform.position = prevBlade.transform.position;
                teslaWires.OnBolt(lightning, prevBlade.item, blade.item,
                    new CollisionInstance { contactPoint = prevBlade.transform.position });
                blade.transform.position = pos;
            }
            teslaWires.duration = duration;
        }

        quiver.RefreshQuiver();

        float startTime = Time.time;
        while (Time.time - startTime < SkillArcOfDaeKvir.duration) {
            center.transform.rotation = Quaternion.AngleAxis(Time.time * SkillArcOfDaeKvir.speed, Vector3.up);
            yield return 0;
        }

        for (var i = 0; i < arcBlades.Count; i++) {
            if (arcBlades[i]) arcBlades[i].Release(false);
        }
    }

    public override bool AllowImbue(Quiver quiver) => false;
    public override bool AllowSpawn(Quiver quiver) => base.AllowSpawn(quiver) && !spraying;

    public override void Update() {
        base.Update();
        spinAmount += spinSpeed * spinMult * Time.deltaTime;
        var pointDir = Vector3.Slerp(mana.casterLeft.ragdollHand.PointDir, mana.casterRight.ragdollHand.PointDir, 0.5f);
        var thumbDir = Vector3.Slerp(mana.casterLeft.ragdollHand.ThumbDir, mana.casterRight.ragdollHand.ThumbDir, 0.5f);
        centerPoint.transform.SetPositionAndRotation(
            Vector3.Slerp(mana.casterLeft.magicSource.position, mana.casterRight.magicSource.position, 0.5f),
            Quaternion.LookRotation(pointDir, Quaternion.AngleAxis(spinAmount, pointDir) * thumbDir));

        if (!mana.mergeActive) return;

        spinMult = 1;

        var ray = new Ray(centerPoint.transform.position, pointDir);

        float leftAngle = Vector3.SignedAngle(ray.direction, mana.casterLeft.ragdollHand.PointDir,
            Vector3.Cross(mana.creature.centerEyes.position - ray.origin,
                mana.casterLeft.magicSource.position - ray.origin).normalized);
        float rightAngle = Vector3.SignedAngle(ray.direction, mana.casterRight.ragdollHand.PointDir,
            Vector3.Cross(mana.casterRight.magicSource.position - ray.origin,
                mana.creature.centerEyes.position - ray.origin).normalized);
        
        spraying = leftAngle < -sprayDaggerMinHandAngle && rightAngle > sprayDaggerMinHandAngle;

        if (!spraying) return;

        // Spraying daggers
        spinMult = 2;
        if (currentCharge <= 0.8f
            || Time.time - lastShoot < shootCooldown
            || Quiver.Get(mana.creature).Count == 0) return;
        lastShoot = Time.time;
        Quiver.Get(mana.creature).Fire(ray.direction * velocity, out _, false, false);
        mana.casterLeft.ragdollHand.HapticTick();
        mana.casterRight.ragdollHand.HapticTick();
    }
}
