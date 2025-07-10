using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace truefactioncolours
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID,
                 MyPluginInfo.PLUGIN_NAME,
                 MyPluginInfo.PLUGIN_VERSION)]
    public class GameAssetsColorMod : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        private Harmony _harmony;

        private void Awake() 
        {
            Log = Logger;
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

                var otherHQ = Object.FindObjectsOfType<FactionHQ>() .FirstOrDefault(hq => hq != newHQ && hq.faction != null && hq.faction != newHQ.faction);
                if (otherHQ == null)
                {
                    Log.LogWarning("Could not locate opposing FactionHQ - colors not changed.");
                    return;
                }

                GameAssets ga = GameAssets.i ?? Resources.FindObjectsOfTypeAll<GameAssets>() .FirstOrDefault(a => a != null);
                if (ga == null)
                {
                    Log.LogWarning("No GameAssets instance found.");
                    return;
                }

                ga.HUDFriendly = newHQ.faction.color;
                ga.HUDHostile = otherHQ.faction.color;

                Log.LogInfo($"HUD colours updated - Friendly:{newHQ.faction}  Hostile:{otherHQ.faction}");
            }
        }
    }
}
