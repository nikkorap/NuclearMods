using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using pinkenemies;
using UnityEngine;

namespace truefactioncolours
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        private ConfigEntry<Color> HUDHostile;

        internal static ManualLogSource Log;
        private Harmony _harmony;
        internal static Plugin Instance;
        private void Awake()
        {
            Instance = this;
            Log = Logger;
            HUDHostile = Config.Bind("Colors", "HUDHostile", new Color(1f, 0f, 1f));
            _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            _harmony.PatchAll();
        }

        [HarmonyPatch(typeof(Player), nameof(Player.SetFaction))]
        private static class Player_SetFaction_Postfix
        {
            private static void Postfix(Player __instance, FactionHQ newHQ)
            {
                if (__instance == null || !__instance.IsLocalPlayer || newHQ == null)
                    return;

                var otherHQ = FindObjectsOfType<FactionHQ>().FirstOrDefault(hq => hq != newHQ && hq.faction != null && hq.faction != newHQ.faction);
                if (otherHQ == null)
                {
                    Log.LogWarning("Could not locate opposing FactionHQ - colors not changed.");
                    return;
                }

                GameAssets ga = GameAssets.i ?? Resources.FindObjectsOfTypeAll<GameAssets>().FirstOrDefault(a => a != null);
                if (ga == null)
                {
                    Log.LogWarning("No GameAssets instance found.");
                    return;
                }

                ga.HUDHostile = Instance.HUDHostile.Value;
                Log.LogInfo($"HUD colours updated Hostile:{otherHQ.faction}");
            }
        }
    }
}
