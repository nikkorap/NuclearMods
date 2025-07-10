using System.IO;
using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using BepInEx.Logging;
using BepInEx.Configuration;

namespace jamsoundreplacer
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class jamsoundreplacer : BaseUnityPlugin
    {
        public static jamsoundreplacer Instance { get; private set; }
        public static ManualLogSource Log => Instance.Logger;
        private Harmony _harmony;

        public static AudioClip customClip;
        private ConfigEntry<int> _volume;
        private void Awake()
        {
            Instance = this;
            customClip = LoadWav();
            _volume = Config.Bind("General","Voiceline Volume",100,new ConfigDescription("",new AcceptableValueRange<int>(0, 100)));
            _volume.SettingChanged += (_, __) => UpdateAllVolumeMultipliers();
            _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            _harmony.PatchAll();

        }

        private static AudioClip LoadWav()
        {
            string path = Directory.EnumerateFiles(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "*.wav").FirstOrDefault();
            if (string.IsNullOrEmpty(path))
            {
                Log.LogWarning("NO PATH");
                return null;
            }
            var data = File.ReadAllBytes(path);
            if (data.Length < 44)
                return null;

            // --- RIFF header parsing ---
            int channels = BitConverter.ToInt16(data, 22);
            int sampleRate = BitConverter.ToInt32(data, 24);
            int bitsPerSample = BitConverter.ToInt16(data, 34);

            // Only support 16-bit or 8-bit PCM
            if (bitsPerSample != 16 && bitsPerSample != 8)
                return null;

            int headerSize = 44;
            if (bitsPerSample == 16)
            {
                // Each sample = 2 bytes (Int16)
                int sampleCount = (data.Length - headerSize) / 2;
                var floatArray = new float[sampleCount];

                for (int i = 0; i < sampleCount; i++)
                {
                    short s = BitConverter.ToInt16(data, headerSize + i * 2);
                    floatArray[i] = s / 32768f;
                }

                // clip length = (total samples) / channels
                int frames = sampleCount / channels;
                var clip = AudioClip.Create(
                    Path.GetFileNameWithoutExtension(path),
                    frames,
                    channels,
                    sampleRate,
                    false
                );
                clip.SetData(floatArray, 0);

                Log.LogInfo("Loaded clip " + Path.GetFileNameWithoutExtension(path));
                return clip;
            }
            else // bitsPerSample == 8
            {
                // Each sample = 1 byte (unsigned). Range 0..255 → convert to –1..+1
                int sampleCount = data.Length - headerSize;
                var floatArray = new float[sampleCount];

                for (int i = 0; i < sampleCount; i++)
                {
                    // byte value in [0..255], zero‐offset → signed in [–128..+127]
                    int unsignedByte = data[headerSize + i];
                    int signed = unsignedByte - 128;        // now in –128..+127
                    floatArray[i] = signed / 128f;          // now in –1.0 .. +0.9921875
                }

                int frames = sampleCount / channels;
                var clip = AudioClip.Create(
                    Path.GetFileNameWithoutExtension(path),
                    frames,
                    channels,
                    sampleRate,
                    false
                );
                clip.SetData(floatArray, 0);
                return clip;
            }
        }

        private void UpdateAllVolumeMultipliers()
        {
            var fi = typeof(CombatHUD).GetField("jammedVolumeMultiplier", BindingFlags.Instance | BindingFlags.NonPublic);
            if (fi == null)
            {
                Log.LogError("Couldn't find private field 'jammedVolumeMultiplier' on CombatHUD.");
                return;
            }

            var hud = FindObjectsOfType<CombatHUD>().FirstOrDefault();
            if (hud == null)
            {
                Log.LogError("NO COMBATHUD");
                return;
            }
            try
            {
                fi.SetValue(hud, _volume.Value / 100f);
                Log.LogInfo($"Set Jammed volume to {_volume.Value}%");
            }
            catch (Exception e)
            {
                Log.LogError($"Exception while setting jammedVolumeMultiplier → {e}");
            }

        }

        [HarmonyPatch(typeof(CombatHUD), nameof(CombatHUD.SetPlayerFaction))]
        private static class CombatHUD_Postfix
        {
            static void Postfix(CombatHUD __instance)
            {
                if (customClip == null && LoadWav() == null)
                {
                    Log.LogError("failed to load replacement clip");
                    return;
                }
                var jammedField = typeof(CombatHUD).GetField("jammedSound", BindingFlags.Instance | BindingFlags.NonPublic);
                if (jammedField == null)
                {
                    Log.LogError("Couldn't find private field 'JammedSound' on CombatHUD.");
                    return;
                }

                try
                {
                    jammedField.SetValue(__instance, customClip);
                    Log.LogInfo("Successfully replaced CombatHUD.JammedSound.");
                }
                catch (Exception e)
                {
                    Log.LogError($"Exception while setting JammedSound → {e}");
                }
                jamsoundreplacer.Instance.UpdateAllVolumeMultipliers();
            }
        }
    }
}
