using System;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DropDownScroll
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class DropdownScrollPlugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;
        private Harmony harmony;
        private ConfigEntry<bool> ModEnabled;
        private ConfigEntry<bool> InvertScroll;
        private ConfigEntry<bool> WrapAround;

        private void Awake()
        {
            this.hideFlags = HideFlags.HideAndDontSave;
            Logger = base.Logger;
            harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);

            ModEnabled = Config.Bind("General", "Enable Mod", true, "Enable or disable dropdown scroll selector");
            InvertScroll = Config.Bind("General", "Invert Scroll Direction", false, new ConfigDescription("", null, new ConfigurationManagerAttributes { IsAdvanced = true }));
            WrapAround = Config.Bind("General", "Wrap Around", false, new ConfigDescription("", null, new ConfigurationManagerAttributes { IsAdvanced = true }));
            ModEnabled.SettingChanged += (_, __) => UpdatePatches();

            UpdatePatches();
        }
        internal class ConfigurationManagerAttributes
        {
            public bool? Browsable;
            public bool? IsAdvanced;
        }
        private void UpdatePatches()
        {
            if (ModEnabled.Value)
            {
                harmony.PatchAll();
                Logger.LogInfo("DropdownScroll mod enabled");
            }
            else
            {
                harmony.UnpatchSelf();
                Logger.LogInfo("DropdownScroll mod disabled");
            }
        }

        private void OnDestroy()
        {
            harmony.UnpatchSelf();
        }

        // Patch TMP_Dropdown.Awake to attach scroll listener
        [HarmonyPatch(typeof(TMP_Dropdown), "Awake")]
        private static class Patch_TmpDropdownAwake
        {
            static void Postfix(TMP_Dropdown __instance)
            {
                if (!((DropdownScrollPlugin)Instance).ModEnabled.Value)
                    return;

                if (__instance.gameObject.GetComponent<ScrollSelector>() == null)
                {
                    var comp = __instance.gameObject.AddComponent<ScrollSelector>();
                    comp.Initialize(__instance,
                        ((DropdownScrollPlugin)Instance).InvertScroll,
                        ((DropdownScrollPlugin)Instance).WrapAround);
                }
            }

            // Helper to access plugin instance
            private static BaseUnityPlugin Instance => BepInEx.Bootstrap.Chainloader.ManagerObject.GetComponent<DropdownScrollPlugin>();
        }

        private class ScrollSelector : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IScrollHandler
        {
            private TMP_Dropdown _dropdown;
            private bool _hovered;
            private ConfigEntry<bool> _invert;
            private ConfigEntry<bool> _wrap;

            public void Initialize(TMP_Dropdown dropdown, ConfigEntry<bool> invertScroll, ConfigEntry<bool> wrapAround)
            {
                _dropdown = dropdown;
                _invert = invertScroll;
                _wrap = wrapAround;
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                _hovered = true;
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                _hovered = false;
            }

            public void OnScroll(PointerEventData eventData)
            {
                if (!_hovered || _dropdown == null || _dropdown.options == null || _dropdown.options.Count == 0)
                    return;

                int count = _dropdown.options.Count;
                float scrollY = eventData.scrollDelta.y;

                // Determine direction, optionally invert
                int delta = scrollY < 0 ? 1 : -1;
                if (_invert.Value)
                    delta = -delta;

                int newIndex = _dropdown.value + delta;

                // Handle wrap or clamp
                if (_wrap.Value)
                {
                    newIndex = (newIndex % count + count) % count;
                }
                else
                {
                    newIndex = Mathf.Clamp(newIndex, 0, count - 1);
                }

                if (newIndex == _dropdown.value)
                    return;

                _dropdown.value = newIndex;
                _dropdown.RefreshShownValue();
                _dropdown.onValueChanged?.Invoke(newIndex);
            }
        }
    }
}