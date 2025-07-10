using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OneLifeTest
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        private Harmony harmony;
        private static ManualLogSource Logger;

        private static ConfigEntry<bool> ModEnabled;
        private static ConfigEntry<int> MaxLives;

        private static ConfigEntry<string> AllowedAirframes;
        private static ConfigEntry<string> AllowedWeapons;

        private static ConfigEntry<bool> RestrictedSound;
        private static ConfigEntry<bool> RescuedSound;
        internal static Plugin Instance { get; private set; }
        private static readonly Dictionary<ulong, PilotData> PilotMap = []; //netstandard2.1 

        private class PilotData
        {
            public int Lives;
            public int PilotsOnBoard;
            public bool Restricted;
        }

        private void Awake()
        {
            Logger = base.Logger;
            Instance = this;
            harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);

            ModEnabled = Config.Bind("General", "_ModEnabled", true);
            MaxLives = Config.Bind("General", "Max Lives", 3);
            AllowedAirframes = Config.Bind("Whitelist", "AllowedAirframes", "SAH-46 Chicane, VL-49 Tarantula");
            AllowedWeapons = Config.Bind("Whitelist", "AllowedWeapons", "30mm HE x750, 12.7mm Rotary Turret, ECM Pod");

            RestrictedSound = Config.Bind("Messages", "RestrictedSound", true, "Play a sound on restriction");
            RescuedSound = Config.Bind("Messages", "RescuedSound", true, "Play a sound when restriction is lifted");
            SceneManager.sceneLoaded += OnSceneLoaded;
            ModEnabled.SettingChanged += (_, __) => ToggleMod(ModEnabled.Value);
            ToggleMod(ModEnabled.Value);
        }

        private void ToggleMod(bool enable)
        {
            if (enable)
            {
                harmony.PatchAll();
                Logger.LogInfo("enabled");
            }
            else
            {
                harmony.UnpatchSelf();
                PilotMap.Clear();
                _chat = null;
                _host = null;
                Logger.LogInfo("disabled");
            }
        }
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            PilotMap.Clear();

            Logger.LogInfo($"reset on scene load (\"{scene.name}\").");
        }

        private static PilotData Get(ulong steamId)
        {
            if (!PilotMap.TryGetValue(steamId, out var data))
            {
                data = new PilotData { Lives = MaxLives.Value, PilotsOnBoard = 0, Restricted = MaxLives.Value <= 0 };
                PilotMap[steamId] = data;
            }
            return data;
        }

        private ChatManager _chat;
        private Player _host; 
        public void Whisper(Player target, string text)
        {
            _chat ??= FindObjectOfType<ChatManager>();
            _host ??= FindObjectsOfType<Player>().FirstOrDefault(p => p.IsHost);

            if (_chat != null && _host != null && target != null)
            {
                _chat.TargetReceiveMessage(target.Owner, text, _host, true);  //game method for directed messages
                Logger.LogInfo($"Whispered to {target.PlayerName}: \"{text}\"");
            }
            else
            {
                Logger.LogWarning($"Unable to whisper to {target?.PlayerName ?? "null"}; ChatManager or host missing");
            }
        }

        [HarmonyPatch(typeof(Unit), "ReportKilled")]
        class CrashPatch
        {
            static void Postfix(Unit __instance)
            {
                if (!ModEnabled.Value) return;
                if (__instance is Aircraft ac && ac.Player is Player p)
                {
                    var data = Plugin.Get(p.SteamID);
                    data.Lives = Math.Max(0, data.Lives - 1);
                    data.PilotsOnBoard = 0;

                    if (data.Lives <= 0)
                    {
                        if (!data.Restricted)
                        {
                            data.Restricted = true;
                            var msg = $"<color=#008FFFFF>{p.PlayerName}</color> lost their last life and is now RESTRICTED";
                            MissionMessages.ShowMessage(msg, RestrictedSound.Value, p.HQ, true);
                            Logger.LogInfo(msg);
                        }
                        else
                        {
                            var msg = $"<color=#008FFFFF>{p.PlayerName}</color> died, again...";
                            MissionMessages.ShowMessage(msg, RestrictedSound.Value, p.HQ, true);
                            Logger.LogInfo(msg);
                        }
                    }
                    else
                    {
                        var msg = $"<color=#008FFFFF>{p.PlayerName}</color> crashed, {data.Lives} Lives left";
                        MissionMessages.ShowMessage(msg, RestrictedSound.Value, p.HQ, true);
                        Logger.LogInfo(msg);
                    }
                }
            }
        }
        [HarmonyPatch(typeof(FactionHQ), "ReportRescuePilotsAction")]
        class RescuePatch
        {
            static void Postfix(Player player, PilotDismounted pilotDismounted)
            {
                if (!ModEnabled.Value || player == null || pilotDismounted == null) return;

                var data = Plugin.Get(player.SteamID);
                data.PilotsOnBoard++;
                Logger.LogDebug($"{player.PlayerName} has now {data.PilotsOnBoard} rescued pilot(s) aboard.");
            }
        }

        [HarmonyPatch(typeof(Aircraft), "SuccessfulSortie")]
        class SortiePatch
        {
            static void Postfix(Aircraft __instance)
            {
                if (!ModEnabled.Value || __instance.Player is not Player p) return;

                var data = Plugin.Get(p.SteamID);
                if (data.PilotsOnBoard == 0) return;

                data.Lives = Mathf.Clamp(data.Lives + data.PilotsOnBoard, 0, MaxLives.Value);

                if (data.Lives > 0)
                {
                    data.Restricted = false;
                    var msg = $"<color=#008FFFFF>{p.PlayerName}</color> rescued {data.PilotsOnBoard} pilots, they now have {data.Lives} lives";
                    MissionMessages.ShowMessage(msg, RescuedSound.Value, p.HQ, true);
                    Logger.LogInfo(msg);
                }
                data.PilotsOnBoard = 0;
            }
        }

        [HarmonyPatch(typeof(Airbase), "TrySpawnAircraft")]
        class TrySpawnPatch
        {
            static bool Prefix(ref Airbase.TrySpawnResult __result, Player player, AircraftDefinition definition, object livery, Loadout loadout)
            {
                if (!ModEnabled.Value || player == null || loadout?.weapons == null)
                {
                    return true;
                }
                var data = Plugin.Get(player.SteamID);
                if (!data.Restricted) 
                {
                    return true;
                }
                if (!AllowedAirframes.Value.Contains(definition.unitName, StringComparison.OrdinalIgnoreCase))
                {
                    __result = new Airbase.TrySpawnResult(false, __result.Hangar, __result.DelayedSpawn);
                    Plugin.Instance.Whisper(player, $"<color=#008FFFFF>{definition.unitName}</color> is restricted. Currently allowed: {AllowedAirframes.Value}");
                    Logger.LogInfo($"Blocked restricted pilot {player.PlayerName} from spawning in “{definition.unitName}”.");
                    return false;
                }
                foreach (var mount in loadout.weapons)
                {
                    if (mount == null || string.IsNullOrEmpty(mount.mountName)) continue;
                    if (!Plugin.AllowedWeapons.Value.Contains(mount.mountName, StringComparison.OrdinalIgnoreCase))
                    {
                        __result = new Airbase.TrySpawnResult(false, __result.Hangar, __result.DelayedSpawn);
                        Plugin.Instance.Whisper(player, $"<color=#008FFFFF>{mount.mountName}</color> is restricted. Currently allowed: {AllowedWeapons.Value}");
                        Logger.LogInfo($"Blocked restricted pilot {player.PlayerName} from spawning with “{mount.mountName}”.");
                        return false;
                    }
                }
                return true;
            }
        }
    }
}