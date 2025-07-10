using System.Collections;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using pinkenemies;
using UnityEngine;

namespace test
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class GameAssetsColorMod : BaseUnityPlugin
    {
        private ConfigEntry<Color> HUDHostile;
        private Harmony _harmony;

        private void Awake()
        {
            HUDHostile = Config.Bind("Colors", "HUDHostile", new Color(1f, 0f, 1f));

            StartCoroutine(TryEditGameAsset());

            _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            _harmony.PatchAll();
        }

        private IEnumerator TryEditGameAsset()
        {
            while (true) 
            {
                yield return new WaitForSeconds(1f);

                var ga = Resources.LoadAll<GameAssets>(string.Empty).FirstOrDefault(a => a != null);

                if (ga != null)
                {
                    Logger.LogWarning("Editing GameAssets colours from config");
                    ga.HUDHostile = HUDHostile.Value;
                    yield break;
                }
                Logger.LogWarning("GameAssets not found. retrying...");
            }
        }
    }
}
