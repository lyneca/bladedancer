using System.Collections.Generic;
using Bladedancer.Misc;
using ThunderRoad;
using ThunderRoad.Skill;
using UnityEngine;

namespace Bladedancer.Skills; 

public class SkillStormVolley : SpellBladeMergeData {
    [SkillCategory("Storm Volley", Category.Base | Category.Lightning, 3)]
    [ModOptionFloatValuesDefault(0.05f, 0.3f, 0.05f, 0.2f)]
    [ModOptionSlider, ModOption("Storm Volley Size", "Distance between daggers in Storm Volley.")]
    public static float size;
    
    [SkillCategory("Storm Volley", Category.Base | Category.Lightning, 3)]
    [ModOptionFloatValuesDefault(1f, 30f, 1f, 8f)]
    [ModOptionSlider, ModOption("Storm Volley Throw Speed", "Throw speed multiplier for Storm Volley throw.")]
    public static float velocityMult;
    
    [SkillCategory("Storm Volley", Category.Base | Category.Lightning, 3)]
    [ModOptionFloatValuesDefault(0f, 2, 0.1f, 0.5f)]
    [ModOptionSlider, ModOption("Storm Volley Throw Spread", "Spread amount multiplier for Storm Volley throw.")]
    public static float spreadMult;

    public static float height = Mathf.Sqrt(3) * size / 2;
    protected Transform centerPoint;

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
    }

    public override void OnMergeUpdate(Quiver quiver) {
        base.OnMergeUpdate(quiver); 
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
