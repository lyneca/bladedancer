using System;
using System.Collections.Generic;
using ThunderRoad;
using ThunderRoad.Skill.Spell;
using UnityEngine;

namespace Bladedancer.Skills; 

public class SkillGoreTalons : SkillSpellPunch {
    [SkillCategory("Gore Talons", Category.Base | Category.Body, 1)]
    [ModOptionFloatValues(0, 30, 5)]
    [ModOptionSlider, ModOption("Talon Speed", "How fast the talons match your hand movement; higher is faster. Set to 0 for instant.", defaultValueIndex = 6)]
    public static float talonSpeed = 30;
    
    [SkillCategory("Gore Talons", Category.Base | Category.Body, 1)]
    [ModOptionFloatValues(60, 180, 30)]
    [ModOptionSlider, ModOption("Talon Spread Angle", "Angle which the talons are spread over.", defaultValueIndex = 2)]
    public static float talonAngle = 120;

    [SkillCategory("Gore Talons", Category.Base | Category.Body, 1)]
    [ModOptionIntValues(1, 5, 1)]
    [ModOptionSlider, ModOption("Talon Count", "Number of blades that are pulled into the talons.", defaultValueIndex = 2)]
    public static int talonCount = 3;

    [SkillCategory("Gore Talons", Category.Base | Category.Body, 1)]
    [ModOptionFloatValues(0.05f, 0.3f, 0.05f)]
    [ModOptionSlider, ModOption("Talon Distance", "How far away talons lie from your hand.", defaultValueIndex = 1)]
    public static float talonDistance = 0.1f;

    [SkillCategory("Gore Talons", Category.Base | Category.Body, 1)]
    [ModOptionFloatValues(0f, 0.1f, 0.05f)]
    [ModOptionSlider, ModOption("Talon Forward Distance", "How far the talons protrude from your hand.", defaultValueIndex = 1)]
    public static float talonForward = 0.05f;

    protected static bool refreshing;

    public const string TalonList = "TalonList";
    public const string TalonActive = "TalonActive";

    public override void OnSkillLoaded(SkillData skillData, Creature creature) {
        base.OnSkillLoaded(skillData, creature);
        refreshing = false;
    }

    public override void OnFist(PlayerHand hand, bool gripping) {
        base.OnFist(hand, gripping);
        if (!Quiver.TryGet(hand.ragdollHand?.creature, out var quiver)) return;
        Player.currentCreature.SetVariable(TalonActive + hand.side, gripping);
        Refresh(hand.side);
        if (gripping) {
            quiver.OnCountChangeEvent -= OnQuiverCountChange;
            quiver.OnCountChangeEvent += OnQuiverCountChange;
        } else {
            quiver.OnCountChangeEvent -= OnQuiverCountChange;
        }
    }

    private void OnQuiverCountChange(Quiver quiver) {
        Refresh();
    }

    protected void Refresh(Side side) {
        if (refreshing) return;
        var hand = Player.local.GetHand(side);

        if (!Player.currentCreature.TryGetVariable(TalonActive + side, out bool active)) {
            active = false;
            Player.currentCreature.SetVariable(TalonActive + side, false);
        }

        if (!Player.currentCreature.TryGetVariable(TalonList + side, out List<Blade> talons)) {
            talons = new List<Blade>();
            Player.currentCreature.SetVariable(TalonList + side, talons);
        }

        if (active && talons.Count == talonCount) return;
        refreshing = true;

        for (var i = 0; i < talons.Count; i++) {
            if (!talons[i]) continue;
            talons[i].isDangerous.Remove(this);
            talons[i].DespawnOrReturn(Quiver.Main);
        }

        talons.Clear();

        if (!active) {
            refreshing = false;
            return;
        }
        int count = Math.Min(Quiver.Main.Count, talonCount);
        var thumbDir = hand.ragdollHand.transform.InverseTransformDirection(hand.ragdollHand.ThumbDir).normalized;
        var pointDir = hand.ragdollHand.transform.InverseTransformDirection(hand.ragdollHand.PointDir).normalized;
        var palmDir = hand.ragdollHand.transform.InverseTransformDirection(hand.ragdollHand.PalmDir).normalized;

        for (var i = 0; i < count; i++) {
            if (!Quiver.Main.TryGetClosestBlade(hand.ragdollHand.transform.position, out var blade))
                break;
            blade.isDangerous.Add(this);
            var position
                = Quaternion.AngleAxis((i - (count - 1) / 2f) * (talonAngle / (count - 1)), pointDir)
                  * palmDir
                  * -talonDistance
                  + pointDir * talonForward;
            blade.MoveTo(new MoveTarget(MoveMode.Joint, talonSpeed)
                .Parent(hand.ragdollHand.transform)
                .At(position,
                    Quaternion.LookRotation(pointDir, position)));
            talons.Add(blade);
        }
        refreshing = false;
    }

    public void Refresh() {
        for (var i = 0; i < 2; i++) {
            Refresh((Side)i);
        }
    }
}
