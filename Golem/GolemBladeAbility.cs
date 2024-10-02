using System;
using System.Collections.Generic;
using ThunderRoad;
using ThunderRoad.Skill;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Bladedancer; 

public class GolemBladeAbility : GolemAbility {
    [SkillCategory("Golem", Category.Base)]
    [ModOption("Golem Abilities", "Disable to turn off custom Golem abilities.")]
    public static bool enable = true;
    public static float lastBladeTime;
    public float bladeCooldownDuration = 10f;
    public float maxDistance = 10f;
    public float startMaxAngle = 50;
    public float quiverSpread = 60f;
    public Vector2 duration = new(8, 12);
    
    public SpellCastCharge spell;
    public Dictionary<Golem.Tier, string[]> spellIds;
    public Dictionary<Golem.Tier, string[]> skillIds;

    public Dictionary<Golem.Tier, int> maxBladeCount = new() {
        { Golem.Tier.Tier1, 3 },
        { Golem.Tier.Tier2, 6 },
        { Golem.Tier.Tier3, 12 },
        { Golem.Tier.Any, 12 },
    };
    
    public List<SpellSkillData> skills;
    
    public float currentDuration;
    public State state;

    public override bool HeadshotInterruptable => true;

    public T GetTier<T>(Dictionary<Golem.Tier, T> dict, GolemController golem) {
        if (dict.TryGetValue(golem.tier, out var value)) return value;
        if (dict.TryGetValue(Golem.Tier.Any, out value)) return value;
        if (dict.TryGetValue(Golem.Tier.Tier3, out value)) return value;
        if (dict.TryGetValue(Golem.Tier.Tier2, out value)) return value;
        if (dict.TryGetValue(Golem.Tier.Tier1, out value)) return value;
        return default;
    }

    public override bool Allow(GolemController golem)
        => base.Allow(golem)
           && enable
           && Time.time - lastBladeTime > bladeCooldownDuration
           && golem.IsSightable(golem.attackTarget, maxDistance, startMaxAngle);

    public List<Blade> blades;
    private float lastBladeSpawn;
    private float bladeSpawnDelay = 0.1f;

    public Collider[] golemColliders;

    public override void Begin(GolemController golem) {
        base.Begin(golem);
        golemColliders = golem.GetComponentsInChildren<Collider>();
        blades = new List<Blade>();
        currentDuration = Random.Range(duration.x, duration.y);
        golem.Deploy(currentDuration, OnDeployStart, OnDeployed, OnDeployEnd);
        state = State.Deploying;

        var spells = new HashSet<string>();
        foreach (var kvp in spellIds) {
            if (golem.tier != Golem.Tier.Any && kvp.Key != Golem.Tier.Any && !golem.tier.HasFlagNoGC(kvp.Key)) continue;
            for (var i = 0; i < kvp.Value.Length; i++) {
                spells.Add(kvp.Value[i]);
            }
        }

        var skillIdSet = new HashSet<string>();
        foreach (var kvp in skillIds) {
            if (golem.tier != Golem.Tier.Any && kvp.Key != Golem.Tier.Any && !golem.tier.HasFlagNoGC(kvp.Key)) continue;
            for (var i = 0; i < kvp.Value.Length; i++) {
                skillIdSet.Add(kvp.Value[i]);
            }
        }

        var skillIdList = new List<string>(skillIdSet);

        if (new List<string>(spells).RandomChoice() is string spellId) {
            spell = Catalog.GetData<SpellCastCharge>(spellId);
        }

        skills = new List<SpellSkillData>();
        for (var i = 0; i < skillIdList.Count; i++) {
            if (Catalog.GetData<SkillData>(skillIdList[i]) is SpellSkillData data)
                skills.Add(data);
        }
    }

    protected virtual void OnDeployStart() {
    }

    protected virtual void OnDeployed() {
        state = State.Firing;
    }

    protected virtual void OnDeployEnd() {
        End();
    }

    public override void OnEnd() {
        base.OnEnd();
        for (var i = 0; i < blades.Count; i++) {
            if (blades[i] != null)
                blades[i].Release(false);
        }
        blades.Clear();
    }

    public Transform Root => golem.headRenderer is SkinnedMeshRenderer renderer
        ? renderer.rootBone.transform
        : golem.headRenderer.transform;

    public override void OnUpdate() {
        base.OnUpdate();
        if (state != State.Deploying
            || blades.Count >= GetTier(maxBladeCount, golem)
            || Time.time - lastBladeSpawn < bladeSpawnDelay) return;

        lastBladeSpawn = Time.time;
        Blade.Spawn(OnBladeSpawn, golem.headRenderer.transform.position + Vector3.up,
            Quaternion.LookRotation(Vector3.up, golem.headRenderer.transform.forward), null);
    }

    public virtual void OnBladeSpawn(Blade blade) {
        blades.Add(blade);
        blade.MaxImbue(spell, null, skills);
        for (var i = 0; i < golemColliders.Length; i++) {
            blade.IgnoreCollider(golemColliders[i]);
        }

        blade.item.OnDespawnEvent += OnBladeDespawn;

        Refresh();
        if (state != State.Deploying || blades.Count != GetTier(maxBladeCount, golem)) return;
        state = State.Waiting;
        OnQuiverFull();
        return;

        void OnBladeDespawn(EventTime time) {
            if (time != EventTime.OnStart) return;
            blade.item.OnDespawnEvent -= OnBladeDespawn;
            for (var i = 0; i < golemColliders.Length; i++) {
                if (golemColliders[i])
                    blade.IgnoreCollider(golemColliders[i], false);
            }
        }
    }


    public virtual void OnQuiverFull() {}

    public virtual void Refresh() {
        int count = blades.Count;
        for (var i = 0; i < blades.Count; i++) {
            float maxSpread = (float)count / GetTier(maxBladeCount, golem) * quiverSpread;
            float half = (blades.Count - 1f) / 2;
            float offset = blades.Count == 1 ? 0 : (i - half) / half;
            blades[i].MoveTo(new MoveTarget(MoveMode.PID, 6)
                .Parent(Root)
                .At(Quaternion.AngleAxis(offset * maxSpread, -Vector3.right)
                    * new Vector3(0, 0, -2.5f))
                .Scale(ScaleMode.Scaled)
                .LookAt(Root));
        }
    }
}

public enum State {
    Deploying,
    Waiting,
    Firing
}
