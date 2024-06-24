using Bladedancer.Skills;
using JetBrains.Annotations;
using ThunderRoad;
using UnityEngine;

namespace Bladedancer.Misc; 

public class SpellBladeMergeData : SpellMergeData {
    [SkillCategory("General", Category.Base)]
    [ModOptionFloatValues(0.05f, 0.3f, 0.05f)]
    [ModOptionSlider, ModOption("Merge Spawn Rate", "How fast daggers are replenished when using merge skills.", defaultValueIndex = 5)]
    public static float spawnCooldown = 0.3f;
    protected float lastSpawn;

    public SpellCastCharge otherSpellData;
    public bool retrieveOnStart = true;

    private bool started;

    public override void Load(Mana mana) {
        base.Load(mana);
    }

    public override void Merge(bool active) {
        base.Merge(active);
        started = false;
    }

    public virtual void OnLoadOtherSpell(SpellCastCharge other) {
        otherSpellData = other;
    }

    public virtual void OnMergeStart(Quiver quiver) {}

    public void StartMerge(Quiver quiver) {
        if (!mana.casterLeft.isFiring || !mana.casterRight.isFiring || started) return;
        currentCharge = 0;
        started = true;
        for (var i = 0; i < 2; i++) {
            var spell = mana.GetCaster((Side)i).spellInstance;
            switch (spell) {
                case null:
                    continue;
                case SpellCastSlingblade blade:
                    blade.OnCastStop();
                    break;
                default:
                    OnLoadOtherSpell(spell as SpellCastCharge);
                    break;
            }
        }

        quiver.ignoreSelf.Add(this);
        OnMergeStart(quiver);
        SkillDoubleTrouble.InvokeOnMergeStart(this);
        if (!retrieveOnStart) return;
        quiver.RetrieveNearby(true);
    }

    /// <summary>
    /// Call this when you're finished with the merge and want to clean up.
    /// MUST BE CALLED MANUALLY
    /// <param name="quiver">The casting creatures' quiver. May be null.</param>
    /// </summary>
    public virtual void OnMergeEnd([CanBeNull] Quiver quiver) {
        started = false;
        quiver?.ignoreSelf.Remove(this);
        SkillDoubleTrouble.InvokeOnMergeEnd(this);
    }

    public override void Unload() {
        base.Unload();
        started = false;
        SkillDoubleTrouble.InvokeOnMergeEnd(this);
        OnMergeEnd(Quiver.Get(mana.creature));
    }

    public virtual bool AllowImbue(Quiver quiver) => true;
    public virtual bool AllowSpawn(Quiver quiver) => !quiver.IsFull;

    public virtual Quaternion SpawnOrientation => Quaternion.LookRotation(
        Vector3.Slerp(mana.casterLeft.ragdollHand.PointDir, mana.casterRight.ragdollHand.PointDir, 0.5f),
        Vector3.Slerp(mana.casterLeft.ragdollHand.ThumbDir, mana.casterRight.ragdollHand.ThumbDir, 0.5f));
    public virtual void OnTryBladeSpawn(Quiver quiver) {}
    public virtual void OnBladeSpawn(Quiver quiver, Blade blade) {}

    public virtual void OnMergeUpdate(Quiver quiver) {
        if (AllowImbue(quiver) && otherSpellData != null)
            quiver.ImbueOverTime(otherSpellData, 1.5f);
    }

    public override void Update() {
        base.Update();
        if (!mana.mergeActive || !Quiver.TryGet(mana.creature, out var quiver)) return;

        if (!started) StartMerge(quiver);
        if (currentCharge < 0.1f) return;
        if (Time.time - lastSpawn > spawnCooldown && AllowSpawn(quiver)) {
            OnTryBladeSpawn(quiver);
            lastSpawn = Time.time;
            mana.casterLeft.ragdollHand.HapticTick();
            mana.casterRight.ragdollHand.HapticTick();
            Blade.Spawn((spawnedBlade, _) => {
                spawnedBlade.ReturnToQuiver(quiver);
                OnBladeSpawn(quiver, spawnedBlade);
            }, mana.mergePoint.position, SpawnOrientation, mana.creature, true);
        }
        OnMergeUpdate(quiver);
    }
}
