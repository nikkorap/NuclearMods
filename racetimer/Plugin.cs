using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace racetimer
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;
        internal static float startTime = -1f;

        internal static readonly List<float> splitTimes = [];
        internal static List<float> bestSplits = [];

        private static string currentRunMission = "";

        private static Dictionary<string, List<float>> BestRuns = [];
        private static string filePath;

        private Harmony _harmony;
        private void Awake()
        {
            Logger = base.Logger;

            filePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".", "RaceTimerRecords.txt");

            LoadFromFile(filePath, out BestRuns);
            _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            _harmony.PatchAll();

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _harmony?.UnpatchSelf();
        }

        private void OnSceneLoaded(Scene _, LoadSceneMode __)
        {
            EvaluateRun(currentRunMission);
            startTime = -1f;
            currentRunMission = "";
            splitTimes.Clear();
            bestSplits = [];
        }

        private void EvaluateRun(string lastmission)
        {
            if (splitTimes.Count == 0) return;
            if (string.IsNullOrEmpty(lastmission)) lastmission = "???"; //in theory this should never happen, but if it does, record it anyway

            BestRuns.TryGetValue(lastmission, out List<float> missionRecord);

            int recordTimes = missionRecord?.Count ?? 0;

            if (splitTimes.Count < recordTimes ||   // incomplete run
               (splitTimes.Count == recordTimes &&  // same length, if current run is longer, consider record as an incomplete run
               (recordTimes > 0 && splitTimes.Last() >= missionRecord.Last()))) return; //not faster, bail

            BestRuns[lastmission] = [.. splitTimes];
            SaveToFile(filePath, BestRuns);

            Logger.LogInfo($"New best run saved for mission '{currentRunMission}'");
        }

        private static void LoadFromFile(string filePath, out Dictionary<string, List<float>> data)
        {

            data = [];
            try
            {
                // create file if no file exists
                if (!File.Exists(filePath))
            {
                File.WriteAllText(filePath, string.Empty);
                return;
            }

            foreach (string line in File.ReadAllLines(filePath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                string[] parts = line.Split(',');
                if (parts.Length <= 1) continue;

                string key = parts[0];
                var values = new List<float>();

                for (int i = 1; i < parts.Length; i++)
                    if (float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                        values.Add(v);

                if (values.Count > 0)
                    data[key] = values;
            }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to read records file: {ex}");
            }
        }
        private static void SaveToFile(string filePath, Dictionary<string, List<float>> data)
        {
            try
            {
                var lines = data.Select(kv => kv.Key + "," + string.Join(",", kv.Value.Select(t => t.ToString("F3", CultureInfo.InvariantCulture))));
                File.WriteAllLines(filePath, lines);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to write records file: {ex}");
            }
        }

        [HarmonyPatch(typeof(MissionMessages), nameof(MissionMessages.ShowMessage))]
        private static class ShowMessagePatch
        {
            private static void Prefix(ref string message)
            {
                if (startTime < 0f)
                {
                    startTime = Time.time;
                    currentRunMission = MissionManager.CurrentMission?.Name ?? "Unknown mission";
                    BestRuns.TryGetValue(currentRunMission, out List<float> record);
                    bestSplits = record != null ? [.. record] : [];
                    Logger.LogDebug($"Race started at {startTime:F3}  (mission: {currentRunMission})");
                    return;
                }

                float elapsed = Time.time - startTime;
                splitTimes.Add(elapsed);

                // time text formatted
                int m = Mathf.FloorToInt(elapsed / 60f);
                float s = elapsed - m * 60f;
                string splitTxt = $"{m:00}:{s:00.000}";

                //split time formatted, if available
                string diffTxt = "";
                int idx = splitTimes.Count - 1;
                if (idx < bestSplits.Count)
                {
                    float diff = elapsed - bestSplits[idx];
                    string color = diff < -0.001f ? "#00FF00FF"    // green faster
                                 : diff > 0.001f ? "#FF0000FF"     // red slower
                                 : "#FFFF00FF";                     // yellow even
                    diffTxt = $" <color={color}>{(diff >= 0 ? "+" : "")}{diff:0.000}</color>";
                }

                message += $"  {splitTxt}{diffTxt}";
            }
        }
    }
}
