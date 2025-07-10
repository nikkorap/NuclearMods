using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;

namespace LoadoutFavourites
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;
        internal static Plugin Instance { get; private set; }
        private Harmony _harmony;
        private bool _patched = false;

        public ConfigEntry<bool> ModEnabled;

        private void Awake()
        {
            Logger = base.Logger;
            Instance = this;

            ModEnabled = Config.Bind("General", "Enabled", true, "Enable / disable the mod");
            ModEnabled.SettingChanged += (_, __) => UpdateModState();

            _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            UpdateModState();
        }

        private void UpdateModState()
        {
            if (ModEnabled.Value && !_patched)
            {
                _harmony.PatchAll();
                _patched = true;
                Logger.LogInfo($"{Info.Metadata.Name} v{Info.Metadata.Version} enabled");
            }
            else if (!ModEnabled.Value && _patched)
            {
                _harmony.UnpatchSelf();
                _patched = false;
                Logger.LogInfo($"{Info.Metadata.Name} v{Info.Metadata.Version} disabled");
            }
        }

        private void OnDestroy()
        {
            try { Config.Save(); if (_patched) _harmony.UnpatchSelf(); } catch { }
        }


        internal static string Section(AircraftDefinition def) => $"Aircraft:{def.unitName}";

        internal static string BuildCsv(AircraftCustomization custom)
        {
            var keys = custom.loadout.weapons.Select(m => m != null ? m.jsonKey : "");
            return string.Join(",", new[] { custom.fuelLevel.ToString("F2"), custom.livery.ToString() }.Concat(keys));
        }


        internal static bool TryParseCsv(string csv, out float fuel, out int livery, out List<string> keys)
        {
            fuel = 0;
            livery = 0;
            keys = null;
            if (string.IsNullOrEmpty(csv))
                return false;

            var parts = csv.Split(',');
            if (parts.Length < 2)
                return false;

            if (!float.TryParse(parts[0], out fuel))
                fuel = 1f;
            if (!int.TryParse(parts[1], out livery))
                livery = 0;

            keys = parts.Skip(2).ToList();
            return true;
        }

        internal static List<WeaponMount> KeysToMounts(IEnumerable<string> keys, IReadOnlyList<HardpointSet> sets)
        {
            var list = keys.ToList();
            if (list.Count < sets.Count)
                list.AddRange(Enumerable.Repeat(string.Empty, sets.Count - list.Count));
            if (list.Count > sets.Count)
                list = list.Take(sets.Count).ToList();

            var result = new List<WeaponMount>(sets.Count);
            for (int i = 0; i < sets.Count; i++)
            {
                var key = list[i];
                result.Add(string.IsNullOrEmpty(key)
                    ? null
                    : sets[i].weaponOptions.Find(w => w != null && w.jsonKey == key));
            }
            return result;
        }

        internal static AircraftCustomization BuildCustomization(AircraftSelectionMenu menu,
                                                                  AircraftDefinition def,
                                                                  float fuel,
                                                                  int livery,
                                                                  IEnumerable<string> keys)
        {
            var preview = AccessTools.Field(menu.GetType(), "previewAircraft")?.GetValue(menu) as Aircraft;
            if (preview?.weaponManager?.hardpointSets is not { Length: > 0 } sets)
                return null;

            var loadout = new Loadout();
            loadout.weapons.AddRange(KeysToMounts(keys, sets));
            return new AircraftCustomization(loadout, Mathf.Clamp01(fuel), Math.Max(0, livery));
        }

        internal static void SaveToConfig(AircraftDefinition def, AircraftCustomization custom)
        {
            var section = Section(def);
            var entry = Instance.Config.Bind(section, "Defaults", BuildCsv(custom));
            entry.Value = BuildCsv(custom);
            Instance.Config.Save();
        }

        internal static bool TryReadCustomization(AircraftDefinition def,
                                                  out float fuel,
                                                  out int livery,
                                                  out List<string> keys)
        {
            fuel = 0; livery = 0; keys = null;
            var section = Section(def);
            var entry = Instance.Config.Bind(section, "Defaults", string.Empty);
            if (!TryParseCsv(entry.Value, out fuel, out livery, out keys))
                return false;
            return true;
        }
    }


    [HarmonyPatch(typeof(AircraftSelectionMenu), "LoadDefaults")]
    public static class Patch_LoadDefaults
    {
        static void Prefix(AircraftSelectionMenu __instance)
        {
            if (!Plugin.Instance.ModEnabled.Value) return;
            try
            {
                var sel = AccessTools.Field(__instance.GetType(), "aircraftSelection")?.GetValue(__instance) as List<AircraftDefinition>;
                var idx = (int)AccessTools.Field(__instance.GetType(), "selectionIndex")?.GetValue(__instance);
                if (sel == null || idx < 0 || idx >= sel.Count) return;
                var def = sel[idx];

                if (!Plugin.TryReadCustomization(def, out var fuel, out var livery, out var keys))
                    return;

                GameManager.aircraftCustomization ??= new Dictionary<AircraftDefinition, AircraftCustomization>();
                var custom = Plugin.BuildCustomization(__instance, def, fuel, livery, keys);
                if (custom != null)
                    GameManager.aircraftCustomization[def] = custom;
            }
            catch (Exception e) { Plugin.Logger.LogError($"LoadDefaults patch error: {e}"); }
        }
    }

    [HarmonyPatch(typeof(AircraftSelectionMenu), "SaveDefaults")]
    public static class Patch_SaveDefaults
    {
        static void Postfix(AircraftSelectionMenu __instance)
        {
            if (!Plugin.Instance.ModEnabled.Value) return;
            try
            {
                var sel = AccessTools.Field(__instance.GetType(), "aircraftSelection")?.GetValue(__instance) as List<AircraftDefinition>;
                var idx = (int)AccessTools.Field(__instance.GetType(), "selectionIndex")?.GetValue(__instance);
                if (sel == null || idx < 0 || idx >= sel.Count) return;
                var def = sel[idx];

                if (GameManager.aircraftCustomization == null ||
                    !GameManager.aircraftCustomization.TryGetValue(def, out var custom))
                    return;

                Plugin.SaveToConfig(def, custom);
            }
            catch (Exception e) { Plugin.Logger.LogError($"SaveDefaults patch error: {e}"); }
        }
    }
}