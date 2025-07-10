using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ttsplus
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME,MyPluginInfo.PLUGIN_VERSION)]
    public class MissionTTSPlugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;
        private Harmony _harmony;
        private ConfigEntry<bool> _enabled;

        private void Awake()
        {
            this.hideFlags = HideFlags.HideAndDontSave;
            Logger = base.Logger;

            _enabled = Config.Bind("General", "Enabled", true);
            _enabled.SettingChanged += (_, __) => OnEnabledChanged();

            _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            OnEnabledChanged();
        }

        private void OnEnabledChanged()
        {
            if (_enabled.Value)
            {
                _harmony.PatchAll();
            }
            else
            {
                _harmony.UnpatchSelf();
            }
        }

        [HarmonyPatch(typeof(MissionMessages), nameof(MissionMessages.ShowMessage))]
        private class ShowMessage_Patch
        {
            static void Postfix(string message, bool playsound, object faction, bool sendToClients)
            {
                try
                {
               //     if (!PlayerSettings.chatTts) return;
                    int speed = PlayerSettings.chatTtsSpeed;
                    int volume = PlayerSettings.chatTtsVolume; 
                    bool filter = PlayerSettings.chatFilter;

                    WindowsTTS.SpeakAsync(speed, volume, message, filter);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"TTS failed: {ex}");
                }
            }                                        
        }
    }
}
                                                 