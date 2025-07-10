// NoDetectionPlugin.cs

using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using NuclearOption.Jobs;
using UnityEngine;

namespace DetectionDisable
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class NoDetectionPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Logger;
        internal static Harmony harmony;

        internal static ConfigEntry<bool> ModEnabled;
        internal static ConfigEntry<bool> BlockRadar;
        internal static ConfigEntry<bool> BlockLoS;
        internal static ConfigEntry<float> MaxAltitude;
        internal static ConfigEntry<float> ZoneCenterX;
        internal static ConfigEntry<float> ZoneCenterZ;
        internal static ConfigEntry<float> ZoneRadius;

        private void Awake()
        {
            this.hideFlags = HideFlags.HideAndDontSave;
            Logger = base.Logger;
            harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);

            // Config bindings
            ModEnabled = Config.Bind("General", "Enable Mod", true, "Enable/disable the entire mod");
            BlockRadar = Config.Bind("Toggles", "Block Radar Checks", true, "Disable DetectorManager.RequestRadarCheck");
            BlockLoS = Config.Bind("Toggles", "Block Line-of-Sight Checks", true, "Disable DetectorManager.RequestLoSCheck");
            MaxAltitude = Config.Bind("Limits", "Max Altitude", 1000f, "Upper altitude bound of no-detect zone");

            ZoneCenterX = Config.Bind("Zone", "Center X", 0f,
                new ConfigDescription("X coordinate of circular no-detect zone center", new AcceptableValueRange<float>(-100000f, 100000f)));
            ZoneCenterZ = Config.Bind("Zone", "Center Z", 0f,
                new ConfigDescription("Z coordinate of circular no-detect zone center", new AcceptableValueRange<float>(-100000f, 100000f)));
            ZoneRadius = Config.Bind("Zone", "Radius", 5000f, "Radius of circular no-detect zone");

            // Subscribe to setting changes
            ModEnabled.SettingChanged += (_, __) => RefreshPatching();
            BlockRadar.SettingChanged += (_, __) => RefreshPatching();
            BlockLoS.SettingChanged += (_, __) => RefreshPatching();
            MaxAltitude.SettingChanged += (_, __) => RefreshPatching();
            ZoneCenterX.SettingChanged += (_, __) => RefreshPatching();
            ZoneCenterZ.SettingChanged += (_, __) => RefreshPatching();
            ZoneRadius.SettingChanged += (_, __) => RefreshPatching();

            RefreshPatching();
        }

        private void RefreshPatching()
        {
            harmony.UnpatchSelf();
            if (!ModEnabled.Value)
            {
                Logger.LogWarning("NoDetectionMod disabled.");
                return;
            }

            if (BlockRadar.Value)
            {
                harmony.Patch(
                    AccessTools.Method(typeof(DetectorManager), "RequestRadarCheck"),
                    new HarmonyMethod(typeof(RadarCheckPatch), nameof(RadarCheckPatch.Prefix)));
                Logger.LogInfo("Radar check patch applied.");
            }

            if (BlockLoS.Value)
            {
                harmony.Patch(
                    AccessTools.Method(typeof(DetectorManager), "RequestLoSCheck"),
                    new HarmonyMethod(typeof(LoSCheckPatch), nameof(LoSCheckPatch.Prefix)));
                Logger.LogInfo("Line-of-sight check patch applied.");
            }
        }
    }

    public static class RadarCheckPatch
    {
        public static bool Prefix(TargetDetector detector, Unit target, IRadarReturn radarReturn)
        {
            //if ((target.definition is not AircraftDefinition))
            //    return false;

            if (target.definition is ShipDefinition or VehicleDefinition or BuildingDefinition)
                return false;

            if (!NoDetectionPlugin.ModEnabled.Value || !NoDetectionPlugin.BlockRadar.Value)
            {
                NoDetectionPlugin.Logger.LogDebug("Radar: mod disabled or radar-block toggle off, allowed.");
                return true;
            }

            GlobalPosition worldPos = target.GlobalPosition();

            float dx = worldPos.x - NoDetectionPlugin.ZoneCenterX.Value;
            float dz = worldPos.z - NoDetectionPlugin.ZoneCenterZ.Value;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);
            bool withinZone = distance <= NoDetectionPlugin.ZoneRadius.Value;
            bool withinAlt = target.radarAlt <= NoDetectionPlugin.MaxAltitude.Value;
            //NoDetectionPlugin.Logger.LogDebug($"Radar debug: {target} distance to zone center = {distance:F1}");
            //NoDetectionPlugin.Logger.LogDebug($"Radar debug:{detector.GetAttachedUnit().GlobalPosition()}  >>  {target.GlobalPosition()}");
            // Zone check
            if (!withinZone)
            {
                //NoDetectionPlugin.Logger.LogDebug($"Radar allowed: outside zone (pos=({worldPos.x:F1},{worldPos.z:F1})).");
                NoDetectionPlugin.Logger.LogInfo($"Radar Detected: {target.unitName} is outside!  Distance: {distance}");
                return true;
            }

            // Altitude check
            if (!withinAlt)
            {
                //NoDetectionPlugin.Logger.LogDebug($"Radar allowed: above max altitude (alt={target.radarAlt:F1}).");
                NoDetectionPlugin.Logger.LogInfo($"Radar Detected: {target.unitName} is too high! Altitude: {target.radarAlt}");
                return true;
            }

            // Both within zone and below altitude => block
            //NoDetectionPlugin.Logger.LogDebug($"Radar blocked:  inside zone at ({worldPos.x:F1},{worldPos.z:F1}) AND below altitude {target.radarAlt:F1}.");
            return false;
        }
    }

    public static class LoSCheckPatch
    {
        public static bool Prefix(TargetDetector detector, Unit target)
        {

            if (target.definition is ShipDefinition or VehicleDefinition or BuildingDefinition)
                return false;

            if (!NoDetectionPlugin.ModEnabled.Value || !NoDetectionPlugin.BlockLoS.Value)
            {
                NoDetectionPlugin.Logger.LogDebug("LoS: mod disabled or LoS-block toggle off, allowed.");
                return true;
            }

            GlobalPosition worldPos = target.GlobalPosition();

            float dx = worldPos.x - NoDetectionPlugin.ZoneCenterX.Value;
            float dz = worldPos.z - NoDetectionPlugin.ZoneCenterZ.Value;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);
            bool withinZone = distance <= NoDetectionPlugin.ZoneRadius.Value;
            bool withinAlt = target.radarAlt <= NoDetectionPlugin.MaxAltitude.Value;

            // Zone check
            if (!withinZone)
            {
                //NoDetectionPlugin.Logger.LogDebug($"LoS   allowed: outside zone (pos=({worldPos.x:F1},{worldPos.z:F1})).");
                NoDetectionPlugin.Logger.LogInfo($"  LoS Detected: {target.unitName} is outside!  Distance: {distance}");
                return true;
            }

            // Altitude check
            if (!withinAlt)
            {
                //NoDetectionPlugin.Logger.LogDebug($"LoS   allowed: above max altitude (alt={target.radarAlt:F1}).");
                NoDetectionPlugin.Logger.LogInfo($"  LoS Detected: {target.unitName} is too high! Altitude: {target.radarAlt}");
                return true;
            }

            // Both within zone and below altitude => block
            //NoDetectionPlugin.Logger.LogDebug($"LoS   blocked:  inside zone at ({worldPos.x:F1},{worldPos.z:F1}) AND below altitude {target.radarAlt:F1}.");
            return false;
        }
    }
}
