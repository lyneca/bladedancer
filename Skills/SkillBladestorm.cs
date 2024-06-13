using ThunderRoad;
using UnityEngine;

namespace Bladedancer.Skills;

public class SkillBladestorm : SpellMergeData {
    [SkillCategory("Bladestorm", Category.Base, 3)]
    [ModOptionFloatValues(0.05f, 0.3f, 0.05f)]
    [ModOptionSlider, ModOption("Bladestorm Spawn Rate", "How fast Bladestorm replenishes daggers", defaultValueIndex = 5)]
    public static float spawnCooldown = 0.3f;
    
    [SkillCategory("Bladestorm", Category.Base, 3)]
    [ModOptionFloatValues(0.05f, 0.2f, 0.05f)]
    [ModOptionSlider, ModOption("Bladestorm Fire Rate", "How fast Bladestorm fires daggers", defaultValueIndex = 1)]
    public static float shootCooldown = 0.15f;

    public float sprayDaggerMinHandAngle = 20f;
    protected float lastSpawn;
    protected float lastShoot;

    public float spinSpeed = 360;
    protected float spinAmount = 0;
    protected float spinMult = 1;

    private bool started = false;

    protected Transform centerPoint;

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
        started = false;
    }
    
    public override void Merge(bool active) {
        base.Merge(active);
        if (active) {
        } else {
            started = false;
            Quiver.Get(mana.creature).SetMode(Mode.Crown);
            Quiver.Get(mana.creature).ignoreSelf.Remove(this);
            SkillDoubleTrouble.InvokeOnMergeEnd(this);
        }
    }

    public void OnMergeStart() {
        if (!mana.casterLeft.isFiring || !mana.casterRight.isFiring) return;
        started = true;
        currentCharge = 0;
        (mana.casterLeft.spellInstance as SpellCastSlingblade)?.OnCastStop();
        (mana.casterRight.spellInstance as SpellCastSlingblade)?.OnCastStop();
        if (Quiver.TryGet(mana.creature, out var quiver)) {
            quiver.RetrieveNearby(true);
            quiver.target = centerPoint.transform;
            quiver.lookDirection = Vector3.forward;
            quiver.SetMode(Mode.Slicer, true);
            quiver.ignoreSelf.Add(this);
        }

        SkillDoubleTrouble.InvokeOnMergeStart(this);
    }

    public override void Unload() {
        base.Unload();
        Quiver.Get(mana.creature).SetMode(Mode.Crown);
        Quiver.Get(mana.creature).ignoreSelf.Remove(this);
        SkillDoubleTrouble.InvokeOnMergeEnd(this);
    }

    public override void Update() {
        base.Update();
        spinAmount += spinSpeed * spinMult * Time.deltaTime;
        var pointDir = Vector3.Slerp(mana.casterLeft.ragdollHand.PointDir, mana.casterRight.ragdollHand.PointDir, 0.5f);
        var thumbDir = Vector3.Slerp(mana.casterLeft.ragdollHand.ThumbDir, mana.casterRight.ragdollHand.ThumbDir, 0.5f);
        centerPoint.transform.SetPositionAndRotation(
            Vector3.Slerp(mana.casterLeft.magicSource.position, mana.casterRight.magicSource.position, 0.5f),
            Quaternion.LookRotation(pointDir, Quaternion.AngleAxis(spinAmount, pointDir) * thumbDir));

        if (!mana.mergeActive) return;
        if (!started) {
            OnMergeStart();
        }

        spinMult = 1;
        var ray = new Ray(mana.mergePoint.position,
            Vector3.Slerp(mana.casterLeft.magicSource.up, mana.casterRight.magicSource.up, 0.5f));
        float leftAngle = Vector3.SignedAngle(ray.direction, mana.casterLeft.magicSource.up,
            Vector3.Cross(mana.creature.centerEyes.position - ray.origin,
                mana.casterLeft.magicSource.position - ray.origin).normalized);
        float rightAngle = Vector3.SignedAngle(ray.direction, mana.casterRight.magicSource.up,
            Vector3.Cross(mana.casterRight.magicSource.position - ray.origin,
                mana.creature.centerEyes.position - ray.origin).normalized);

        if (leftAngle > -sprayDaggerMinHandAngle || rightAngle < sprayDaggerMinHandAngle) {
            // Not spraying daggers
            if (Time.time - lastSpawn < spawnCooldown || Quiver.Get(mana.creature).IsFull) return;
            lastSpawn = Time.time;
            mana.casterLeft.ragdollHand.HapticTick();
            mana.casterRight.ragdollHand.HapticTick();
            Blade.Spawn((blade, _) => {
                blade.ReturnToQuiver(mana.creature, true);
            }, mana.mergePoint.position, Quaternion.LookRotation(pointDir), mana.creature, true);
            return;
        }

        // Spraying daggers
        spinMult = 2;
        if (currentCharge <= 0.8f || Time.time - lastShoot < shootCooldown || Quiver.Get(mana.creature).Count == 0) return;
        lastShoot = Time.time;
        Quiver.Get(mana.creature).Fire(ray.direction * velocity, out _, false, false);
        mana.casterLeft.ragdollHand.HapticTick();
        mana.casterRight.ragdollHand.HapticTick();
    }
}
