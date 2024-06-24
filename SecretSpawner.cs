using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using ThunderRoad;
using ThunderRoad.Skill.Spell;
using UnityEngine;

namespace Bladedancer; 

public class SecretSpawner : LevelModule {
    public string areaId;

    public string skillIdToSkipIfOwned;

    public List<SpawnSettings> spawnSettings;
    public bool spawned = false;
    
    public override IEnumerator OnLoadCoroutine() {
        spawned = false;
        if (spawnSettings != null && spawnSettings.Count != 0) {
            AreaManager.Instance.OnPlayerChangeAreaEvent += OnPlayerChangeArea;
        }
        return base.OnLoadCoroutine();
    }

    private void OnPlayerChangeArea(SpawnableArea newArea, SpawnableArea prevArea) {
        if (!string.IsNullOrEmpty(skillIdToSkipIfOwned)
            && Player.currentCreature
            && Player.currentCreature.HasSkill(skillIdToSkipIfOwned)) {
            spawned = true;
            return;
        }
        if (spawned || newArea.AreaDataId != areaId) return;
        for (var i = 0; i < spawnSettings.Count; i++) {
            spawnSettings[i].Spawn(newArea.SpawnedArea);
        }
        spawned = true;
    }
}

public abstract class SpawnSettings {
    public Vector3 position;
    public Vector3 rotation;
    public abstract void Spawn(Area area);

    public Vector3 GetPosition(Area area) => area.transform.TransformPoint(position);
    public Quaternion GetRotation(Area area) => area.transform.TransformRotation(Quaternion.Euler(rotation));
}

public class ItemSpawnSettings : SpawnSettings {
    public string itemId;
    public ItemModule[] extraModules;
    public override void Spawn(Area area) {
        if (Catalog.TryGetData(itemId, out ItemData data)) {
            data.SpawnAsync(OnItemSpawn, GetPosition(area), GetRotation(area), area.transform, false);
        }
    }

    public void OnItemSpawn(Item item) {
        if (extraModules is not { Length: > 0 }) return;
        for (var i = 0; i < extraModules.Length; i++) {
            extraModules[i].itemData = item.data;
            extraModules[i].OnItemLoaded(item);
        }
    }
}

public class CreatureSpawnSettings : SpawnSettings {
    public string creatureId;
    public string brainId;
    public string containerId;
    public int factionId;
    public string ethnicityId;
    public bool killOnStart;

    public override void Spawn(Area area) {
        if (Catalog.GetData<CreatureData>(creatureId)?.Clone() is not CreatureData creatureData) return;
        
        if (!string.IsNullOrEmpty(brainId))
            creatureData.brainId = brainId;
        
        if (!string.IsNullOrEmpty(containerId))
            creatureData.containerID = containerId;
        
        if (!string.IsNullOrEmpty(ethnicityId))
            creatureData.ethnicityId = ethnicityId;
        
        creatureData.factionId = factionId;
        
        creatureData.SpawnAsync(GetPosition(area), area.transform.rotation.y + rotation.y, area.transform, true, null, OnCreatureSpawn);
    }

    public void OnCreatureSpawn(Creature creature) {
        creature.RunAfter(() => {
            if (!string.IsNullOrEmpty(ethnicityId))
                creature.SetEthnicGroupFromId(ethnicityId);
            
            if (!killOnStart) return;
            creature.Kill();
            creature.ragdoll.isTkGrabbed = true;
            
        }, 0.1f);
    }
}