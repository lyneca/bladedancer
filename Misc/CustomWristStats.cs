using System.Collections.Generic;
using ThunderRoad;
using UnityEngine;

namespace Bladedancer; 

public class CustomWristStats : ThunderBehaviour {
    public override ManagedLoops EnabledManagedLoops => ManagedLoops.Update;

    public Creature creature;
    public Dictionary<Blade, GameObject> indicators;
    public List<GameObject> indicatorList;

    private WristStats stats;
    private bool isShown;

    public static int emissionPropertyID = Shader.PropertyToID("_EmissionColor");
    public static int useEmissionPropertyID = Shader.PropertyToID("_UseEmission");

    private void Awake() {
        creature = GetComponentInParent<Creature>();
        stats = GetComponent<WristStats>();
        creature.OnDespawnEvent += OnCreatureDespawn;
        indicators = new Dictionary<Blade, GameObject>();
        indicatorList = new List<GameObject>();
    }

    protected override void ManagedUpdate() {
        base.ManagedUpdate();
        if (stats.isShown != isShown)
            SetActive(stats.isShown);
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
            if (indicators.ContainsKey(quiver.blades[i])) {
                newList.Add(indicators[quiver.blades[i]]);
            } else {
                var mesh = CloneMeshFromBlade(quiver.blades[i]);
                indicators[quiver.blades[i]] = mesh;
                newList.Add(indicators[quiver.blades[i]]);
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

    public void OnCreatureDespawn(EventTime time) {
        if (time == EventTime.OnEnd) return;
        creature.OnDespawnEvent -= OnCreatureDespawn;
        Destroy(this);
    }

    public GameObject CloneMeshFromBlade(Blade blade) {
        var meshes = blade.gameObject.GetComponentsInChildren<MeshFilter>();
        var obj = new GameObject($"indicator-{blade.item.data.id}");
        obj.transform.SetPositionAndRotation(blade.item.Center, blade.ForwardRotation);
        obj.transform.localScale = blade.transform.localScale;
        var clones = new List<MeshRenderer>();
        for (var i = 0; i < meshes.Length; i++) {
            var mesh = meshes[i];
            var meshRenderer = mesh.GetComponent<MeshRenderer>();
            if (meshRenderer == null || !meshRenderer.enabled) continue;
            var clone = new GameObject().AddComponent<MeshFilter>();
            clone.mesh = Instantiate(mesh.sharedMesh);
            clone.transform.SetParent(mesh.transform.parent);
            clone.transform.localPosition = mesh.transform.localPosition;
            clone.transform.localRotation = mesh.transform.localRotation;
            clone.transform.localScale = mesh.transform.localScale;
            clone.transform.SetParent(obj.transform);
            var cloneRenderer = clone.gameObject.AddComponent<MeshRenderer>();
            cloneRenderer.material = Instantiate(meshRenderer.sharedMaterial);
            Imbue imbue = null;
            for (var j = 0; j < blade.item.colliderGroups.Count; j++) {
                if (blade.item.colliderGroups[j].imbueEmissionRenderer == meshRenderer) {
                    imbue = blade.item.colliderGroups[j].imbue;
                    break;
                }
            }

            if (imbue != null) {
                var linker = clone.gameObject.AddComponent<ImbueEmissionLinker>();
                linker.imbue = imbue;
                linker.target = cloneRenderer.gameObject.AddComponent<MaterialInstance>();
                linker.Refresh();
                imbue.OnImbueSpellChange += ImbueSpellChange;

                void ImbueSpellChange(
                    Imbue thisImbue,
                    SpellCastCharge spell,
                    float amount,
                    float change,
                    EventTime time) {
                    if (time == EventTime.OnEnd) {
                        linker.Refresh();
                    }
                }
            }
            clones.Add(cloneRenderer);
        }

        obj.transform.position = Vector3.zero;
        obj.transform.rotation = Quaternion.identity;
        var bounds = new Bounds();
        for (var i = 0; i < clones.Count; i++) {
            bounds.Encapsulate(clones[i].bounds);
        }

        var largestAxis = bounds.size.x > bounds.size.y ? Axis.X : Axis.Y;
        largestAxis = bounds.size.GetAxis(largestAxis) > bounds.size.z ? largestAxis : Axis.Z;
        float largestAxisSize = bounds.size.GetAxis(largestAxis);
        obj.transform.localScale = Vector3.one * Mathf.Clamp01(0.02f / largestAxisSize);
        obj.transform.position = transform.position;
        obj.transform.rotation = transform.rotation;
        obj.transform.SetParent(transform);
        return obj;
    }
}

public class ImbueEmissionLinker : ThunderBehaviour {
    public Imbue imbue;
    public MaterialInstance target;

    public void Refresh() {
        if (target == null) return;
        if (imbue == null || imbue.spellCastBase == null || imbue.energy == 0) {
            target.material.SetFloat(CustomWristStats.useEmissionPropertyID, 1f);
            target.material.SetColor(CustomWristStats.emissionPropertyID, Color.black);
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
