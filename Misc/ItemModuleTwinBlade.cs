using System.Collections.Generic;
using Bladedancer.Skills;
using ThunderRoad;
using UnityEngine;

namespace Bladedancer;

public class ItemModuleTwinBlade : ItemModule {
    public string skillId;
    public string effectId;
    public static SkillData skillData;
    public static EffectData effectData;

    public override void OnItemDataRefresh(ItemData data) {
        base.OnItemDataRefresh(data);
        skillData = Catalog.GetData<SkillData>(skillId);
        effectData = Catalog.GetData<EffectData>(effectId);
    }

    public override void OnItemLoaded(Item item) {
        base.OnItemLoaded(item);
        item.gameObject.AddComponent<TwinBladeBehaviour>();
    }
}

public class ItemModuleTeacherBlade : ItemModule {
    public override void OnItemLoaded(Item item) {
        base.OnItemLoaded(item);
        item.OnGrabEvent += OnItemGrabbed;
    }

    private void OnItemGrabbed(Handle handle, RagdollHand hand) {
        item.OnGrabEvent -= OnItemGrabbed;
        if (ItemModuleTwinBlade.skillData == null || hand.creature.container.HasSkillContent(ItemModuleTwinBlade.skillData)) return;
        item.SetOwner(Item.Owner.Player);
        ItemModuleTwinBlade.effectData?.Spawn(hand.transform).Play();
        Player.local.creature.container.AddDataContent(ItemModuleTwinBlade.skillData);
        Player.characterData.inventory.ClearPlayerInventory(true);
        Player.characterData.inventory.SetPlayerInventory(Player.local.creature.container.CloneContents());
        Player.characterData.SaveAsync();
        DisplayMessage.instance.ShowMessage(new DisplayMessage.MessageData(
            LocalizationManager.Instance.TryGetLocalization("Skills", ItemModuleTwinBlade.skillData.description), 0));
    }
}

public class TwinBladeBehaviour : ThunderBehaviour {
    public Item item;
    public Blade[] heldBlades;
    public Dictionary<ColliderGroup, Damager> damagers;
    public ColliderGroup[] colliderGroups;

    private void Awake() {
        item = GetComponent<Item>();
        item.OnHeldActionEvent += OnHeldAction;
        item.OnUngrabEvent += OnUnGrab;
        item.data.drainImbueOnSnap = false;
        item.data.drainImbueWhenIdle = false;
        item.mainHandleLeft.UnGrabbed += OnHandleUnGrabbed;
        
        heldBlades = new Blade[2];
        colliderGroups = new ColliderGroup[2];
        damagers = new Dictionary<ColliderGroup, Damager>();

        var j = 0;
        for (var i = 0; i < item.mainCollisionHandler.damagers.Count; i++) {
            var damager = item.mainCollisionHandler.damagers[i];
            if (damager.type != Damager.Type.Pierce) continue;
            damagers[damager.colliderGroup] = damager;
            colliderGroups[j++] = damager.colliderGroup;
            if (damager.colliderGroup.data.Clone() is not ColliderGroupData data) continue;
            
            damager.colliderGroup.modifier = data.GetModifier(damager.colliderGroup);
            damager.colliderGroup.modifier.imbueConstantLoss = 0;
            damager.colliderGroup.modifier.imbueHitLoss = 0;
            damager.colliderGroup.modifier.imbueVelocityLossPerSecond = 0;
        }
        
        SkillTwinBladeMaestro.OnSkillDisableEvent += OnSkillDisabled;
    }

    private void OnHandleUnGrabbed(RagdollHand hand, Handle handle, EventTime time) {
        if (time == EventTime.OnEnd) return;
        int current = GetIndexForHand(hand);
        if (heldBlades[current]) ReleaseDagger(current);
    }

    public int Other(int i) => i == 0 ? 1 : 0;

    private void OnSkillDisabled(SkillData data, Creature creature) {
        DropDaggers();
    }

    private void OnUnGrab(Handle handle, RagdollHand hand, bool throwing) {
        DropDaggers();
    }

    public void DropDaggers() {
        ReleaseDagger(0);
        ReleaseDagger(1);
    }

    private void OnHeldAction(RagdollHand hand, Handle handle, Interactable.Action action) {
        if (!SkillTwinBladeMaestro.enabled) return;
        switch (action) {
            case Interactable.Action.UseStart:
                GrabDagger(hand);
                break;
            case Interactable.Action.UseStop:
                ReleaseDagger(hand);
                break;
        }
    }

