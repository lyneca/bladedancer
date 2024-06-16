using Bladedancer.Misc;
using ThunderRoad;
using UnityEngine;

namespace Bladedancer.Skills;

public class SkillBladestorm : SpellBladeMergeData {
    [SkillCategory("Bladestorm", Category.Base, 3)]
    [ModOptionFloatValuesDefault(0.05f, 0.2f, 0.05f, 0.15f)]
    [ModOptionSlider, ModOption("Bladestorm Fire Rate", "How fast Bladestorm fires daggers")]
    public static float shootCooldown;

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
        quiver.SetMode(Mode.Slicer, true);
    }

    public override void OnMergeEnd(Quiver quiver) {
        base.OnMergeEnd(quiver);
        quiver?.SetMode(Mode.Crown);
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
