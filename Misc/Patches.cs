using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using ThunderRoad;
using UnityEngine;

namespace Bladedancer;

[HarmonyPatch(typeof(SkillTree), nameof(SkillTree.LoadMusicCoroutine))]
public class Patches {
    public static IEnumerator GetEnumerator(SkillTree instance) {
        if (!instance.TryGetPrivate("musicPlayer", out SynchronousMusicPlayer musicPlayer)
            || !instance.TryGetPrivate("musicClips", out List<AudioClip> musicClips)
            || !instance.TryGetPrivate("treeToMusicTrack", out Dictionary<int, int> treeToMusicTrack)
           ) yield break;
        var allTrees = Catalog.GetDataList<SkillTreeData>();
        int index = 0;
        for (var i = 0; i < allTrees.Count; ++i) {
            if (string.IsNullOrEmpty(allTrees[i].musicAddress)) {
                Debug.LogWarning($"Skill Tree {allTrees[i].id} has a null or empty musicAddress!");
                continue;
            }

            AudioClip audioClip = null;
            yield return Catalog.LoadAssetCoroutine<AudioClip>(allTrees[i].musicAddress,
                value => audioClip = value, nameof(SkillTree));
            Debug.Log($"Adding track {audioClip.name} at index {index}");
            musicClips.Add(audioClip);
            treeToMusicTrack[allTrees[i].hashId] = index++;
        }

        
        musicPlayer.LoadClips(musicClips);
        musicPlayer.Play();
    }

    public static bool Prefix(SkillTree __instance, ref IEnumerator __result) {
        Debug.Log("Patched method run!");
        __result = GetEnumerator(__instance);
        return false;
    }
}
