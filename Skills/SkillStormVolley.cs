using System.Collections.Generic;
using Bladedancer.Misc;
using ThunderRoad;
using ThunderRoad.Skill;
using ThunderRoad.Skill.Spell;
using UnityEngine;

namespace Bladedancer.Skills; 

public class SkillStormVolley : SpellBladeMergeData {
    [SkillCategory("Storm Volley", Category.Base | Category.Lightning, 3)]
    [ModOptionFloatValues(0.05f, 0.3f, 0.05f)]
    [ModOptionSlider, ModOption("Storm Volley Size", "Distance between daggers in Storm Volley.")]
    public static float size = 0.2f;
    
    [SkillCategory("Storm Volley", Category.Base | Category.Lightning, 3)]
    [ModOptionFloatValues(1f, 30f, 1f)]
    [ModOptionSlider, ModOption("Storm Volley Throw Speed", "Throw speed multiplier for Storm Volley throw.")]
    public static float velocityMult = 8f;
    
    [SkillCategory("Storm Volley", Category.Base | Category.Lightning, 3)]
    [ModOptionFloatValues(0f, 2, 0.1f)]
    [ModOptionSlider, ModOption("Storm Volley Throw Spread", "Spread amount multiplier for Storm Volley throw.")]
    public static float spreadMult = 0.5f;
    
    [SkillCategory("Storm Volley", Category.Base | Category.Lightning, 3)]
    [ModOptionFloatValues(1f, 3f, 0.1f)]
    [ModOptionSlider, ModOption("Storm Volley Size (Spraying)", "Multiplier on the distance between daggers in Storm Volley while spraying.")]
    public static float spraySpreadMult = 2f;
    
    [SkillCategory("Storm Volley", Category.Base | Category.Lightning, 3)]
    [ModOptionFloatValues(0.5f, 2f, 0.1f)]
    [ModOptionSlider, ModOption("Storm Volley Player Distance (Spraying)", "How far the daggers float from the player while spraying")]
    public static float sprayDistanceFromPlayer = 0.6f;
    
    [SkillCategory("Storm Volley", Category.Base | Category.Lightning, 3)]
    [ModOptionFloatValues(0f, 1, 0.1f)]
    [ModOptionSlider, ModOption("Storm Volley Thunderbolt Delay Min", "Min time between thunderbolts")]
    public static float thunderboltDelayMin = 0.8f;
    
    [SkillCategory("Storm Volley", Category.Base | Category.Lightning, 3)]
    [ModOptionFloatValues(0f, 1, 0.1f)]
    [ModOptionSlider, ModOption("Storm Volley Thunderbolt Delay Max", "Max time between thunderbolts")]
    public static float thunderboltDelayMax = 0.2f;
    
    public float thunderboltMinAngle = 20f;
    public float lastThunderbolt;
    public float sprayStartTime;
    public int lastFireIndex;

    public static float height = Mathf.Sqrt(3) * size / 2;
    protected Transform centerPoint;
    public bool spraying;

    public List<Transform> transforms;

    public override void OnCatalogRefresh() {
        base.OnCatalogRefresh();
        if (!ModOptions.TryGetOption("Storm Volley Size", out var option)) return;
        height = Mathf.Sqrt(3) * size / 2;
        option.ValueChanged += _ => {
            height = Mathf.Sqrt(3) * size / 2;
            Quiver.Main?.RefreshQuiver();
        };
    }

    public override void Load(Mana mana) {
        base.Load(mana);
        transforms = new List<Transform>();
        centerPoint = new GameObject().transform;
    }

    public override void Merge(bool active) {
        base.Merge(active);
        if (active) return;
        OnMergeEnd(Quiver.Get(mana.creature));
    }

    public override void Throw(Vector3 velocity) {
        base.Throw(velocity);
        if (!Quiver.TryGet(mana.creature, out var quiver) || quiver.Count == 0) return;
        if (mana.creature.TryGetSkill("TeslaWires", out SkillTeslaWires teslaWires) & quiver.blades.Count > 0 && otherSpellData is SpellCastLightning lightning) {
            for (var i = 0; i < quiver.blades.Count - 1; i++) {
                var pos = quiver.blades[i + 1].item.transform.position;
                quiver.blades[i + 1].transform.position = quiver.blades[i].transform.position;
                teslaWires.OnBolt(lightning, quiver.blades[i].item, quiver.blades[i + 1].item,
                    new CollisionInstance { contactPoint = quiver.blades[i].transform.position });
                quiver.blades[i + 1].transform.position = pos;
            }
        }

        var blades = new List<Blade>(quiver.blades);
        Quiver.TrianglePos(blades.Count - 1, out int maxRow, out _, out _);
        var midPoint = centerPoint.transform.TransformPoint(maxRow * height / 2 * Vector3.forward);
        for (var i = 0; i < blades.Count; i++) {
            var blade = blades[i];
            quiver.Fire(blade, (velocity.normalized + (blade.transform.position - midPoint) * spreadMult) * velocity.magnitude * velocityMult, false);
        }
    }

