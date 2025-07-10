// AirbaseCapturePlugin.cs

using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace BaseRefresh
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class AirbaseCapturePlugin : BaseUnityPlugin
    {
        private static new ManualLogSource Logger;

        // Config entries
        private ConfigEntry<bool> ModEnabled;
        private ConfigEntry<KeyboardShortcut> TriggerKey;

        // Reflection
        private MethodInfo waitRepairMethod;

        private void Awake()
        {
            this.hideFlags = HideFlags.HideAndDontSave;
            Logger = base.Logger;

            ModEnabled = Config.Bind("General", "Enable Mod", true, new ConfigDescription("Enable or disable the Airbase Capture mod"));

            TriggerKey = Config.Bind("General", "Capture Toggle Key", new KeyboardShortcut(KeyCode.C), new ConfigDescription("Key to trigger repair cycle on all airbases"));

            waitRepairMethod = typeof(Airbase).GetMethod("WaitRepair", BindingFlags.Instance | BindingFlags.NonPublic);
            if (waitRepairMethod == null)
                Logger.LogError("Could not find Airbase.WaitRepair via reflection");
            else
                Logger.LogDebug("Reflection: found Airbase.WaitRepair(FactionHQ)");
        }

        private void Update()
        {
            if (!ModEnabled.Value) return;

            if (TriggerKey.Value.IsDown())
            {
                Logger.LogDebug("Trigger key pressed, initiating repair sequence");
                StartCoroutine(RepairAllAirbasesCoroutine());
            }
        }

        private IEnumerator RepairAllAirbasesCoroutine()
        {
            if (waitRepairMethod == null)
            {
                Logger.LogError("WaitRepair method unavailable, aborting.");
                yield break;
            }

            // Find all non-carrier, non-destroyer airbases
            var airbases = Resources.FindObjectsOfTypeAll<Airbase>()
                .Where(ab => !ab.name.Contains("Carrier", StringComparison.OrdinalIgnoreCase)
                             && !ab.name.Contains("Destroyer", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            Logger.LogDebug($"Found {airbases.Length} airbases to process");

            foreach (var ab in airbases)
            {
                var original = ab.CurrentHQ;
                if (original == null)
                {
                    Logger.LogDebug($"Skipping '{ab.name}' (no current HQ)");
                    continue;
                }

                // Invoke WaitRepair via reflection
                object uniTaskObj = null;
                try
                {
                    uniTaskObj = waitRepairMethod.Invoke(ab, null);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error invoking WaitRepair on '{ab.name}': {ex}");
                }

                if (uniTaskObj != null)
                {
                    // Attempt to await the task via ToCoroutine if available
                    var toCorr = uniTaskObj.GetType().GetMethod("ToCoroutine", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                    if (toCorr != null)
                    {
                        if (toCorr.Invoke(uniTaskObj, null) is IEnumerator enumerator)
                            yield return enumerator;
                        else
                            yield return null;
                    }
                    else
                    {
                        yield return null;
                    }
                }
                else
                {
                    yield return null;
                }
                Logger.LogInfo($"Repaired airbase '{ab.name}'");
            }
        }
    }
}
