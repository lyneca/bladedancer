using System.Collections.Generic;
using ThunderRoad;
using ThunderRoad.DebugViz;
using UnityEngine;

namespace Bladedancer; 

public class CustomWristStats : ThunderBehaviour {
    [SkillCategory("Crown of Knives", Category.Base, 2)]
    [ModOptionSlider, ModOption("Show Crown UI On Wrist", "Show or hide the crown wrist UI.")]
    public static bool allowEnable = true;
    public override ManagedLoops EnabledManagedLoops => ManagedLoops.Update;

    public Creature creature;
    public Dictionary<Blade, GameObject> indicators;
    public List<GameObject> indicatorList;

    private WristStats stats;
    private RagdollHand hand;
    private Quiver quiver;
    private bool isShown;

    public static int emissionPropertyID = Shader.PropertyToID("_BaseColor");

    private void Awake() {
        if (ModOptions.TryGetOption("Show Crown UI On Wrist", out ModOption option)) {
            option.ValueChanged += OnChanged;
        }

        creature = GetComponentInParent<Creature>();
        quiver = Quiver.Get(creature);
        stats = GetComponent<WristStats>();
        creature.OnDespawnEvent += OnCreatureDespawn;
        indicators = new Dictionary<Blade, GameObject>();
        indicatorList = new List<GameObject>();
    }

    public CustomWristStats SetHand(RagdollHand hand) {
        this.hand = hand;
        return this;
    }

    private void OnChanged(object obj) {
        if (obj is not bool enabled) return;
        if (enabled) Refresh(creature);
        else SetActive(false);
    }

    protected override void ManagedUpdate() {
        base.ManagedUpdate();
        if (stats.isShown != isShown && (!stats.isShown || allowEnable))
            SetActive(stats.isShown);

        if (isShown
            && !quiver.IsEmpty
            && hand.caster.other is
                { isFiring: true, grabbedFire: false, spellInstance: SpellCastCharge { imbueEnabled: true } spell }
            && Vector3.Distance(hand.caster.other.Orb.position, transform.position) < spell.imbueRadius / 2) {
            quiver.Imbue(spell,
                spell.imbueRate * spell.spellCaster.ChargeRatio / quiver.Count * Time.unscaledDeltaTime,
                hand.creature);
        }
    }

    public void SetActive(bool active) {
        if (active == isShown) return;
        for (var i = 0; i < indicatorList.Count; i++) {
            indicatorList[i].SetActive(active);
        }

        isShown = active;
    }

    public void Refresh(Quiver quiver) {
        var newList = new List<GameObject>();
        
        for (var i = 0; i < quiver.blades.Count; i++) {
            var blade = quiver.blades[i];
            if (indicators.TryGetValue(blade, out var indicator)) {
                newList.Add(indicator);
            } else {
                var mesh = CreateBladeMesh();
                
                var linker = mesh.AddComponent<ImbueEmissionLinker>();
                linker.target = mesh.AddComponent<MaterialInstance>();
                if (blade.item.colliderGroups.Count > 0) {
                    for (var j = 0; j < blade.item.colliderGroups.Count; j++) {
                        if (blade.item.colliderGroups[j].imbue is Imbue imbue) {
                            Debug.Log("Linking blade mesh to blade imbue");
                            linker.imbue = imbue;
                            imbue.OnImbueSpellChange += (imbue, data, amount, change, time) => {
                                if (time == EventTime.OnEnd)
                                    linker.Refresh();
                            };

                            break;
                        }
                    }
                }
                linker.Refresh();

                indicators[blade] = mesh;
                newList.Add(indicators[blade]);
            }
        }

        bool changed = indicatorList.Count != newList.Count;
        for (var i = 0; i < indicatorList.Count; i++) {
            if (newList.Contains(indicatorList[i])) {
                if (!changed && indicatorList[i] != newList[i])
                    changed = true;
                continue;
            }
            changed = true;
            indicatorList[i].SetActive(false);
        }

        if (changed) {
            indicatorList = newList;

            int count = indicatorList.Count;
            for (var i = 0; i < indicatorList.Count; i++) {
                float maxSpread = (float)count / quiver.Max * Quiver.quiverSpread;
                float half = (count - 1f) / 2;
                float offset = count == 1 ? 0 : (i - half) / half;
                indicatorList[i].SetActive(isShown);
                indicatorList[i].transform.localPosition = Quaternion.AngleAxis(offset * maxSpread, Vector3.forward)
                                                           * new Vector3(0, 0.05f, 0);
                indicatorList[i].transform.rotation
                    = Quaternion.LookRotation(transform.position - indicatorList[i].transform.position,
                        transform.forward);
            }
        }
    }

    public GameObject CreateBladeMesh() {
        var mesh = new GameObject();
        mesh.gameObject.AddComponent<MeshFilter>().mesh = Quiver.bladeMesh;
        mesh.gameObject.AddComponent<MeshRenderer>().material = Instantiate(Quiver.bladeMat);
        mesh.gameObject.transform.SetParent(transform);
        mesh.gameObject.transform.localScale = Vector3.one * 0.1f;
        return mesh;
    }

    public void OnCreatureDespawn(EventTime time) {
        if (time == EventTime.OnEnd) return;
        creature.OnDespawnEvent -= OnCreatureDespawn;
        Destroy(this);
    }
}

public class ImbueEmissionLinker : ThunderBehaviour {
    public Imbue imbue;
    public MaterialInstance target;

    public void Refresh() {
        if (target == null) return;
        target.material.SetColor(CustomWristStats.emissionPropertyID, Color.white * 0.5f);
        if (imbue == null || imbue.spellCastBase == null || imbue.energy == 0) {
            return;
        }

        var effects = imbue.spellCastBase.imbueEffect.effects;
        for (var i = 0; i < effects.Count; i++) {
            if (effects[i]?.module is EffectModuleShader shader) {
                target.material.SetColor(CustomWristStats.emissionPropertyID, shader.mainColorEnd);
                return;
            }
        }
    }
}
