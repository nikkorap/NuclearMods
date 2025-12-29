using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using HarmonyLib.Tools;
using UnityEngine;

namespace Ranvil
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID,MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;
        private Harmony _harmony;
        private ConfigEntry<bool> modEnabled;

        private void Awake()
        {
            this.hideFlags = HideFlags.HideAndDontSave;
            Log = Logger;
            modEnabled = Config.Bind("Settings", "Enable mod", true);
            modEnabled.SettingChanged += (_, __) => ToggleMod(modEnabled.Value);
            _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            _harmony.PatchAll();
            ToggleMod(modEnabled.Value);
        }

        private void ToggleMod(bool enable)
        { 
            if(enable)
            {
                Log.LogInfo("SPAAG-Destroyer swap loaded");
                _harmony.PatchAll();
            }
            else
            {
                _harmony?.UnpatchSelf();
            }
        }
        private void OnDestroy() => _harmony?.UnpatchSelf();

        [HarmonyPatch(typeof(Application), nameof(Application.version), MethodType.Getter)]
        private static class VersionGetterPatch
        {
            static void Postfix(ref string __result)
            {
                __result += $"_{MyPluginInfo.PLUGIN_GUID}-v{MyPluginInfo.PLUGIN_VERSION}";
                Log.LogWarning($"Updated game version to {__result}");
            }
        }
    }
    [HarmonyPatch(typeof(global::Unit), nameof(global::Unit.Awake))]
    internal static class UnitAwakePatch
    {
        private const string SPAAG_NAME_PREFIX = "SPAAG2";
        private const string SPAAG_TURRET_PATH = "turret";
        private const string SPAAG_GUN_PATH = "turret/gun";
        private const string DESTROYER_CANNON_PATH ="Hull_CF/Hull_CFF/turret_F/cannon_F";
        private static GameObject _destroyerCannonPrefab;
        private static readonly HashSet<WeaponInfo> _patchedInfos = new();

        static void Postfix(global::Unit __instance)
        {
            try
            {
                ReplaceGunIfSpaag(__instance.gameObject);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SPAAG swap] exception:\n{ex}");
            }
        }

        private static void ReplaceGunIfSpaag(GameObject unitGO)
        {
            if (!unitGO.name.StartsWith(SPAAG_NAME_PREFIX, StringComparison.OrdinalIgnoreCase))
                return;

            Transform turretT = unitGO.transform.Find(SPAAG_TURRET_PATH);
            Transform oldGunT = unitGO.transform.Find(SPAAG_GUN_PATH);
            if (turretT == null || oldGunT == null) return;  
            if (turretT.Find("DestroyerCannon") != null) return;
            if (!EnsureCannonPrefab()) return;
            GameObject newGunGO = UnityEngine.Object.Instantiate(_destroyerCannonPrefab);
            newGunGO.name = "DestroyerCannon";
            newGunGO.transform.SetParent(turretT, worldPositionStays: false);
            Debug.Log(newGunGO.transform.localPosition);
            Debug.Log(oldGunT.localPosition);
            newGunGO.transform.localPosition = oldGunT.localPosition;
            newGunGO.transform.localRotation = oldGunT.localRotation;

            Weapon spaagWeapon = oldGunT.GetComponent<Weapon>();
            Weapon railWeapon = newGunGO.GetComponent<Weapon>();
            EnsureTargetReqPatched(spaagWeapon?.info, railWeapon?.info);
            Turret turretComp = turretT.GetComponent<Turret>();
            if (turretComp == null)
            {
                Plugin.Log.LogWarning("Turret component missing on SPAAG turret.");
                return;
            }

            WeaponStation ws = turretComp.GetWeaponStations()[0];
            if (ws == null) return;
            if (ws.weapons is IList<Weapon> list)
            {
                if (list.Count == 0) list.Add(railWeapon);
                else list[0] = railWeapon;
            }
            else
            {
                Plugin.Log.LogWarning("WeaponStation.weapons not IList<Weapon>; unable to swap.");
            }

            if (railWeapon?.info != null)
                ws.weaponInfo = railWeapon.info;

            UnityEngine.Object.Destroy(oldGunT.gameObject);

            Plugin.Log.LogInfo($"[{unitGO.name}] swapped SPAAG gun for destroyer cannon.");
        }
        private static bool EnsureCannonPrefab()
        {
            if (_destroyerCannonPrefab != null) return true;

            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go.name != "Destroyer1") continue;

                Transform cannonT = go.transform.Find(DESTROYER_CANNON_PATH);
                if (cannonT == null) continue;

                _destroyerCannonPrefab = cannonT.gameObject;
                Plugin.Log.LogInfo("Cached destroyer cannon prefab (Resources scan).");
                return true;
            }
            Plugin.Log.LogDebug("Destroyer prefab not present yet; will retry on next SPAAG.");
            return false;
        }
        private static void EnsureTargetReqPatched(WeaponInfo spaagInfo, WeaponInfo railInfo)
        {
            if (spaagInfo == null || railInfo == null) return;
            if (_patchedInfos.Contains(railInfo)) return;    

            railInfo.targetRequirements = spaagInfo.targetRequirements;
            railInfo.targetRequirements.maxRange = 50000;
            _patchedInfos.Add(railInfo);

            Plugin.Log.LogDebug($"WeaponInfo '{railInfo.name}' targetRequirements patched.");
        }
    }
}