    public override void OnMergeStart(Quiver quiver) {
        base.OnMergeStart(quiver);
        quiver.target = centerPoint;
        quiver.preventBlock.Add(this);
        quiver.SetMode(Mode.Volley);
        hapticCurveModifier = 0.5f;
    }

    public override void OnMergeUpdate(Quiver quiver) {
        base.OnMergeUpdate(quiver);

        if (quiver.Count == 0 || !mana.creature.TryGetSkill("Thunderbolt", out SkillThunderbolt thunderbolt)) return;
        var pointDir = Vector3.Slerp(mana.casterLeft.ragdollHand.PointDir, mana.casterRight.ragdollHand.PointDir, 0.5f);
        var thumbDir = Vector3.Slerp(mana.casterLeft.ragdollHand.ThumbDir, mana.casterRight.ragdollHand.ThumbDir, 0.5f);

        var ray = new Ray(centerPoint.transform.position, pointDir);

        float leftAngle = Vector3.SignedAngle(ray.direction, mana.casterLeft.ragdollHand.PointDir,
            Vector3.Cross(mana.creature.centerEyes.position - ray.origin,
                mana.casterLeft.magicSource.position - ray.origin).normalized);
        float rightAngle = Vector3.SignedAngle(ray.direction, mana.casterRight.ragdollHand.PointDir,
            Vector3.Cross(mana.casterRight.magicSource.position - ray.origin,
                mana.creature.centerEyes.position - ray.origin).normalized);

        bool wasSpraying = spraying;
        spraying = leftAngle < -thunderboltMinAngle && rightAngle > thunderboltMinAngle;
        if (spraying != wasSpraying) {
            quiver.SetMode(spraying ? Mode.VolleySpraying : Mode.Volley);
            hapticCurveModifier = spraying ? 0.1f : 0.5f;
        }

        if (!spraying) return;

        if (!wasSpraying && spraying) {
            sprayStartTime = Time.time;
        }

        if (Time.time - lastThunderbolt
            < Mathf.Lerp(thunderboltDelayMin, thunderboltDelayMax,
                Mathf.InverseLerp(0, 3, Time.time - sprayStartTime))) return;

        lastFireIndex = ++lastFireIndex % quiver.Count;

        while (transforms.Count < quiver.Count) {
            transforms.Add(new GameObject().transform);
        }

        lastThunderbolt = Time.time;
        transforms[lastFireIndex].transform.position = quiver.blades[lastFireIndex].transform.position
                                                       + pointDir * 0.3f;
        thunderbolt.FireBoltFixed(transforms[lastFireIndex], pointDir);
        if (lastFireIndex % 2 == 0) mana.casterLeft.ragdollHand.HapticTick();
        else mana.casterRight.ragdollHand.HapticTick();
    }

    public override void OnMergeEnd(Quiver quiver) {
        base.OnMergeEnd(quiver); 
        quiver?.SetMode(Mode.Crown);
        quiver?.preventBlock.Remove(this);
    }

    public override Quaternion SpawnOrientation => Quaternion.LookRotation(
        Vector3.Slerp(mana.casterLeft.ragdollHand.ThumbDir, mana.casterRight.ragdollHand.ThumbDir, 0.5f),
        Vector3.Slerp(-mana.casterLeft.ragdollHand.PointDir, -mana.casterRight.ragdollHand.PointDir, 0.5f));

    public override void Update() {
        base.Update();
        if (!mana.mergeActive) return;

        var pointDir = Vector3.Slerp(mana.casterLeft.ragdollHand.PointDir, mana.casterRight.ragdollHand.PointDir, 0.5f);
        var thumbDir = Vector3.Slerp(mana.casterLeft.ragdollHand.ThumbDir, mana.casterRight.ragdollHand.ThumbDir, 0.5f);

        centerPoint.transform.SetPositionAndRotation(
            Vector3.Slerp(mana.casterLeft.magicSource.position, mana.casterRight.magicSource.position, 0.5f),
            Quaternion.LookRotation(pointDir, thumbDir));
    }
}
