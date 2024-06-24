using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using ThunderRoad;
using ThunderRoad.Modules;
using Debug = UnityEngine.Debug;

namespace Bladedancer; 

public class CustomLoreAdder : GameModeModule {
    public List<CustomLorePack> loreEntries;
    public bool loaded;

    public override IEnumerator OnLoadCoroutine() {
        yield return base.OnLoadCoroutine();
        if (loaded || loreEntries == null) yield break;
        loaded = true;

        if (!TryGetLoreModule(out var module)) yield break;
        
        var loreDict = GetCurrentLoreDict();
        foreach (var customLorePack in loreEntries) {
            if (!loreDict.TryGetValue(customLorePack.groupId, out var loreGroup)) continue;
            if (customLorePack.ToLorePack() is not LoreScriptableObject.LorePack pack) continue;
            foreach (int i in pack.loreRequirement) {
                Debug.Log($"Pack {loreGroup.name} getting requirement {i} for {pack.nameId}: '{loreGroup.GetPack(i)?.nameId}'");
            }
            Debug.Log(string.Join(", ", pack.loreRequirement));
            var list = new List<LoreScriptableObject.LorePack>(loreGroup.allLorePacks) { pack };
            loreGroup.rootLoreHashIds.Add(pack.hashId);
            typeof(LoreScriptableObject).GetField("_hashIdToLorePack", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(loreGroup, null);
            loreGroup.allLorePacks = list.ToArray();
        }

        typeof(LoreModule).GetMethod("InitLoreState", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(module, Array.Empty<object>());
        Debug.Log("Refreshed lore module with custom data");
    }

    public bool TryGetLoreModule(out LoreModule module) {
        return GameModeManager.instance.currentGameMode.TryGetModule(out module);
    }

    public List<int> GetAvailableLore() {
        if (TryGetLoreModule(out var module))
            return typeof(LoreModule).GetField("availableLore", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(module) as List<int>;
        return null;
    }

    public List<LoreScriptableObject> GetAllLoreScriptableObjects() {
        if (!TryGetLoreModule(out LoreModule module)) return null;
        return typeof(LoreModule).GetField("allLoreSO", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(module) as List<LoreScriptableObject>;
    }

    public Dictionary<string, LoreScriptableObject> GetCurrentLoreDict() {
        if (GetAllLoreScriptableObjects() is not List<LoreScriptableObject> allSOs) return null;
        
        

        var dict = new Dictionary<string, LoreScriptableObject>();
        for (var i = 0; i < allSOs.Count; i++) {
            if (allSOs[i].allLorePacks is not { Length: > 0 } packs
                || packs[0].groupId is not string loreId) continue;

            Debug.Log($"Adding {allSOs[i]} ({allSOs[i].name} to dict under ID {loreId}");

            dict[loreId] = allSOs[i];
        }

        return dict;
    }
}

public class CustomLorePack {
    public string groupId;
    public string packId;
    public string contentAddress;
    public string itemId;
    public List<string> prerequisites;
    public LoreScriptableObject.LoreType type;
    public List<LoreScriptableObject.LoreData> lore;
    public List<LorePackCondition.Visibility> visibilityConditions;
    public LorePackCondition.LoreLevelOptionCondition[] levelOptionConditions;

    public LoreScriptableObject.LorePack ToLorePack() {
        if (lore == null) return null;
        for (var i = 0; i < lore.Count; i++) {
            lore[i].groupId = groupId;
            lore[i].displayGraphicsInJournal = false;
            lore[i].itemId ??= itemId;
            lore[i].loreType = type;
            lore[i].contentAddress = contentAddress;
        }

        var condition
            = (LorePackCondition)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(
                typeof(LorePackCondition));

        condition.visibilityRequired = visibilityConditions;
        condition.levelOptions = levelOptionConditions;
        condition.requiredParameters = Array.Empty<string>();

        var loreRequirement = new List<int>();

        if (prerequisites is { Count: > 0 }) {
            for (var i = 0; i < prerequisites.Count; i++) {
                loreRequirement.Add(LoreScriptableObject.GetLoreHashId(prerequisites[i]));
            }
        }

        return new LoreScriptableObject.LorePack {
            groupId = groupId,
            nameId = packId,
            hashId = LoreScriptableObject.GetLoreHashId(packId),
            loreData = lore,
            lorePackCondition = condition,
            loreRequirement = loreRequirement,
            spawnPackAsOneItem = lore.Count > 1
        };
    }
}
