using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace WeatherSetter
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance;
        private static ManualLogSource Logger;

        private ConfigEntry<float> timeOfDay;
        private ConfigEntry<float> conditions;
        private ConfigEntry<float> cloudHeight;
        private ConfigEntry<Vector3> windVelocity;
        private ConfigEntry<float> windTurbulence;
        private ConfigEntry<float> windSpeed;

        private LevelInfo levelInfoInstance;

        private void Awake()
        {
            this.hideFlags = HideFlags.HideAndDontSave;
            Logger = base.Logger;
            Instance = this;
            timeOfDay = Config.Bind("Environment", "TimeOfDay", 12f, new ConfigDescription("Time of Day (0–24)", new AcceptableValueRange<float>(0f, 24f)));
            conditions = Config.Bind("Environment", "Conditions", 0.1f, new ConfigDescription("Weather Conditions (0–1)", new AcceptableValueRange<float>(0f, 10f)));
            cloudHeight = Config.Bind("Environment", "CloudHeight", 1500f, new ConfigDescription("Cloud Height (500–4000)", new AcceptableValueRange<float>(0f, 40000f)));
            windVelocity = Config.Bind("Environment", "WindVelocity", new Vector3(0f, 0f, 0f), "Wind direction and strength vector");
            windTurbulence = Config.Bind("Environment", "WindTurbulence", 0.1f, new ConfigDescription("Wind Turbulence (0–1)", new AcceptableValueRange<float>(0f, 10f)));
            windSpeed = Config.Bind("Environment", "WindSpeed", 10f, new ConfigDescription("Wind Speed (0–72)", new AcceptableValueRange<float>(0f, 1000f)));

            timeOfDay.SettingChanged += (_, _) => { if (levelInfoInstance) levelInfoInstance.NetworktimeOfDay = timeOfDay.Value; };
            conditions.SettingChanged += (_, _) => { if (levelInfoInstance) levelInfoInstance.Networkconditions = conditions.Value; };
            cloudHeight.SettingChanged += (_, _) => { if (levelInfoInstance) levelInfoInstance.NetworkcloudHeight = cloudHeight.Value; };
            windVelocity.SettingChanged += (_, _) => { if (levelInfoInstance) levelInfoInstance.NetworkwindVelocity = windVelocity.Value; };
            windTurbulence.SettingChanged += (_, _) => { if (levelInfoInstance) levelInfoInstance.NetworkwindTurbulence = windTurbulence.Value; };
            windSpeed.SettingChanged += (_, _) => { if (levelInfoInstance) levelInfoInstance.NetworkwindSpeed = windSpeed.Value; };

            new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll();
            Logger.LogInfo("Plugin started.");
        }

        [HarmonyPatch(typeof(LevelInfo), "Awake")]
        class Patch_LevelInfo_Awake
        {
            static void Postfix(LevelInfo __instance)
            {
                Instance.levelInfoInstance = __instance;

            }
        }
    }
}
