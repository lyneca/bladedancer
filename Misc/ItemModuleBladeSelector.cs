using System.Collections.Generic;
using ThunderRoad;
using ThunderRoad.DebugViz;
using UnityEngine;

namespace Bladedancer; 

public class ItemModuleBladeSelector : ItemModule {
    public EffectData effectData;
    public string effectId;

    public string setEffectId;
    public EffectData setEffectData;
    public string unsetEffectId;
    public EffectData unsetEffectData;
    
    public List<string> allowedSlots = new() {
        "Arrow",
        "Cork",
        "Head",
        "Small",
        "Medium",
        "Large",
        "Potion",
        "Quiver",
        "Shield",
        "ShieldSmall",
        "Throwables",
        "Torch",
    };

    public override void OnItemDataRefresh(ItemData data) {
        base.OnItemDataRefresh(data);
        setEffectData = Catalog.GetData<EffectData>(setEffectId);
        unsetEffectData = Catalog.GetData<EffectData>(unsetEffectId);
        effectData = Catalog.GetData<EffectData>(effectId);
    }

    public override void OnItemLoaded(Item item) {
        base.OnItemLoaded(item);
        item.gameObject.AddComponent<BladeSelector>().module = this;
    }
}

public class BladeSelector : ThunderBehaviour {
    public bool shown;
    public Item item;
    public ItemMagnet magnet;
    public ItemModuleBladeSelector module;
    public EffectInstance effect;

    public override ManagedLoops EnabledManagedLoops => ManagedLoops.Update;

    private void Start() {
        item = GetComponent<Item>();
        item.OnHeldActionEvent += OnHeldAction;

        var obj = new GameObject("BladeMagnet");
        var rb = obj.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        
        var collider = obj.AddComponent<SphereCollider>();
        collider.radius = 0.2f;
        collider.isTrigger = true;
        
        magnet = obj.AddComponent<ItemMagnet>();
        magnet.tagFilter = FilterLogic.NoneExcept;
        magnet.slots = module.allowedSlots;
        magnet.catchedItemIgnoreGravityPush = true;
        magnet.magnetReactivateDurationOnRelease = 0.5f;
        magnet.massMultiplier = 3f;
        magnet.OnItemCatchEvent += OnItemCatch;
        magnet.OnItemReleaseEvent += OnItemRelease;
    }

    private void OnItemCatch(Item item, EventTime time) {
        if (time == EventTime.OnEnd || !shown || item.data == null || string.IsNullOrEmpty(item.data.id)) return;
        module.setEffectData?.Spawn(transform).Play();
        magnet.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(item.ForwardVector(), Vector3.up));
        Blade.SetCustomBlade(item.data.id);
    }

    private void OnItemRelease(Item item, EventTime time) {
        if (time == EventTime.OnStart || !shown) return;
        module.unsetEffectData.Spawn(transform).Play();
        Blade.ClearCustomBlade();
    }

    public void OnHeldAction(RagdollHand hand, Handle handle, Interactable.Action action) {
        switch (action) {
            case Interactable.Action.AlternateUseStart:
                Show(true);
                break;
            case Interactable.Action.AlternateUseStop or Interactable.Action.Ungrab:
                Show(false);
                break;
        }
    }

    protected override void ManagedUpdate() {
        base.ManagedUpdate();
        if (!shown || !magnet || !Player.local) return;
        
        magnet.transform.position = transform.position + Vector3.up * 0.3f;
    }

    public void Show(bool shown) {
        if (this.shown == shown) return;
        this.shown = shown;
        if (shown) {
            magnet.transform.position = transform.position + Vector3.up * 0.3f;
            magnet.transform.rotation
                = Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.up, Vector3.up), Vector3.up);
            magnet.transform.SetParent(null);
            magnet.enabled = true;
            magnet.trigger.enabled = true;
            effect = module.effectData?.Spawn(magnet.transform);
            effect?.Play();
        } else {
            effect.SetParent(null);
            effect?.End();
            effect = null;
            if (magnet.capturedItems.Count == 0) {
                module.unsetEffectData?.Spawn(transform).Play();
                Blade.ClearCustomBlade();
            }

            magnet.enabled = false;
            magnet.trigger.enabled = false;
            magnet.transform.SetParent(transform);
            magnet.transform.localPosition = Vector3.zero;
            magnet.transform.localRotation = Quaternion.identity;
        }
    }
}
