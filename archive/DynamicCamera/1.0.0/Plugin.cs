

using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace DynamicCamera
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class VelocityViewPlugin : BaseUnityPlugin
    {
        internal static VelocityViewPlugin Instance { get; private set; }
        public enum VelocityViewMode
        {
            Off = 0,
            Toward = 1,
            Away = 2
        }
        private ConfigEntry<KeyCode> _cycleKey;
        public ConfigEntry<VelocityViewMode> _mode;

        public ConfigEntry<float> _smoothTime;
        public ConfigEntry<float> _fullEffectSpeed;
        public ConfigEntry<float> _offsetMultiplier;
        public ConfigEntry<float> _maxPanDeg;
        public ConfigEntry<float> _maxTiltDeg;

        public ConfigEntry<bool> _modEnabled;
        private static Harmony _harmony;
        private static bool _patched;
        private void Awake()
        {
            hideFlags = HideFlags.HideAndDontSave;
            Instance = this;
            _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);

            _modEnabled = Config.Bind("General", "Enabled", true, "Master on/off switch for velocity view");
            _modEnabled.SettingChanged += (_, __) => ToggleMod(_modEnabled.Value);

            _mode = Config.Bind("General", "View mode", VelocityViewMode.Off, "0-Off, 1-Toward velocity, 2-Away from velocity");
            _cycleKey = Config.Bind("Keys", "Cycle key", KeyCode.V, "Press to cycle velocity-view modes");
            _smoothTime = Config.Bind("General", "Smoothing (sec)", 0.2f, "0 = snap; 0.1-0.3 is typical   (time for SmoothDamp to reach ~63 % of the target)");
            _fullEffectSpeed = Config.Bind("General", "Full Effect Speed", 100f, "minimum speed for full effect");
            _offsetMultiplier = Config.Bind("General", "OffsetMultiplier", 0.5f, "Multiply the velocity-vector offset after speed scaling (1 = 100 %).");
            _maxPanDeg = Config.Bind("General", "MaxPanDeg", 165f, new ConfigDescription("", null, new AcceptableValueRange<float>(0f, 165f)));
            _maxTiltDeg = Config.Bind("General", "MaxTiltDeg", 65f, new ConfigDescription("", null, new AcceptableValueRange<float>(0f, 65f)));
            ToggleMod(_modEnabled.Value);
        }
        private static void ToggleMod(bool on)
        {
            if (on && !_patched)
            {
                _harmony.PatchAll();
                _patched = true;
                CockpitStatePatch.ResetState();
            }
            else if (!on && _patched)
            {
                Instance = null;
                _harmony.UnpatchSelf();
                _patched = false;
                CockpitStatePatch.ResetState();
            }
        }

        private void Update()
        {
            if (!_patched) return;
            if (Input.GetKeyDown(_cycleKey.Value))
            {
                _mode.Value = (VelocityViewMode)(((int)_mode.Value + 1) % 3);

                string message = _mode.Value switch
                {
                    VelocityViewMode.Off => "Velocity View: OFF",
                    VelocityViewMode.Toward => "Velocity View: Direction",
                    VelocityViewMode.Away => "Velocity View: Ahead",
                    _ => "Velocity View"
                };

                Logger.LogDebug(message);
                var reporter = SceneSingleton<AircraftActionsReport>.i;
                reporter?.ReportText(message, 2f);
            }
        }
        private void OnDisable() => ToggleMod(false);
    }

    [HarmonyPatch(typeof(CameraCockpitState), "UpdateState")]
    static class CockpitStatePatch
    {
        static readonly FieldInfo fAircraft = AccessTools.Field(typeof(CameraCockpitState), "aircraft");
        static readonly FieldInfo fPan = AccessTools.Field(typeof(CameraCockpitState), "panView");
        static readonly FieldInfo fTilt = AccessTools.Field(typeof(CameraCockpitState), "tiltView");

        static float _offPan, _offTilt;
        static float _velPan, _velTilt;
        static int _lastMode;
        static bool _init;
        internal static void ResetState()
        {
            _offPan = _offTilt = 0f;
            _velPan = _velTilt = 0f;
            _init = false;
        }
        static void Prefix(CameraCockpitState __instance)
        {
            var mode = VelocityViewPlugin.Instance._mode.Value;

            float gamePan = (float)fPan.GetValue(__instance);
            float gameTilt = (float)fTilt.GetValue(__instance);

            float baselinePan = gamePan - _offPan;
            float baselineTilt = gameTilt - _offTilt;


            if (mode == VelocityViewPlugin.VelocityViewMode.Off)
            {
                _offPan = _offTilt = _velPan = _velTilt = 0f;
                _init = false;
                fPan.SetValue(__instance, baselinePan);
                fTilt.SetValue(__instance, baselineTilt);
                return;
            }

            var aircraft = fAircraft.GetValue(__instance) as Aircraft;
            if (aircraft == null) return;

            var rb = aircraft.CockpitRB();
            if (rb == null) return;

            Vector3 vel = rb.velocity;
            if (vel.sqrMagnitude < 1f) return;

            Vector3 local = aircraft.transform.InverseTransformDirection(vel);

            float tgtPan = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float tgtTilt = -Mathf.Atan2(local.y,
                           new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;

            if (mode == VelocityViewPlugin.VelocityViewMode.Away)
            {
                tgtPan = -tgtPan;
                tgtTilt = -tgtTilt;
            }

            float speedFactor = Mathf.Clamp01(vel.magnitude / VelocityViewPlugin.Instance._fullEffectSpeed.Value);
            tgtPan *= speedFactor * VelocityViewPlugin.Instance._offsetMultiplier.Value;
            tgtTilt *= speedFactor * VelocityViewPlugin.Instance._offsetMultiplier.Value;

            if (!_init || _lastMode != (int)mode)
            {
                _offPan = tgtPan;
                _offTilt = tgtTilt;
                _velPan = _velTilt = 0f;
                _init = true;
                _lastMode = (int)mode;
            }

            float t = Mathf.Max(0.0001f, VelocityViewPlugin.Instance._smoothTime.Value);
            _offPan = Mathf.SmoothDamp(_offPan, tgtPan, ref _velPan, t);
            _offTilt = Mathf.SmoothDamp(_offTilt, tgtTilt, ref _velTilt, t);

            float panClamp = Mathf.Abs(VelocityViewPlugin.Instance._maxPanDeg.Value);
            float tiltClamp = Mathf.Abs(VelocityViewPlugin.Instance._maxTiltDeg.Value);

            float finalPan = Mathf.Clamp(baselinePan + _offPan, -panClamp, panClamp);
            float finalTilt = Mathf.Clamp(baselineTilt + _offTilt, -tiltClamp, tiltClamp);

            fPan.SetValue(__instance, finalPan);
            fTilt.SetValue(__instance, finalTilt);
        }
    }
}