    public Vector3 GetTargetPos(int i) => damagers[colliderGroups[i]].transform.position
                                       + damagers[colliderGroups[i]].transform.forward * 0.4f;

    public Quaternion GetTargetRot(int i) {
        var damager = damagers[colliderGroups[i]].transform;
        return Quaternion.LookRotation(damager.forward, damager.right);
    }

    public int GetHandUpIndex(RagdollHand hand) {
        var toPos0 = GetTargetPos(0) - hand.transform.position;
        return Vector3.Dot(hand.ThumbDir, toPos0) > 0 ? 0 : 1;
    }

    public int GetHandSideIndex(RagdollHand hand) {
        bool firstHandlerIsHigher = item.handlers[0].gripInfo.axisPosition > item.handlers[1].gripInfo.axisPosition;
        bool handIsFirstHandler = item.handlers[0] == hand;
        return firstHandlerIsHigher ? handIsFirstHandler ? 1 : 0 : handIsFirstHandler ? 0 : 1;
    }

    public void GrabDagger(RagdollHand hand) {
        if (GrabDagger(GetIndexForHand(hand)))
            hand.HapticTick();
    }

    public bool GrabDagger(int index) {
        if (index < 0
            || index > colliderGroups.Length
            || colliderGroups[index] is not ColliderGroup group) return false;

        if (!Quiver.Main.TryGetClosestBlade(GetTargetPos(index), out var blade)) return false;

        ReleaseDagger(index);
        blade.IgnoreItem(item);
        heldBlades[index] = blade;
        blade.MoveTo(new MoveTarget(MoveMode.PID, 10)
            .Parent(group.transform)
            .AtWorld(GetTargetPos(index), GetTargetRot(index))
            .Scale(ScaleMode.Scaled));
        blade.AllowDespawn(false);
        if (group.imbue is { spellCastBase: not null and var spell, energy: > 0 }) {
            blade.MaxImbue(spell, Player.currentCreature);
        }

        return true;
    }

    public void ReleaseDagger(RagdollHand hand) {
        int handUp = GetHandUpIndex(item.mainHandler);
        if (item.handlers.Count == 1
            && heldBlades[handUp] == null
            && heldBlades[Other(handUp)] != null) {
            if (ReleaseDagger(Other(handUp)))
                hand.playerHand.controlHand.HapticPlayClip(Catalog.gameData.haptics.bowShoot);
        }
        if (ReleaseDagger(GetIndexForHand(hand)))
            hand.playerHand.controlHand.HapticPlayClip(Catalog.gameData.haptics.bowShoot);
    }

    public bool ReleaseDagger(int index) {
        if (index < 0
            || index > colliderGroups.Length
            || colliderGroups[index] is not ColliderGroup
            || heldBlades[index] == null) return false;

        var velocity = item.physicBody.GetPointVelocity(GetTargetPos(index));
        velocity = Vector3.Slerp(Player.local.head.transform.forward.normalized, velocity.normalized, 0.5f)
                   * velocity.magnitude;

        if (velocity.sqrMagnitude
            < SkillTwinBladeMaestro.shootVelocityThreshold * SkillTwinBladeMaestro.shootVelocityThreshold) {
            heldBlades[index]?.ReturnToQuiver(Quiver.Main);
            heldBlades[index] = null;
            return false;
        }

        heldBlades[index].Release(true, 0.5f);
        heldBlades[index].AddForce(velocity * SkillTwinBladeMaestro.throwMult, ForceMode.VelocityChange);
        heldBlades[index].IgnoreItem(item, false);
        
        if (Creature.AimAssist(GetTargetPos(index), velocity, 30, 30, out var target, Filter.EnemyOf(Player.currentCreature),
                CreatureType.Golem | CreatureType.Human) is ThunderEntity entity) {
            heldBlades[index].HomeTo(entity, target);
        }
        
        heldBlades[index] = null;

        return true;
    }

    public int GetIndexForHand(RagdollHand hand) {
        if (!item.handlers.Contains(hand)) return -1;
        switch (item.handlers.Count) {
            case 0:
                return -1;
            case 1:
                return GetHandUpIndex(hand);
            case 2:
                return GetHandSideIndex(hand);
        }

        return -1;
    }
}