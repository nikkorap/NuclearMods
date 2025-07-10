using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace DetailedClimbRate
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;

        private Harmony harmony;

        private ConfigEntry<bool> ModEnabled;
        private ConfigEntry<bool> ClimbRateReading;
        private ConfigEntry<bool> AltitudeReading;

        private void Awake()
        {
            this.hideFlags = HideFlags.HideAndDontSave;
            Logger = base.Logger;
            harmony = new Harmony(MyPluginInfo.PLUGIN_NAME);

            ModEnabled = Config.Bind("General", "Enabled", true, "Enable this mod");
            ClimbRateReading = Config.Bind("General", "Detailed climbrate", true, "Enable detailed climbrate reading");
            AltitudeReading = Config.Bind("General", "Detailed altitude", true, "Enable detailed altitude reading");

            ModEnabled.SettingChanged += (_, __) => UpdateModState();
            ClimbRateReading.SettingChanged += (_, __) => ToggleClimbRateReading_Patch(ClimbRateReading.Value);
            AltitudeReading.SettingChanged += (_, __) => ToggleAltitudeReading_Patch(AltitudeReading.Value);
            UpdateModState();
        }
        private void UpdateModState()
        {
            if (ModEnabled.Value)
            {
                Logger.LogInfo("Mod enabled, patching");
                ToggleClimbRateReading_Patch(ClimbRateReading.Value);
                ToggleAltitudeReading_Patch(AltitudeReading.Value);
            }
            else
            {
                Logger.LogWarning("Mod disabled, unpatching");
                harmony.UnpatchSelf();
            }
        }
        private void ToggleClimbRateReading_Patch(bool enabled)
        {
            var method = ClimbRateReading_Patch.TargetMethod();
            var prefix = new HarmonyMethod(typeof(ClimbRateReading_Patch).GetMethod(nameof(ClimbRateReading_Patch.Prefix)));

            if (enabled)
            {
                harmony.Patch(method, prefix: prefix);
                Logger.LogInfo("ClimbRateReading patched");
            }
            else
            {
                harmony.Unpatch(method, HarmonyPatchType.Prefix, harmony.Id);
                Logger.LogInfo("ClimbRateReading unpatched");
            }
        }
        private void ToggleAltitudeReading_Patch(bool enabled)
        {
            var method = AltitudeReading_Patch.TargetMethod();
            var prefix = new HarmonyMethod(typeof(AltitudeReading_Patch).GetMethod(nameof(AltitudeReading_Patch.Prefix)));

            if (enabled)
            {
                harmony.Patch(method, prefix: prefix);
                Logger.LogInfo("AltitudeReading patched");
            }
            else
            {
                harmony.Unpatch(method, HarmonyPatchType.Prefix, harmony.Id);
                Logger.LogInfo("AltitudeReading unpatched");
            }
        }
    }
}
[HarmonyPatch(typeof(UnitConverter), nameof(UnitConverter.ClimbRateReading))]
class ClimbRateReading_Patch
{
    public static bool Prefix(ref string __result, ref float speed)
    {
        string unit = "m/s";
        int roundval = 0;
        float fpmMult = 60f * 3.28084f;
        if (PlayerSettings.unitSystem == PlayerSettings.UnitSystem.Metric)
        {
            roundval = (int)MathF.Round(speed * 100); // speed in hundredths of m/s
        }
        else
        {
            roundval = (int)MathF.Round(speed * 100 * fpmMult); // speed in hundredths of fpm
            unit = "fpm";
        }
        if (roundval >= 10000)
            __result = $"\u00A0+{roundval / 100}{unit}"; // e.g., +100m/s
        else if (roundval >= 1000)
            __result = $"+{(roundval / 10) / 10f:F1}{unit}"; // e.g., +45.2m/s
        else if (roundval >= 0)
            __result = $"\u00A0{roundval / 100f:F2}{unit}"; // e.g., +0.34m/s
        else if (roundval <= -10000)
            __result = $"\u00A0{roundval / 100}{unit}"; // e.g., -150m/s
        else if (roundval <= -1000)
            __result = $"{(roundval / 10) / 10f:F1}{unit}"; // e.g., -54.3m/s
        else
            __result = $"{roundval / 100f:F2}{unit}"; // e.g., -0.67m/s
        return false;
    }
    public static MethodBase TargetMethod() => AccessTools.Method(typeof(UnitConverter), "ClimbRateReading");
}

[HarmonyPatch(typeof(UnitConverter), nameof(UnitConverter.AltitudeReading))]
class AltitudeReading_Patch
{
    public static bool Prefix(ref string __result, ref float altitude)
    {
        string unit = "m";
        int roundval = 0;
        float ftMult = 3.28084f;
        if (PlayerSettings.unitSystem == PlayerSettings.UnitSystem.Metric)
        {
            roundval = (int)MathF.Round(altitude * 100); // altitude in hundredths of m
        }
        else
        {
            roundval = (int)MathF.Round(altitude * 100 * ftMult); // altitude in hundredths of ft
            unit = "ft";
        }
        if (roundval >= 10000)
            __result = $"\u00A0+{roundval / 100}{unit}"; // e.g., +100m
        else if (roundval >= 1000)
            __result = $"+{(roundval / 10) / 10f:F1}{unit}"; // e.g., +45.2m
        else if (roundval >= 0)
            __result = $"\u00A0{roundval / 100f:F2}{unit}"; // e.g., +0.34m
        else if (roundval <= -10000)
            __result = $"\u00A0{roundval / 100}{unit}"; // e.g., -150m
        else if (roundval <= -1000)
            __result = $"{(roundval / 10) / 10f:F1}{unit}"; // e.g., -54.3m
        else
            __result = $"{roundval / 100f:F2}{unit}"; // e.g., -0.67m
        return false;

    }
    public static MethodBase TargetMethod() => AccessTools.Method(typeof(UnitConverter), "AltitudeReading");
}