using ThunderRoad;
using UnityEngine;

namespace Bladedancer.Skills; 

public class SkillVortexBlender : SpellMergeData {
    [SkillCategory("Vortex Blender", Category.Base | Category.Gravity, 3)]
    [ModOptionFloatValues(0.05f, 0.3f, 0.05f)]
    [ModOptionSlider, ModOption("Vortex Spawn Rate", "How fast daggers are replenished", defaultValueIndex = 5)]
    public static float spawnCooldown = 0.3f;
    
    protected float lastSpawn;

    public string spellId = "Gravity";
    protected SpellCastCharge spellData;

    protected Transform centerPoint;
    
    protected bool started = false;

    [SkillCategory("Vortex Blender", Category.Base | Category.Gravity, 3)]
    [ModOptionFloatValues(180f, 720, 180f)]
    [ModOptionSlider, ModOption("Vortex Spin Speed", "How fast the vortex spins at base", defaultValueIndex = 1)]
    public static float spinSpeed = 360;

    [SkillCategory("Vortex Blender", Category.Base | Category.Gravity, 3)]
    [ModOptionFloatValues(1, 2, 0.1f)]
    [ModOptionSlider, ModOption("Vortex Radius", "Radius of the vortex", defaultValueIndex = 3)]
    public static float spinDistance = 1.3f;
    protected float spinAmount = 0;
    protected float spinMult = 1;

    public override void Load(Mana mana) {
        base.Load(mana);
        centerPoint = new GameObject().transform;
    }

    public override void Merge(bool active) {
        base.Merge(active);
        if (active || !Quiver.TryGet(mana.creature, out var quiver)) return;
        quiver.SetMode(Mode.Crown);
        SkillDoubleTrouble.InvokeOnMergeEnd(this);
        quiver.preventBlock.Remove(this);
        quiver.ignoreCap.Remove(this);
        started = false;
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
        quiver.preventBlock.Add(this);
        quiver.ignoreCap.Add(this);
        quiver.RetrieveNearby();
        quiver.target = centerPoint;
        quiver.lookDirection = Vector3.forward;
        quiver.SetMode(Mode.Blender, true);
    }

    public override void Unload() {
        base.Unload();
        started = false;
        SkillDoubleTrouble.InvokeOnMergeEnd(this);
        if (!Quiver.TryGet(mana.creature, out var quiver)) return;
        quiver.preventBlock.Remove(this);
        quiver.ignoreCap.Remove(this);
        quiver.SetMode(Mode.Crown);
    }

    public override void Update() {
        base.Update();
        if (!mana.mergeActive) return;
        spinMult = Mathf.Lerp(1, 2,
            Mathf.InverseLerp(0.15f, 0.05f,
                (mana.casterLeft.transform.position - mana.casterRight.transform.position).magnitude));
        spinAmount += spinSpeed * spinMult * Time.deltaTime;
        var forwardDir = Vector3.Slerp(mana.casterLeft.ragdollHand.PointDir, mana.casterRight.ragdollHand.PointDir,
            0.5f);
        var rightDir = mana.casterRight.ragdollHand.transform.position - mana.casterLeft.ragdollHand.transform.position;
        var upDir = Vector3.Cross(rightDir, forwardDir);
        
        centerPoint.transform.SetPositionAndRotation(
            mana.mergePoint.position,
            Quaternion.LookRotation(upDir,
                Quaternion.AngleAxis(spinAmount, upDir)
                * Vector3.ProjectOnPlane(Player.currentCreature.ragdoll.targetPart.transform.forward, upDir)));

        if (!started) OnMergeStart();
        if (currentCharge < 0.1f || !Quiver.TryGet(mana.creature, out var quiver)) return;
        if (Time.time - lastSpawn > spawnCooldown && !quiver.IsFull) {
            quiver.RetrieveNearby(true, 2);

            // Specifically we check whether the quiver is actually full or not, and only spawn blades if so
            // (as opposed to whether quiver.IsFull, which always returns false when quiver.ignoreCap is true)
            if (quiver.Count < quiver.Max) {
                lastSpawn = Time.time;
                mana.casterLeft.ragdollHand.HapticTick();
                mana.casterRight.ragdollHand.HapticTick();
                Blade.Spawn((spawnedBlade, _) => { spawnedBlade.ReturnToQuiver(quiver, true); },
                    mana.mergePoint.position, Quaternion.LookRotation(forwardDir), mana.creature, true);
            }
        }

        quiver.ImbueOverTime(spellData, 1.5f);
    }
}
