using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using HarmonyLib.Tools;

namespace UnrestrictedWeapons
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class UnrestrictedWeaponsPlugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;
        private static Harmony _harmony;

        private ConfigEntry<bool> ModEnabled;
        private ConfigEntry<bool> ToggleWhitelist;
        private ConfigEntry<string> BlacklistCsv;

        private Coroutine cacheCoroutine;
        private bool cacheComplete = false;

        // store prefab mounts and original options per prefab hardpoint set
        private List<WeaponMount> prefabMounts = [];
        private Dictionary<object, List<WeaponMount>> originalOptions = [];
        private Dictionary<WeaponMount, bool> originalDisabled = [];

        private void Awake()
        {
            this.hideFlags = HideFlags.HideAndDontSave;
            Logger = base.Logger;
            ModEnabled = Config.Bind("General", "ModEnabled", true, "Enable the mod");
            ModEnabled.SettingChanged += (_, __) => UpdateModState();

            ToggleWhitelist = Config.Bind("General", "ToggleWhitelist", false, "Use the blacklist as a whitelist instead");
            BlacklistCsv = Config.Bind("General","Part Blacklist (comma-separated)", "afv, lcv, hlt, container, hook, flex, Turret, 750", "Lowercase substrings to block mounts");
            _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            _harmony.PatchAll();
            UpdateModState();
        }
        private void UpdateModState()
        {
            if (ModEnabled.Value)
            {
                Logger.LogInfo("UnrestrictedWeapons mod enabled");
                if (!cacheComplete && cacheCoroutine == null)
                    cacheCoroutine = StartCoroutine(CachePrefabsLoop());
                if (cacheComplete)
                    ModifyPrefabs();
            }
            else
            {
                Logger.LogWarning("UnrestrictedWeapons mod disabled, restoring prefab options");
                if (cacheCoroutine != null)
                {
                    StopCoroutine(cacheCoroutine);
                    cacheCoroutine = null;
                }
                RestorePrefabOptions();
            }
        }

        // Wait until prefab assets are loaded into memory
        private IEnumerator CachePrefabsLoop()
        {
            var wait = new WaitForSeconds(2f);
            while (!cacheComplete)
            {
                // find managers and mounts, filter out scene instances
                var managers = Resources.FindObjectsOfTypeAll<WeaponManager>().Where(m => IsPrefabObject(m)).ToArray();
                var mounts = Resources.FindObjectsOfTypeAll<WeaponMount>().Where(m => IsPrefabObject(m)).ToArray();

                if (managers.Length > 0 && mounts.Length > 0)
                {
                    prefabMounts = [.. mounts];

                    // cache disabled flag
                    foreach (var m in prefabMounts)
                        originalDisabled[m] = m.disabled;

                    // cache original options per prefab hardpoint set
                    foreach (var mgr in managers)
                    {
                        if (mgr.hardpointSets == null) continue;
                        foreach (var set in mgr.hardpointSets)
                        {
                            if (set == null || set.weaponOptions == null) continue;
                            if (!originalOptions.ContainsKey(set))
                                originalOptions[set] = [.. set.weaponOptions];
                        }
                    }

                    cacheComplete = true;
                    Logger.LogInfo($"Cached {prefabMounts.Count} prefab mounts and {originalOptions.Count} prefab sets");
                    ModifyPrefabs();
                    yield break;
                }
                yield return wait;
            }
        }

        //  determine if object is a prefab definition rather than a scene instance
        private bool IsPrefabObject(UnityEngine.Object obj)
        {
            if (obj is Component comp)
                return !comp.gameObject.scene.isLoaded;
            // if not a Component, assume prefab
            return true;
        }

        // Apply changes only on prefab definitions
        private void ModifyPrefabs()
        {
            if (!ModEnabled.Value || !cacheComplete)
                return;

            var blacklist = BlacklistCsv.Value
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToLower())
                .ToList();

            int added = 0;
            foreach (var kv in originalOptions)
            {
                var set = kv.Key;
                var origList = kv.Value;
                var optionsField = set.GetType().GetField("weaponOptions", BindingFlags.Public | BindingFlags.Instance);
                if (optionsField == null) continue;
                var list = optionsField.GetValue(set) as IList<WeaponMount>;
                if (list == null) continue;

                foreach (var mount in prefabMounts)
                {
                    if (mount == null || string.IsNullOrEmpty(mount.mountName)) continue;
                    var name = mount.mountName.ToLower();
                    if (ToggleWhitelist.Value)
                    {
                        if (!blacklist.Any(b => name.Contains(b)))
                            continue;
                    }
                    else
                    {
                        if (blacklist.Any(b => name.Contains(b)))
                            continue;
                    }
                        if (!list.Contains(mount)) { list.Add(mount); added++; }
                    if (mount.disabled) { mount.disabled = false; added++; }
                }
            }
            Logger.LogInfo($"Prefabs modified: added {added} options");
        }

        private void RestorePrefabOptions()
        {
            int removed = 0;
            foreach (var kv in originalOptions)
            {
                var set = kv.Key;
                var origList = kv.Value;
                var optionsField = set.GetType().GetField("weaponOptions", BindingFlags.Public | BindingFlags.Instance);
                if (optionsField == null) continue;
                var list = optionsField.GetValue(set) as IList<WeaponMount>;
                if (list == null) continue;

                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var m = list[i];
                    if (m != null && !origList.Contains(m)) { list.RemoveAt(i); removed++; }
                }
            }

            int reset = 0;
            foreach (var kv in originalDisabled)
            {
                var m = kv.Key;
                if (m != null && m.disabled != kv.Value) { m.disabled = kv.Value; reset++; }
            }

            Logger.LogInfo($"Prefabs restored: removed {removed} options, reset {reset} disabled flags");
        }
    }

    [HarmonyPatch(typeof(Application), nameof(Application.version), MethodType.Getter)]
    static class VersionGetterPatch
    {
        static void Postfix(ref string __result)
        {
            __result += $"_{MyPluginInfo.PLUGIN_GUID}-v{MyPluginInfo.PLUGIN_VERSION}";
            UnrestrictedWeaponsPlugin.Logger.LogWarning($"Updated game version to {__result}");
        }
    }
}

