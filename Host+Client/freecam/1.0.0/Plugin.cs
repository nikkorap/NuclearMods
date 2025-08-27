using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace freecam
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;
        private Harmony _harmony;

        private ConfigEntry<KeyboardShortcut> _toggleKey;

        private void Awake()
        {
            _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            _harmony.PatchAll();
            Logger = base.Logger;

            _toggleKey = Config.Bind("General","Toggle freecam",new KeyboardShortcut(KeyCode.F6));

            Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
        }

        private void Update()
        {
            if (!_toggleKey.Value.IsDown()) return;
            if (!TryToggleCameraState()) Logger.LogDebug("Toggle skipped: manager missing.");
        }

        private bool TryToggleCameraState()
        {
            CameraStateManager mgr = FindObjectOfType<CameraStateManager>();
            if (mgr == null) return false;
           
            if (mgr.currentState == mgr.freeState)
            {
                mgr.SwitchState(mgr.orbitState);
                Logger.LogInfo("Switched to orbitState.");
            }
            else
            {
                mgr.SwitchState(mgr.freeState);
                Logger.LogInfo("Switched to freeState.");
            }
            return true;
        }


        [HarmonyPatch(typeof(Application), nameof(Application.version), MethodType.Getter)] 
        private class VersionGetterPatch
        {
            static void Postfix(ref string __result)
            {
                __result += $"_{MyPluginInfo.PLUGIN_GUID}-v{MyPluginInfo.PLUGIN_VERSION}";
                Logger.LogWarning($"Updated game version to {__result}");
            }
        }
    }
}
