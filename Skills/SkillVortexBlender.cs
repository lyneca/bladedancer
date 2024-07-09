using Bladedancer.Misc;
using ThunderRoad;
using UnityEngine;

namespace Bladedancer.Skills;

public class SkillVortexBlender : SpellBladeMergeData {
    protected Transform centerPoint;
    [SkillCategory("Vortex Blender", Category.Base | Category.Gravity, 3)]
    [ModOptionFloatValuesDefault(180f, 720, 180f, 360f)]
    [ModOptionSlider, ModOption("Vortex Spin Speed", "How fast the vortex spins at base")]
    public static float spinSpeed;

    [SkillCategory("Vortex Blender", Category.Base | Category.Gravity, 3)]
    [ModOptionFloatValuesDefault(1, 2, 0.1f, 1.3f)]
    [ModOptionSlider, ModOption("Vortex Radius", "Radius of the vortex")]
    public static float spinDistance;

    protected float spinAmount = 0;
    protected float spinMult = 1;

    public string vortexEffectId;
    public EffectData vortexEffectData;
    
    protected EffectInstance vortexEffect;
    protected Quaternion slowRotation;

    public override void OnCatalogRefresh() {
        base.OnCatalogRefresh();
        vortexEffectData = Catalog.GetData<EffectData>(vortexEffectId);
    }

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
        OnMergeEnd(quiver);
    }

    public override void OnMergeStart(Quiver quiver) {
        base.OnMergeStart(quiver);
        
        quiver.preventBlock.Add(this);
        quiver.ignoreCap.Add(this);
        quiver.target = centerPoint;
        // quiver.lookDirection = Vector3.forward;
        quiver.SetMode(Mode.Blender, true);
        vortexEffect?.End();
        vortexEffect = vortexEffectData.Spawn(mana.mergePoint);
        vortexEffect.Play();
        
        var forwardDir = Vector3.Slerp(mana.casterLeft.ragdollHand.PointDir, mana.casterRight.ragdollHand.PointDir,
            0.5f);
        var rightDir = mana.casterRight.ragdollHand.transform.position - mana.casterLeft.ragdollHand.transform.position;
        var upDir = Vector3.Cross(rightDir, forwardDir);
        slowRotation = Quaternion.LookRotation(upDir,
            Quaternion.AngleAxis(spinAmount, upDir)
            * Vector3.ProjectOnPlane(Player.currentCreature.ragdoll.targetPart.transform.forward, upDir));
    }

    public override void OnMergeEnd(Quiver quiver) {
        base.OnMergeEnd(quiver);
        vortexEffect?.End();
        vortexEffect = null;
    }

    public override void Unload() {
        base.Unload();
        if (!Quiver.TryGet(mana.creature, out var quiver)) return;
        quiver.preventBlock.Remove(this);
        quiver.ignoreCap.Remove(this);
        vortexEffect?.End();
    }

    public override void OnTryBladeSpawn(Quiver quiver) {
        base.OnTryBladeSpawn(quiver);
        quiver.RetrieveNearby(true, 2);
    }

    public override bool AllowSpawn(Quiver quiver) {
        // Specifically we check whether the quiver is actually full or not, and only spawn blades if so
        // (as opposed to whether quiver.IsFull, which always returns false when quiver.ignoreCap is true)
        return quiver.Count < quiver.Max;
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

        slowRotation = Quaternion.Slerp(slowRotation, centerPoint.transform.rotation, Time.deltaTime);
        vortexEffect?.SetIntensity(Mathf.InverseLerp(0, 30,
            Quaternion.Angle(slowRotation, centerPoint.transform.rotation)));
    }
}