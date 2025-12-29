using BepInEx;
using BepInEx.Configuration;
using QuickComms;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class HoverTriggerPlugin : BaseUnityPlugin
{
    private ConfigEntry<bool>     _modEnabled;
    private ConfigEntry<KeyCode>  _holdKey;
    private ConfigEntry<bool>     _allChat;
    private ConfigEntry<KeyCode>  _allChatModKey;
    private ConfigEntry<bool>     _fastMode;
    private FieldInfo _fiTargetInfo;
    private bool _hudShown;
    private readonly ConfigEntry<string>[] _msg = new ConfigEntry<string>[10];

    private GameObject _ui;
    private readonly List<(Func<bool> hover, Action send)> _entries = [];

    private readonly Text[] _labelRef = new Text[10];
    private static readonly Vector2[] POS = [
        new(-160,  160), new( 160,  160),
        new(-250,   80), new(-250,    0), new(-250,  -80),
        new( 250,   80), new( 250,    0), new( 250,  -80),
        new(-160, -160), new( 160, -160) ];

    private void Awake()
    {
        this.hideFlags = HideFlags.HideAndDontSave;

        _modEnabled    = Config.Bind("General", "Enabled",  true,  "Turn the mod on/off");
        _allChat       = Config.Bind("General", "AllChat",  true,  "Send to all-chat");
        _holdKey       = Config.Bind("Keys",    "HUD key", KeyCode.Y, "Press to show HUD");
        _allChatModKey = Config.Bind("Keys",    "All-Chat Modifier", KeyCode.U, "Hold to invert AllChat for one send");
        _fastMode      = Config.Bind("General", "Fast Mode", false, "True = button toggles the hud, False = hold button for hud");

        string[] def = [ "Yes", "No",
            "{target} Incoming!", "Attacking {target}", "Need help!",
            "Hello!", "Returning to base", "Defend the base!",
            "Thank you!", "Well done!" ];

        for (int i = 0; i < 10; ++i)
        {
            _msg[i] = Config.Bind("Messages", $"Message{i}", def[i], $"Chat text for button {i}");
            int idx = i;
            _msg[i].SettingChanged += (_, __) =>
            {
                if (_labelRef[idx] != null)
                    _labelRef[idx].text = _msg[idx].Value;

                Logger.LogDebug($"Message{idx} changed → \"{_msg[idx].Value}\"");
            };
        }
        _modEnabled.SettingChanged += (_, __) =>
        {
            if (!_modEnabled.Value && _ui != null)
                _ui.SetActive(false);
        };
    }
    private bool EnsureHudExists()
    {
        if (_ui != null)  
            return true;

        var parent = GameObject.Find("HUDCenter")?.transform;
        if (parent == null)
        {
            return false;
        }
        BuildHud(parent);
        return true;
    }
    private void Update()
    {
        if (!_modEnabled.Value) return;

        if (_fastMode.Value)
        {
            if (Input.GetKeyDown(_holdKey.Value))
            {
                if (!EnsureHudExists()) return;    
                _ui?.SetActive(true);
                _hudShown = true;
            }
            else if (Input.GetKeyUp(_holdKey.Value) && _hudShown)
            {
                FireHoveredButtons();
                _ui?.SetActive(false); 
                _hudShown = false;
            }
        }
        else
        {
            if (Input.GetKeyDown(_holdKey.Value))
            {
                if (!_hudShown)
                {
                    if (!EnsureHudExists()) return;         
                    _ui?.SetActive(true);
                    _hudShown = true;
                }
                else
                {
                    FireHoveredButtons();
                    _ui?.SetActive(false);
                    _hudShown = false;
                }
            }
        }
    }

    private void FireHoveredButtons()
    {
        int fired = 0;
        foreach (var (hover, send) in _entries)
        {
            if (hover())
            {
                try
                {
                    send();
                    fired++;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Chat send failed: {ex.Message}");
                }
            }
        }
    }

    private void BuildHud(Transform parent)
    {
        Logger.LogInfo("creating root UI object");
        _ui = new GameObject("QuickCommsHUD");
        _ui.transform.SetParent(parent, false);
        _ui.SetActive(false);

        for (int i = 0; i < 10; ++i)
        {
            int idx = i;

            _labelRef[idx] = MakeButton(_msg[idx].Value, POS[idx], () =>
            {
                bool useAll = Input.GetKey(_allChatModKey.Value) ? !_allChat.Value : _allChat.Value;
                string raw   = _msg[idx].Value;

                string final = raw.Contains("{target}", StringComparison.OrdinalIgnoreCase)
                               ? raw.Replace("{target}", GetCurrentTarget())
                               : raw;

                Logger.LogInfo($"Sending chat \"{final}\"  (allChat={useAll})");
                ChatManager.SendChatMessage(final, useAll);
            });
        }
    }

    private Text MakeButton(string label, Vector2 pos, Action send)
    {
        var go = new GameObject($"Btn_{label}");
        go.transform.SetParent(_ui.transform, false);

        // background
        var img = go.AddComponent<Image>();
        img.color = new Color(0, 0, 0, 0.7f);

        // clickable
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        // rect
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta   = new Vector2(300, 75);
        rt.anchorMin   = rt.anchorMax = new Vector2(0.5f, 1);
        rt.pivot       = new Vector2(0.5f, 1);
        rt.anchoredPosition = pos;

        var canvas = go.GetComponentInParent<Canvas>();
        Camera cam = (canvas.renderMode == RenderMode.ScreenSpaceOverlay) ? null
                                                                          : canvas.worldCamera;

        // hover + send
        _entries.Add((() => RectTransformUtility.RectangleContainsScreenPoint(rt,
                        new Vector2(Screen.width * 0.5f, Screen.height * 0.5f),cam),send));

        // label
        var txt = new GameObject("Text").AddComponent<Text>();
        txt.transform.SetParent(go.transform, false);
        txt.text = label;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        txt.color = Color.white;
        txt.fontSize = 24;
        txt.resizeTextForBestFit = true;
        txt.resizeTextMinSize = 8;
        txt.resizeTextMaxSize = txt.fontSize;
        txt.GetComponent<RectTransform>().anchorMin = Vector2.zero;
        txt.GetComponent<RectTransform>().anchorMax = Vector2.one;

        return txt;
    }

    private string GetCurrentTarget()
    {
        var hud = SceneSingleton<CombatHUD>.i;
        if (hud == null) return "";

        if (_fiTargetInfo == null)
            _fiTargetInfo = typeof(CombatHUD)
                .GetField("targetInfo", BindingFlags.Instance | BindingFlags.NonPublic);

        var txt = _fiTargetInfo?.GetValue(hud) as UnityEngine.UI.Text;
        if (txt == null) return "";

        if (!txt.gameObject.activeInHierarchy || !txt.enabled) return "";
        var parts = txt.text.Split('\n');
        return parts[0]+ " [" + string.Concat(parts.Skip(1)).Trim() + "]";
    }
}
