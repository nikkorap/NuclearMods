using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnrestrictedWeapons
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance { get; private set; }
        internal static new ManualLogSource Logger;
        private static Harmony _harmony;

        private bool cached = false;
        public List<WeaponMount> originalMounts = [];
        public List<WeaponManager> originalManagers = [];
        private readonly Dictionary<HardpointSet, List<WeaponMount>> originalOptions = [];
        private readonly Dictionary<WeaponMount, bool> originalDisabled = [];
        private readonly Dictionary<WeaponMount, string> _mountKey = [];
        private readonly Dictionary<string, List<WeaponMount>> originalOptionsByName = [];
        private List<string> _filterTokens = [];

        private ConfigEntry<bool> ModEnabled;
        private ConfigEntry<bool> BlockAI;
        private ConfigEntry<bool> ToggleWhitelist;
        private ConfigEntry<string> BlacklistCsv;


        private static string KeyFor(HardpointSet set)
        {
            if (set == null) return "<null>";
            string n = set.name ?? "UnnamedSet";
            int hp = set.hardpoints?.Count ?? 0;
            return $"{n}|hp={hp}";
        }

        private void Awake()
        {
            Instance = this;
            this.hideFlags = HideFlags.HideAndDontSave;
            Logger = base.Logger;
            ModEnabled = Config.Bind("General", "ModEnabled", true, "Enable the mod");
            BlockAI = Config.Bind("General", "BlockAI", true, "force AI to use normal loadouts");
            ModEnabled.SettingChanged += (_, __) => ToggleMod(ModEnabled.Value);

            ToggleWhitelist = Config.Bind("General", "ToggleWhitelist", false, "Use the blacklist as a whitelist instead");
            BlacklistCsv = Config.Bind("General", "Part Blacklist (comma-separated)", "flare, afv, lcv, hlt, container, hook, flex, Turret, 750", "Lowercase substrings to block mounts");
            BlacklistCsv.SettingChanged += (_, __) =>
            {
                UpdateFilterTokens();
                if (cached && ModEnabled.Value) { ToggleMod(false); ToggleMod(true); }
            };

            ToggleWhitelist.SettingChanged += (_, __) =>
            {
                Logger.LogInfo($"Filter mode set to {(ToggleWhitelist.Value ? "Whitelist" : "Blacklist")}");
                if (cached && ModEnabled.Value) { ToggleMod(false); ToggleMod(true); }
            };


            UpdateFilterTokens();
            _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            _harmony.PatchAll();
        }

        private void UpdateFilterTokens()
        {
            string raw = BlacklistCsv?.Value ?? string.Empty;
            _filterTokens = [.. raw
                .Split([','], StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().ToLowerInvariant())
                .Where(t => t.Length > 0)
                .Distinct()];

            Logger.LogInfo($"Filter tokens: [{string.Join(", ", _filterTokens)}] (mode={(ToggleWhitelist.Value ? "whitelist" : "blacklist")})");
        }

        private bool IsAllowed(WeaponMount wm)
        {
            if (wm == null) return false;
            if (_filterTokens.Count == 0) return !ToggleWhitelist.Value;

            if (!_mountKey.TryGetValue(wm, out string key) || key == null)
                key = ((wm.mountName ?? wm.name) ?? string.Empty).ToLowerInvariant();

            bool matches = _filterTokens.Any(tok => key.Contains(tok));
            return ToggleWhitelist.Value ? matches : !matches;
        }


        private void CachePrefabs()
        {
            if (cached) return;

            originalMounts = [.. Resources.FindObjectsOfTypeAll<WeaponMount>().Where(m => m != null)];
            originalManagers = [.. Resources.FindObjectsOfTypeAll<WeaponManager>().Where(m => m != null)];

            if (originalMounts.Count == 0)
            {
                Logger.LogWarning("No WeaponMounts found; skipping cache.");
                return;
            }

            foreach (WeaponMount m in originalMounts)
            {
                originalDisabled[m] = m.disabled;
                string key = ((m.mountName ?? m.name) ?? string.Empty).ToLowerInvariant();
                _mountKey[m] = key;
                if (m.mountName is string s && !s.Contains(" [")) m.mountName = $"{s} [{m.name}]";
            }

            foreach (HardpointSet set in originalManagers.SelectMany(mgr => mgr.hardpointSets ?? []))
            {
                if (set == null || set.weaponOptions == null) continue;

                List<WeaponMount> src = set.weaponOptions;
                List<WeaponMount> filtered = new(src.Count);
                if (src.Count > 0 && src[0] == null) filtered.Add(null);
                for (int i = 1; i < src.Count; i++)
                {
                    WeaponMount wm = src[i];
                    if (wm != null && !wm.disabled) filtered.Add(wm);
                }

                if (!originalOptions.ContainsKey(set)) originalOptions[set] = filtered;

                string key = KeyFor(set);
                if (!originalOptionsByName.ContainsKey(key)) originalOptionsByName[key] = filtered;
            }
            cached = true;
            ToggleMod(ModEnabled.Value);
            Logger.LogInfo($"Cached {originalMounts.Count} mounts, {originalOptions.Count} sets.");
        }

        private void ToggleMod(bool enable)
        {
            if (!cached) return;

            if (enable)
            {
                foreach (HardpointSet set in originalOptions.Keys.ToList())
                {
                    if (set == null) continue;

                    List<WeaponMount> existing = set.weaponOptions ?? [];
                    HashSet<int> seenIds = [.. existing.Where(x => x != null).Select(x => x.GetInstanceID())];

                    foreach (WeaponMount wm in originalMounts)
                    {
                        if (wm != null && IsAllowed(wm) && seenIds.Add(wm.GetInstanceID()))
                            existing.Add(wm);
                    }

                    set.weaponOptions = existing;
                }

                foreach (WeaponMount wm in originalMounts) if (wm != null && IsAllowed(wm)) wm.disabled = false;
            }
            else
            {
                foreach (KeyValuePair<HardpointSet, List<WeaponMount>> kv in originalOptions)
                {
                    HardpointSet set = kv.Key;
                    if (set == null) continue;
                    set.weaponOptions = [.. kv.Value];
                }
                foreach (KeyValuePair<WeaponMount, bool> kv in originalDisabled)
                    if (kv.Key != null) kv.Key.disabled = kv.Value;
            }

            Logger.LogInfo($"{(enable ? "Enabled" : "Restored")} unrestrictedWeapons on {originalOptions.Count} sets (mounts={originalMounts.Count}, mode={(ToggleWhitelist.Value ? "whitelist" : "blacklist")}).");
        }
        //I HATE BOTS
        [HarmonyPatch(typeof(WeaponChecker), nameof(WeaponChecker.GetAvailableWeaponsNonAlloc))]
        static class GetAvailableWeaponsNonAlloc_FeedOriginal
        {
            static void Prefix(
                int? playerRank,
                HardpointSet hardpointSet,
                ref List<WeaponMount> __state)
            {
                __state = null;

                if (playerRank != null || hardpointSet == null || Instance == null || !Instance.BlockAI.Value) return;

                if (!Instance.originalOptions.TryGetValue(hardpointSet, out List<WeaponMount> original))
                {
                    string key = KeyFor(hardpointSet);
                    Instance.originalOptionsByName.TryGetValue(key, out original);
                }
                if (original == null) return;

                __state = hardpointSet.weaponOptions;
                hardpointSet.weaponOptions = [.. original];
            }

            static void Postfix(HardpointSet hardpointSet, List<WeaponMount> __state)
            {
                if (__state != null && hardpointSet != null)
                    hardpointSet.weaponOptions = __state;
            }
        }


        [HarmonyPatch(typeof(Encyclopedia), "AfterLoad", new Type[] { })]
        public static class EncyclopediaAfterLoadPatch
        {
            static void Postfix() => Instance.CachePrefabs();
        }
    }
    [HarmonyPatch(typeof(Application), nameof(Application.version), MethodType.Getter)]
    static class VersionGetterPatch
    {
        static void Postfix(ref string __result)
        {
            __result += $"_{MyPluginInfo.PLUGIN_GUID}-v{MyPluginInfo.PLUGIN_VERSION}";
            Plugin.Logger.LogWarning($"Updated game version to {__result}");
        }
    }
}

