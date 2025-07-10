using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using Mirage;
//using HarmonyLib;

namespace spawnertest
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]

    public class SpawnerLoggerPlugin : BaseUnityPlugin

    {
        // — UndoManager —
        #region
        public class UndoManager
        {
            /*——————————————————————  action description  ——————————————————————*/
            public enum ActionType { Spawn, Delete, Move, Rotate }

            public struct ActionRecord
            {
                public ActionType Type;
                //public string UnitGuid;
                public GameObject Obj;

                public UnitDefinition PrefabDef;
                public GlobalPosition Position;
                public Quaternion Rotation;
                public FactionHQ Faction;
                public float Skill;
                public Vector3 InitialVelocity;
            }

            /*——————————————————————  ctor and data  ——————————————————————*/
            private readonly Spawner _spawner;
            private readonly Stack<ActionRecord> _undo = new();
            private readonly Stack<ActionRecord> _redo = new();

            private readonly LinkedList<GameObject> _disableBuffer = new();
            private readonly int _bufferCapacity;

            private readonly Action<GameObject> _onAboutToDestroy;

            public UndoManager(Spawner spawner,
                               int bufferCapacity = 50,
                               Action<GameObject> aboutToDestroy = null)
            {
                _spawner = spawner;
                _bufferCapacity = Mathf.Max(bufferCapacity, 0);
                _onAboutToDestroy = aboutToDestroy;
            }

            /*——————————————————————  public API  ——————————————————————*/
            public void Record(ActionRecord rec)
            {
                _undo.Push(rec);
                _redo.Clear();
            }

            public void Undo()
            {
                if (_undo.Count == 0) return;
                var ar = _undo.Pop();

                switch (ar.Type)
                {
                    case ActionType.Spawn:
                        DestroyGO(ar.Obj);
                        break;

                    case ActionType.Delete:
                        RestoreGO(ar.Obj);
                        break;

                    case ActionType.Move:
                        if (ar.Obj)
                        {

                            var newPos = new GlobalPosition(ar.Obj.transform.position);
                            ar.Obj.transform.position = ar.Position.AsVector3();
                            ar.Position = newPos;
                        }
                        break;

                    case ActionType.Rotate:
                        if (ar.Obj)
                        {
                            var newRot = ar.Obj.transform.rotation;
                            ar.Obj.transform.rotation = ar.Rotation;
                            ar.Rotation = newRot;
                        }
                        break;
                }
                _redo.Push(ar);
            }


            public void Redo()
            {
                if (_redo.Count == 0) return;
                var ar = _redo.Pop();

                switch (ar.Type)
                {
                    case ActionType.Spawn:
                        ar.Obj = RespawnUnit(ar); 
                        break;
                    case ActionType.Delete:
                        DestroyGO(ar.Obj); 
                        break;
                    case ActionType.Move:
                        if (ar.Obj)
                        {
                            var cur = new GlobalPosition(ar.Obj.transform.position);
                            ar.Obj.transform.position = ar.Position.AsVector3();
                            ar.Position = cur;
                        }
                        break;

                    case ActionType.Rotate:
                        if (ar.Obj)
                        {
                            var cur = ar.Obj.transform.rotation;
                            ar.Obj.transform.rotation = ar.Rotation;
                            ar.Rotation = cur;
                        }
                        break;
                }
                _undo.Push(ar);
            }

            /*——————————————————————  helpers  ——————————————————————*/
            private void DestroyGO(GameObject go)
            {
                if (!go) return;

                _onAboutToDestroy?.Invoke(go);
                RemoveFromBuffer(go);
                UnityEngine.Object.Destroy(go);
            }

            private void RestoreGO(GameObject go)
            {
                if (!go) return;
                go.SetActive(true);
                RemoveFromBuffer(go);
            }

            private GameObject RespawnUnit(ActionRecord ar)
            {
                if (_spawner == null || ar.PrefabDef == null) return null;
                GameObject go = null;

                try
                {
                    string guid = Guid.NewGuid().ToString("N") + " (Clone)";
                    switch (ar.PrefabDef)
                    {
                        case VehicleDefinition v:
                            go = _spawner.SpawnVehicle(v.unitPrefab, ar.Position, ar.Rotation,
                                                        ar.InitialVelocity, ar.Faction,
                                                        guid,
                                                        ar.Skill, false, null)?.gameObject;
                            break;

                        case ShipDefinition s:
                            go = _spawner.SpawnShip(s.unitPrefab, ar.Position, ar.Rotation,
                                                    ar.Faction, guid,
                                                    ar.Skill, false)?.gameObject;
                            break;

                        case AircraftDefinition a:
                            go = _spawner.SpawnAircraft(null, a.unitPrefab, null,
                                                        ar.Skill, default,
                                                        ar.Position, ar.Rotation,
                                                        ar.InitialVelocity,
                                                        null, ar.Faction,
                                                        guid,
                                                        1f, 0.5f)?.gameObject;
                            break;

                        case BuildingDefinition b:
                            go = _spawner.SpawnBuilding(b.unitPrefab, ar.Position, ar.Rotation,
                                                        ar.Faction, null,
                                                        guid,
                                                        true)?.gameObject;
                            break;

                        case MissileDefinition m:
                            go = _spawner.SpawnMissile(m,
                                                       ar.Position.AsVector3() + Datum.origin.position,
                                                       ar.Rotation,
                                                       ar.InitialVelocity,
                                                       null,
                                                       ar.Faction?.GetComponent<Unit>())
                                        ?.gameObject;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    SpawnerLoggerPlugin.Logger.LogError($"[UndoManager] Respawn failed: {ex}");
                }

                return go;
            }

            /*——————  disable-buffer (unchanged)  ——————*/
            public void AddToBuffer(GameObject obj)
            {
                _disableBuffer.AddLast(obj);
                if (_disableBuffer.Count > _bufferCapacity)
                {
                    var oldest = _disableBuffer.First.Value;
                    _disableBuffer.RemoveFirst();
                    DestroyGO(oldest);
                }
            }

            public void RemoveFromBuffer(GameObject obj)
            {
                var node = _disableBuffer.Find(obj);
                if (node != null) _disableBuffer.Remove(node);
            }
        }
        #endregion
        // - Definitions -
        #region
        // === Configuration Entries ===
        internal ConfigEntry<bool> EnableMod;
        private ConfigEntry<KeyboardShortcut> ToggleEditModeKey;
        private ConfigEntry<float> maxDistance;
        private ConfigEntry<float> spawnSpeed;
        private ConfigEntry<int> BufferCapacity;
        private ConfigEntry<float> spawnHeight;
        private ConfigEntry<float> SnapGrid;
        private ConfigEntry<float> SnapAngle;
        private ConfigEntry<bool> SnapToGround;
        private ConfigEntry<float> spawnSkill;
        private ConfigEntry<float> waterLevel;
        private ConfigEntry<string> SelectedDefinition;
        //private ConfigEntry<string> SelectedFaction;
        private ConfigEntry<float> SelectionOpacity;
        private ConfigEntry<bool> DefaultHoldPosition;
        private ConfigEntry<int> SelectedFactionIndex;

        // === Plugin State Flags & Simple Primitives ===
        private static new ManualLogSource Logger;
        private bool editMode = false;
        private bool spawnConfigInitialized = false;
        private bool lastShiftHeld = false;
        private bool isRotating = false;
        private float lastMouseY;
        private float lastMouseX;
        private bool holdPositionEnabled = false;
        private bool staticSpawn = false;
        private string _selectedDisplayName = string.Empty;

        // === References to Managers & Services ===
        private CameraStateManager cameraMgr;
        private Spawner spawner;
        private UndoManager undoMgr;
        private MethodInfo waitRepairMethod;
        //private Dictionary<string, FactionHQ> _factionLookup;
        private List<FactionHQ> _factionList;      
        private string[] _factionNames;
        private NetworkServer _netServer;
        private bool _checkedHost;


        // === Saved Physics State for Drag/Rotate ===
        private Rigidbody[] _savedRigidbodies;
        private bool[] _savedRbKinematic;
        //private bool[] _savedRbUseGravity;
        private Collider[] _savedColliders;
        private bool[] _savedColliderEnabled;

        // === Dragging Helpers ===
        private Plane dragPlane;
        private float dragGroundY;
        private float verticalOffset;
        private GameObject dragTarget = null;
        private Rigidbody dragRigidbody;
        private Collider[] dragColliders;
        private Vector3 dragOffset;

        // === Rotation Helpers ===
        private GameObject rotatingTarget = null;
        private Rigidbody rotatingRigidbody;
        private Collider[] rotatingColliders;

        // === UI Elements ===
        private GameObject _uiCanvas;
        private GameObject _menuPanelGO;
        //private CursorLockMode _prevLockState;
        //private bool _prevCursorVisible;

        // === Spawnable Units & Categories ===
        private UnitDefinition[] allDefinitions;
        private List<(string Name, List<UnitDefinition> Defs)> _categories;

        // — Selection & Movement Helpers —
        private readonly List<GameObject> selectedMovers = [];

        // === Box-Selection UI ===
        private bool isBoxSelecting = false;
        private Vector2 boxStartScreen = Vector2.zero;
        private Vector2 boxCurrentScreen = Vector2.zero;
        private GameObject boxGO = null;
        private RectTransform boxRT = null;

        // — Click vs Drag State —
        private bool lmbDown = false;
        private bool isLeftDragging = false;
        private Vector2 lmbDownPos = Vector2.zero;
        private bool rmbDown = false;
        private bool isRightDragging = false;
        private Vector2 rmbDownPos = Vector2.zero;
        private const float clickThresholdSqr = 25f;
        private readonly Dictionary<GameObject, RectTransform> selectionBoxes = [];
        private bool _messageSound = false;
        private string _messageText;
        private InputField _messageInput;
        private static readonly BindingFlags _flags = BindingFlags.Instance | BindingFlags.NonPublic;
        /*
        // - Weather
        public static SpawnerLoggerPlugin Instance;
        private static ManualLogSource staticLogger;

        private ConfigEntry<float> timeOfDay;
        private ConfigEntry<float> conditions;
        private ConfigEntry<float> cloudHeight;
        private ConfigEntry<Vector3> windVelocity;
        private ConfigEntry<float> windTurbulence;
        private ConfigEntry<float> windSpeed;

        private LevelInfo levelInfoInstance;*/
        #endregion
        internal class ConfigurationManagerAttributes
        {
            public bool? Browsable;
            public bool? IsAdvanced;
        }

        // - Loop -
        #region
        private void OnDisable()
        {
            // in case the BepInEx host tears you down, ensure cursor comes back
            if (editMode) SetEditMode(false);
        }
        private bool NotHostYet()
        {
            if (_checkedHost) 
                return _netServer == null || !_netServer.IsHost;

            _netServer = GameObject.Find("networkManager(Clone)") ?.GetComponent<NetworkServer>();
            _checkedHost = true;
            if (_netServer == null)
            {
                Logger.LogWarning("NetworkManager not found – disabling plugin on this client.");
                return true;
            }

            if (!_netServer.IsHost)
            {
                Logger.LogInfo("Client is not the host – plugin will stay inactive.");
                return true;
            }

            //Logger.LogInfo("Host confirmed – full functionality enabled.");
            return false;                 
        }
        private void Awake()
        {
            this.hideFlags = HideFlags.HideAndDontSave;
            Logger = base.Logger;
            EnableMod = Config.Bind("Settings", "Enable Mod", true);
            ToggleEditModeKey = Config.Bind("Hotkeys", "ToggleEditMode", new KeyboardShortcut(KeyCode.Tab), new ConfigDescription("", null, new ConfigurationManagerAttributes { IsAdvanced = true }));
            DefaultHoldPosition = Config.Bind("Spawn", "Default Hold Position", false, "When true, every newly-spawned GV / Ship starts in hold-position.");
            holdPositionEnabled = DefaultHoldPosition.Value;
            maxDistance = Config.Bind("Settings", "Max spawn distance", 1000f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 10000f)));
            spawnHeight = Config.Bind("Settings", "Set spawn height offset", 1.5f, new ConfigDescription("", new AcceptableValueRange<float>(-100f, 1000f)));
            spawnSpeed = Config.Bind("Settings", "Set spawn speed in m/s", 0f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 500f)));
            spawnSkill = Config.Bind("Settings", "Set spawn skill", 0.7f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f)));
            BufferCapacity = Config.Bind("Settings", "undo buffer size", 0, new ConfigDescription("", new AcceptableValueRange<int>(0, 200), new ConfigurationManagerAttributes { IsAdvanced = true }));
            SnapGrid = Config.Bind("Settings", "Snap Grid size", 0f, new ConfigDescription("", new AcceptableValueRange<float>(0, 1000)));
            SnapAngle = Config.Bind("Settings", "Snap Angle size", 0f, new ConfigDescription("", new AcceptableValueRange<float>(0, 359)));
            SelectionOpacity = Config.Bind("Settings", "Selection box opacity", 0.3f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { IsAdvanced = true }));
            SnapToGround = Config.Bind("Settings", "Snap To Ground", true);
            waterLevel = Config.Bind("Settings", "Water Level Y", 6f, new ConfigDescription("", null, new ConfigurationManagerAttributes { IsAdvanced = true }));
            /*
            Instance = this;

            timeOfDay = Config.Bind("Environment", "TimeOfDay", 12f, new ConfigDescription("Time of Day (0–24)", new AcceptableValueRange<float>(0f, 24f)));
            conditions = Config.Bind("Environment", "Conditions", 0.5f, new ConfigDescription("Weather Conditions (0–1)", new AcceptableValueRange<float>(0f, 1f)));
            cloudHeight = Config.Bind("Environment", "CloudHeight", 1500f, new ConfigDescription("Cloud Height (500–4000)", new AcceptableValueRange<float>(500f, 4000f)));
            windVelocity = Config.Bind("Environment", "WindVelocity", new Vector3(0f, 0f, 0f), "Wind direction and strength vector");
            windTurbulence = Config.Bind("Environment", "WindTurbulence", 0.1f, new ConfigDescription("Wind Turbulence (0–1)", new AcceptableValueRange<float>(0f, 1f)));
            windSpeed = Config.Bind("Environment", "WindSpeed", 10f, new ConfigDescription("Wind Speed (0–72)", new AcceptableValueRange<float>(0f, 72f)));

            BindChangeEvents();
            new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll();
            */
            EnableMod.SettingChanged += (s, e) => { if (!EnableMod.Value && editMode) SetEditMode(false);};
            waitRepairMethod = typeof(global::Airbase).GetMethod("WaitRepair", BindingFlags.Instance | BindingFlags.NonPublic);
            if (waitRepairMethod == null) Logger.LogError("Could not find Airbase.WaitRepair via reflection.");

        }/*
        private void BindChangeEvents()
        {
            timeOfDay.SettingChanged += (_, _) => { if (levelInfoInstance) levelInfoInstance.NetworktimeOfDay = timeOfDay.Value; };
            conditions.SettingChanged += (_, _) => { if (levelInfoInstance) levelInfoInstance.Networkconditions = conditions.Value; };
            cloudHeight.SettingChanged += (_, _) => { if (levelInfoInstance) levelInfoInstance.NetworkcloudHeight = cloudHeight.Value; };
            windVelocity.SettingChanged += (_, _) => { if (levelInfoInstance) levelInfoInstance.NetworkwindVelocity = windVelocity.Value; };
            windTurbulence.SettingChanged += (_, _) => { if (levelInfoInstance) levelInfoInstance.NetworkwindTurbulence = windTurbulence.Value; };
            windSpeed.SettingChanged += (_, _) => { if (levelInfoInstance) levelInfoInstance.NetworkwindSpeed = windSpeed.Value; };
        }

        [HarmonyPatch(typeof(LevelInfo), "Awake")]
        class Patch_LevelInfo_Awake
        {
            static void Postfix(LevelInfo __instance)
            {
                staticLogger.LogInfo("[LevelInfoConfigSync] Found LevelInfo instance, syncing config.");

                Instance.levelInfoInstance = __instance;

                // Update config values from game state
                Instance.timeOfDay.Value = __instance.NetworktimeOfDay;
                Instance.conditions.Value = __instance.Networkconditions;
                Instance.cloudHeight.Value = __instance.NetworkcloudHeight;
                Instance.windVelocity.Value = __instance.NetworkwindVelocity;
                Instance.windTurbulence.Value = __instance.NetworkwindTurbulence;
                Instance.windSpeed.Value = __instance.NetworkwindSpeed;

                staticLogger.LogInfo("[LevelInfoConfigSync] Config values updated from LevelInfo instance.");
            }
        }*/
        private void MessageOut()
        {
            if (string.IsNullOrWhiteSpace(_messageText))
                return;

            int idx = SelectedFactionIndex.Value;
            IEnumerable<FactionHQ> targets;

            if (idx == 0)
            {
                // “0” means broadcast to every real HQ (we stored null at [0])
                targets = _factionList
                    .Skip(1)           // skip the null placeholder
                    .Where(hq => hq != null);
            }
            else if (idx > 0 && idx < _factionList.Count)
            {
                // single selected HQ
                var hq = _factionList[idx];
                if (hq == null)
                {
                    Logger.LogError($"Selected faction index {idx} has no FactionHQ instance.");
                    return;
                }
                targets = new[] { hq };
            }
            else
            {
                Logger.LogError($"Faction index {idx} is out of range (0..{_factionList.Count - 1}).");
                return;
            }

            // send the message
            foreach (var hq in targets)
                MissionMessages.ShowMessage(_messageText, _messageSound, hq, true);

            // log
            var who = (idx == 0)
                ? "all factions"
                : _factionNames[idx];
            Logger.LogInfo($"Sent message to {who}: \"{_messageText}\"");
        }
        private void Update()
        {
            // if the mod is turned off, but we're still in editMode, shut it down cleanly:
            if (!EnableMod.Value)
            {
                if (editMode)
                    SetEditMode(false);
                return;
            }

            // likewise, if we lose host status:
            if (NotHostYet())
            {
                if (editMode)
                    SetEditMode(false);
                return;
            }

            // only now do your scene check:
            if (SceneManager.GetActiveScene().name != "GameWorld")
            {
                _checkedHost = false;
                SetEditMode(false);
                CleanupUI();
                return;
            }
            EnsureReferences();
            HandleEditModeToggle();
            CheckCursorState();
            if (!editMode) return;

            if (_uiCanvas == null && allDefinitions?.Any() == true)
                CreateMenuUI();
            HandleInput();
            if (isRotating && !Input.GetMouseButton(1))
                EndRotation();
            if (isLeftDragging && !Input.GetMouseButton(0))
            {
                if (isBoxSelecting) EndBoxSelect();
                else EndDrag();
            }
        }
        private void LateUpdate()
        {
            if (editMode)
                RefreshSelectionBoxes();
        }
        #endregion
        // — Edit‐Mode Control —
        #region
        private void SetEditMode(bool on)
        {
            if (on)
            {
                //_prevLockState = Cursor.lockState;
                //_prevCursorVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                // recreate UI if it was destroyed by a scene change
                if (_uiCanvas == null && allDefinitions?.Any() == true)
                    CreateMenuUI();
                ShowCategories();
                _uiCanvas.SetActive(true);
            }
            else
            {
                if (_uiCanvas) _uiCanvas.SetActive(false);
                if (isRotating) EndRotation();
                if (isBoxSelecting) EndBoxSelect();
                if (isLeftDragging) EndDrag();
                //selectedMovers.Clear();
                //Cursor.lockState = _prevLockState;
                //Cursor.visible = _prevCursorVisible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            editMode = on;
            //Logger.LogInfo($"Edit mode {(on ? "enabled" : "disabled")}.");
        }
        private void HandleEditModeToggle()
        {
            if (ToggleEditModeKey.Value.MainKey != KeyCode.None
                && Input.GetKeyDown(ToggleEditModeKey.Value.MainKey))
            {
                SetEditMode(!editMode);
            }
        }
        private void CheckCursorState()
        {
            if (editMode && (Cursor.lockState != CursorLockMode.None || !Cursor.visible))
            {
                SetEditMode(false);
            }
        }
        #endregion
        // — Initialization & References —
        #region
        private void InitializeSpawnConfig()
        {
            if (allDefinitions?.Any() == true && (SelectedDefinition == null || string.IsNullOrEmpty(SelectedDefinition.Value)))
            {
                var names = allDefinitions.Select(d => d.name).ToArray();
                SelectedDefinition = Config.Bind("Spawn", "Unit Definition", names[0], new ConfigDescription("Which unit to spawn", new AcceptableValueList<string>(names)));
                _selectedDisplayName = allDefinitions.FirstOrDefault(d => d.name == SelectedDefinition.Value)?.unitName ?? SelectedDefinition.Value;
            }
        }
        private void InitializeFactions()
        {
            // grab every FactionHQ in the scene (or via Resources)
            var found = UnityEngine.Object.FindObjectsOfType<FactionHQ>().ToList();

            // build our list: index 0 = null (no faction), 1..N = the actual HQs
            _factionList = new List<FactionHQ> { null };
            _factionList.AddRange(found);

            // build the matching display names
            _factionNames = _factionList
                .Select((hq, i) => i == 0 ? "None/All" : hq.name)
                .ToArray();

            // bind an integer config entry, with an acceptable range of [0 .. count-1]
            SelectedFactionIndex = Config.Bind(
                "Spawn", "Faction Index",
                0,
                new ConfigDescription(
                    "Which faction to spawn under (0 = none)",
                    new AcceptableValueList<int>(Enumerable.Range(0, _factionNames.Length).ToArray())
                )
            );
        }
        private void EnsureReferences()
        {
            if (allDefinitions == null || allDefinitions.Length == 0)
            {
                var allUnits = Resources.FindObjectsOfTypeAll<UnitDefinition>();
                var vehicles = Resources.FindObjectsOfTypeAll<VehicleDefinition>().Cast<UnitDefinition>();
                var ships = Resources.FindObjectsOfTypeAll<ShipDefinition>().Cast<UnitDefinition>();
                var aircraft = Resources.FindObjectsOfTypeAll<AircraftDefinition>().Cast<UnitDefinition>();
                var buildings = Resources.FindObjectsOfTypeAll<BuildingDefinition>().Cast<UnitDefinition>();

                var containers = allUnits.Where(u => string.Equals(u.code, "CTNR", StringComparison.OrdinalIgnoreCase)).ToList();
                var bombs = allUnits.Where(u => string.Equals(u.code, "BOMB", StringComparison.OrdinalIgnoreCase)).ToList();
                var missiles = allUnits.Where(u => string.Equals(u.code, "MSL", StringComparison.OrdinalIgnoreCase)).ToList();
                var pilots = allUnits.Where(u => string.Equals(u.code, "PILOT", StringComparison.OrdinalIgnoreCase)).ToList();

                //var missiles = Resources.FindObjectsOfTypeAll<MissileDefinition>().Cast<UnitDefinition>();
                allDefinitions = [.. vehicles, .. ships, .. aircraft, .. buildings, .. missiles, .. bombs, .. containers, .. pilots];
            }
            if (!spawnConfigInitialized && allDefinitions?.Any() == true)
            {
                InitializeSpawnConfig();
                spawnConfigInitialized = true;
            }
            if (cameraMgr == null)
                cameraMgr = FindObjectOfType<CameraStateManager>();
            if (spawner == null)
            {
                spawner = FindObjectOfType<Spawner>() ?? GameObject.Find("NetworkScripts")?.GetComponent<Spawner>();

                /* Instantiate UndoManager the moment we have a Spawner */
                if (spawner != null && undoMgr == null)
                {
                    undoMgr = new UndoManager(spawner, BufferCapacity.Value, go =>                  
                        {
                            selectedMovers.Remove(go);
                            if (selectionBoxes.TryGetValue(go, out var rt))
                            {
                                Destroy(rt.gameObject);
                                selectionBoxes.Remove(go);
                            }
                        });
                }
            }
            InitializeFactions();
        }
        #endregion
        // — Input Handlers —
        #region
        private void HandleInput()
        {
            HandleLeftClick();
            HandleRightClick();
            HandleUndoShortcut();
            HandleOngoingRotation();
            HandleCopyShortcut();
            HandleDeleteAllSelectedShortcut();
        }

        private void HandleLeftClick()
        {
            // Mouse button down: start tracking
            if (Input.GetMouseButtonDown(0))
            {
                lmbDown = true;
                isLeftDragging = false;
                lmbDownPos = Input.mousePosition;
            }
            // Mouse held: detect drag start or continue drag
            else if (Input.GetMouseButton(0) && lmbDown)
            {
                var delta = (Vector2)Input.mousePosition - lmbDownPos;
                if (!isLeftDragging && delta.sqrMagnitude > clickThresholdSqr)
                {
                    if (Raycast(out var downHit) && GetClickedObject(downHit) != null)
                    {
                     //   Debug.Log("LEFT DRAG START");
                        isLeftDragging = true;
                        BeginDrag();
                    }
                    else
                    {
                   //     Debug.Log("LEFT DRAG Continue");
                        isLeftDragging = true;
                        StartBoxSelect(lmbDownPos);
                    }
                }
                if (isLeftDragging)
                {
                  //  Debug.Log("isLeftDragging");
                    if (isBoxSelecting)
                    {
                        UpdateBoxSelect(Input.mousePosition);
                    }
                    else
                    {
                      //  Debug.Log("HandleDrag");
                        HandleDrag();
                    }
                }
            }
            // Mouse button up: end drag or perform click action
            else if (Input.GetMouseButtonUp(0) && lmbDown)
            {
                var delta = (Vector2)Input.mousePosition - lmbDownPos;
                if (isLeftDragging)
                {
                    if (isBoxSelecting)
                        EndBoxSelect();
                    else
                        EndDrag();
                }
                else
                {
                    selectedMovers.Clear();
                    if (!Input.GetKey(KeyCode.LeftControl) && HandleSelectUnit())
                    {
                        // unit selected
                    }
                    else if (Input.GetKey(KeyCode.LeftControl))
                    {
                        SpawnConfiguredUnit();
                    }
                }
                lmbDown = false;
                isLeftDragging = false;
            }
        }
        private void HandleRightClick()
        {
            // Mouse button down: start tracking
            if (Input.GetMouseButtonDown(1))
            {
                rmbDown = true;
                isRightDragging = false;
                rmbDownPos = Input.mousePosition;
            }
            // Mouse held: detect drag start or continue rotation
            else if (Input.GetMouseButton(1) && rmbDown)
            {
                var delta = (Vector2)Input.mousePosition - rmbDownPos;
                if (!isRightDragging && delta.sqrMagnitude > clickThresholdSqr)
                {
                    isRightDragging = true;
                    BeginRotation();
                }
                if (isRightDragging)
                {
                    HandleRotation();
                }
            }
            // Mouse button up: end rotation or perform click action
            else if (Input.GetMouseButtonUp(1) && rmbDown)
            {
                var delta = (Vector2)Input.mousePosition - rmbDownPos;
                if (isRightDragging)
                {
                    EndRotation();
                }
                else
                {
                    // Click: move command or delete
                    if (!Input.GetKey(KeyCode.LeftControl) && HandleMoveCommand())
                    {
                        // move issued
                    }
                    else if (Input.GetKey(KeyCode.LeftControl))
                    {
                        DeleteObject();
                    }
                }
                rmbDown = false;
                isRightDragging = false;
            }
        }
        private void HandleUndoShortcut()
        {
            if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.Z))
            {
                if (Input.GetKey(KeyCode.LeftShift))
                    undoMgr.Redo();
                else
                    undoMgr.Undo();
            }
        }
        private void HandleOngoingRotation()
        {
            if (isRotating)
                HandleRotation();
        }
        private void HandleCopyShortcut()
        {
            // Ctrl+C
            if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.C))
            {
                if (!Raycast(out var hit))
                    return;
                if (hit.collider == null)
                {
                    Logger.LogInfo("[Copy] Hit water plane – nothing to copy.");
                    return;
                }

                var obj = GetClickedObject(hit);
                if (obj == null)
                    return;

                // strip “(Clone)” if present
                var name = obj.name.EndsWith("(Clone)")
                    ? obj.name.Substring(0, obj.name.Length - "(Clone)".Length)
                    : obj.name;

                // find the definition
                var def = allDefinitions.FirstOrDefault(d => d.name == name);
                if (def != null)
                {
                    // 1) Copy the unit definition
                    SelectedDefinition.Value = def.name;
                    _selectedDisplayName = def.unitName;
                    Logger.LogInfo($"Copied definition: '{def.name}'");

                    // 2) Copy its faction index
                    int facIndex = 0; // default = “None/All”
                    var unit = obj.GetComponentInParent<Unit>();
                    if (unit?.NetworkHQ != null)
                    {
                        // find which index in our _factionList matches
                        facIndex = _factionList.FindIndex(hq => hq == unit.NetworkHQ);
                        if (facIndex < 0) facIndex = 0;
                    }
                    SelectedFactionIndex.Value = facIndex;
                    Logger.LogInfo($"Copied faction: '{_factionNames[facIndex]}' (index {facIndex})");

                    // 3) Refresh the UI if in edit mode
                    if (editMode && _uiCanvas != null)
                        ShowCategories();
                }
                else
                {
                    Logger.LogWarning($"Object '{name}' is not a known definition.");
                }
            }
        }
        private void HandleDeleteAllSelectedShortcut()
        {
            // when user presses the Delete key
            if (Input.GetKeyDown(KeyCode.Delete))
                DeleteAllSelectedUnits();
        }

        // - Command Handlers -
        private bool HandleSelectUnit()
        {
            if (!Raycast(out var hit) || hit.collider == null)
                return false;

            selectedMovers.Clear();

            var gv = hit.collider.GetComponentInParent<GroundVehicle>();
            if (gv != null)
            {
                selectedMovers.Add(gv.gameObject);
                Logger.LogInfo($"Selected GroundVehicle: {gv.name}");
                return true;
            }
            var ship = hit.collider.GetComponentInParent<Ship>();
            if (ship != null)
            {
                selectedMovers.Add(ship.gameObject);
                Logger.LogInfo($"Selected Ship: {ship.name}");
                return true;
            }
            return false;
        }
        private bool HandleMoveCommand()
        {
            return false; //broken as of 0.30.9
            /*
            if (selectedMovers.Count == 0)
                return false;

            if (!Raycast(out var hit))
                return false;

            Vector3 worldPos = hit.point - Datum.origin.position;
            var dest = new GlobalPosition(worldPos);

            foreach (var go in selectedMovers)
            {
                var gv = go.GetComponent<GroundVehicle>();
                if (gv != null)
                {
                    gv.SetDestination(dest, null);
                    continue;
                }
                var sh = go.GetComponent<Ship>();
                if (sh != null)
                {
                    sh.SetDestination(dest, null);
                }
            }
            Logger.LogInfo($"Issued move command to {selectedMovers.Count} movers.");
            return true;
            */
        }
        #endregion
        // — Spawning Logic —
        #region
        private void DeleteObject()
        {
            if (!Raycast(out var hit))
                return;
            if (hit.collider == null)
            {
                Logger.LogInfo("[DeleteObject] Hit water plane, nothing to delete.");
                return;
            }
            var toDisable = GetClickedObject(hit);

            if (toDisable == null) return;
            if (toDisable == dragTarget) EndDrag();
            if (toDisable == rotatingTarget) EndRotation();
            toDisable.SetActive(false);
            undoMgr.Record(new UndoManager.ActionRecord { Type = UndoManager.ActionType.Delete, Obj = toDisable });
            undoMgr.AddToBuffer(toDisable);
            Logger.LogInfo($"Disabled '{toDisable.name}'");
        }
        private void DeleteAllSelectedUnits()
        {
            if (selectedMovers.Count == 0)
                return;

            foreach (var go in selectedMovers)
            {
                if (go)
                {
                    go.SetActive(false);
                    undoMgr.Record(new UndoManager.ActionRecord { Type = UndoManager.ActionType.Delete, Obj = go });
                    undoMgr.AddToBuffer(go);
                }
            }
            Logger.LogInfo($"Deleted {selectedMovers.Count} selected units.");
            selectedMovers.Clear();
        }
        private void SpawnConfiguredUnit()
        {
            if (spawner == null)
            {
                Logger.LogError("Cannot spawn: Spawner instance is null.");
                return;
            }

            var def = allDefinitions.FirstOrDefault(d => d.name == SelectedDefinition.Value);
            if (def == null)
            {
                Logger.LogError($"Definition '{SelectedDefinition.Value}' not found.");
                return;
            }

            FactionHQ hq = _factionList[SelectedFactionIndex.Value];

            cameraMgr.GetCameraPosition(out GlobalPosition camPos, out Quaternion camRot);
            Vector3 basePos = camPos.AsVector3();
            Ray ray = GetCursorRay();
            float dist = maxDistance.Value;
            if (Raycast(out var hit))
                dist = hit.distance;
            Vector3 spawnPosGlobal3 = basePos + ray.direction.normalized * dist;
            spawnPosGlobal3.y = Mathf.Max(spawnPosGlobal3.y, spawnHeight.Value);
            GlobalPosition spawnPosGlobal = new GlobalPosition(spawnPosGlobal3 + Vector3.up * spawnHeight.Value);
            Vector3 spawnPosRelative = spawnPosGlobal.AsVector3() + global::Datum.origin.position;


            Quaternion spawnRot;
            Vector3 velocity;
            if (spawnSpeed.Value > 1f)
            {
                spawnRot = spawnRot = Quaternion.LookRotation(GetCursorRay().direction.normalized, Vector3.up);
                velocity = GetCursorRay().direction.normalized * spawnSpeed.Value;
            }
            else
            {
                spawnRot = CalculateSpawnRotation();
                velocity = Camera.main.transform.forward * spawnSpeed.Value;
            }
            //if (SnapGrid.Value > 0) spawned.transform.position = SnapToGlobalGrid(spawned.transform.position);
          //  if (SnapGrid.Value > 0) spawnPosGlobal = SnapToGlobalGrid(spawnPosGlobal.AsVector3()).ToGlobalPosition();
            if (SnapGrid.Value > 0) 
            {
                spawnPosGlobal.x = Mathf.Round(spawnPosGlobal.x / SnapGrid.Value) * SnapGrid.Value;
                spawnPosGlobal.y = Mathf.Round(spawnPosGlobal.y / SnapGrid.Value) * SnapGrid.Value;
                spawnPosGlobal.z = Mathf.Round(spawnPosGlobal.z / SnapGrid.Value) * SnapGrid.Value;
            }
            if (SnapGrid.Value > 0) spawnPosRelative = SnapToGlobalGrid(spawnPosRelative);
            if (SnapAngle.Value > 0f)
            {
                Vector3 e = spawnRot.eulerAngles;
                e.x = Mathf.Round(e.x / SnapAngle.Value) * SnapAngle.Value;
                e.y = Mathf.Round(e.y / SnapAngle.Value) * SnapAngle.Value;
                e.z = Mathf.Round(e.z / SnapAngle.Value) * SnapAngle.Value;
                spawnRot.eulerAngles = e;
            }
            GameObject spawned = null;
            try
            {
                string guid = Guid.NewGuid().ToString("N") + " (Clone)";
                if (def is VehicleDefinition vDef)
                    spawned = spawner.SpawnVehicle(vDef.unitPrefab, spawnPosGlobal, spawnRot, velocity, hq, guid, spawnSkill.Value, false, null)?.gameObject;
                else if (def is ShipDefinition sDef)
                    spawned = spawner.SpawnShip(sDef.unitPrefab, spawnPosGlobal, spawnRot, hq, guid, spawnSkill.Value, false)?.gameObject;
                else if (def is BuildingDefinition bDef)
                    spawned = spawner.SpawnBuilding(bDef.unitPrefab, spawnPosGlobal, spawnRot, hq, null/*airbase*/, guid, true)?.gameObject;
                else if (def is AircraftDefinition aDef)
                    spawned = spawner.SpawnAircraft(null, aDef.unitPrefab, null/*Loadout*/, 1f, default, spawnPosGlobal, spawnRot, velocity, null, hq, guid, spawnSkill.Value, 0.5f)?.gameObject;
                else if (def is MissileDefinition mDef)
                {
                    var candidates = UnityEngine.Object.FindObjectsOfType<Unit>().Where(u => u.NetworkHQ == hq).ToArray();
                    Unit owner = candidates.Length > 0 ? candidates[UnityEngine.Random.Range(0, candidates.Length)] : null;
                    spawned = spawner.SpawnMissile(mDef, spawnPosRelative, spawnRot, velocity, null, owner).gameObject;
                }
                else
                {
                    var candidates = UnityEngine.Object.FindObjectsOfType<Unit>().Where(u => u.NetworkHQ == hq).ToArray();
                    Unit owner = candidates.Length > 0 ? candidates[UnityEngine.Random.Range(0, candidates.Length)] : null;
                    spawner.SpawnUnit(def, spawnPosRelative, spawnRot, velocity, owner, null);
                }
                if (spawned != null)
                {
                    if (def is BuildingDefinition bDef)
                    {
                        var hangar = spawned.GetComponent<Hangar>();
                        if (hangar != null)
                        {
                            Airbase nearest = null;
                            float bestSq = float.PositiveInfinity;

                            foreach (var ab in UnityEngine.Object.FindObjectsOfType<Airbase>())
                            {
                                float sq = (ab.transform.position - spawned.transform.position).sqrMagnitude;
                                if (sq < bestSq)
                                {
                                    bestSq = sq;
                                    nearest = ab;
                                }
                            }
                            if (nearest != null)
                            {
                                nearest.AddHangar(hangar);
                                Logger.LogInfo(
                                    $"Registered new hangar with airbase '{nearest.name}' " +
                                    $"({Mathf.Sqrt(bestSq):F1} m away).");

                                // ───── “carrier” upgrade ─────
                                if (nearest.name.IndexOf("carrier", StringComparison.OrdinalIgnoreCase) >= 0 || nearest.name.IndexOf("destroyer", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    // keep world-space position/rotation while parenting under the carrier
                                    hangar.transform.SetParent(nearest.transform, true);
                                    Logger.LogInfo($"Hangar re-parented under carrier '{nearest.name}'.");
                                }
                            }
                            else
                            {
                                Logger.LogInfo("Spawned building has a Hangar component but no Airbase exists in the scene.");
                            }
                        }
                    }
                    else
                    {
                        if (holdPositionEnabled) SetHoldPosition(spawned, true);
                        if (staticSpawn) SetStaticOnSpawn(spawned, false);
                    }
                    undoMgr.Record(new UndoManager.ActionRecord
                    {
                        Type = UndoManager.ActionType.Spawn,
                        Obj = spawned,
                        PrefabDef = def,
                        Position = spawnPosGlobal,
                        Rotation = spawnRot,
                        Faction = hq,
                        Skill = spawnSkill.Value,
                        InitialVelocity = velocity
                    });
                    Logger.LogInfo(
                        $"Spawned '{spawned.name}' (HoldPos={(holdPositionEnabled ? "ON" : "OFF")}, " +
                        $"Static={(staticSpawn ? "ON" : "OFF")})");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Spawn failed: {ex}");
            }
        }
       /* private GameObject SpawnMissile(MissileDefinition mDef, Quaternion camRot, Vector3 worldPos, FactionHQ hq)
        {

            Vector3 dir = GetCursorRay().direction.normalized;
                
            var candidates = UnityEngine.Object.FindObjectsOfType<Unit>().Where(u => u.NetworkHQ == hq).ToArray();
            Unit owner = candidates.Length > 0 ? candidates[UnityEngine.Random.Range(0, candidates.Length)]: null;
            var missile = spawner.SpawnMissile(mDef, worldPos + global::Datum.origin.position, Quaternion.LookRotation(dir, Vector3.up), dir * spawnSpeed.Value, null, owner);
            return missile?.gameObject;
        }*/
        private Quaternion CalculateSpawnRotation()
        {
            Vector3 forward = Camera.main.transform.forward;
            forward.y = 0;
            if (forward == Vector3.zero)
                forward = Vector3.forward;
            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }
        private void SetHoldPosition(GameObject go, bool value)
        {
            if (go == null) return;

            var gv = go.GetComponent<GroundVehicle>();
            if (gv != null)
            {
                typeof(GroundVehicle).GetField("holdPosition", _flags)?.SetValue(gv, value);
            }

            var ship = go.GetComponent<Ship>();
            if (ship != null)
            {
                typeof(Ship).GetField("holdPosition", _flags)?.SetValue(ship, value);
            }
        }
        private void SetStaticOnSpawn(GameObject root, bool enabled)
        {
            foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
            {
                rb.isKinematic = !enabled;
               // rb.useGravity = !enabled;  
            }
        }
        private IEnumerator RepairNearestAirbaseCoroutine()
        {
            if (waitRepairMethod == null)
            {
                Logger.LogError("WaitRepair method unavailable; aborting repair.");
                yield break;
            }

            // find the closest Airbase to the main camera
            Vector3 camPos = Camera.main.transform.position;
            global::Airbase nearest = null;
            float bestSq = float.PositiveInfinity;

            foreach (var ab in UnityEngine.Object.FindObjectsOfType<global::Airbase>())
            {
                float sq = (ab.transform.position - camPos).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    nearest = ab;
                }
            }

            if (nearest == null)
            {
                Logger.LogInfo("No airbase found to repair.");
                yield break;
            }

            Logger.LogInfo($"Repairing nearest airbase '{nearest.name}' …");

            // call the private UniTask WaitRepair() and turn it into a coroutine
            object taskObj = null;
            try
            {
                taskObj = waitRepairMethod.Invoke(nearest, null);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error invoking WaitRepair on '{nearest.name}': {ex}");
                yield break;
            }

            if (taskObj != null)
            {
                var toCo = taskObj.GetType().GetMethod("ToCoroutine",
                             BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (toCo != null)
                {
                    if (toCo.Invoke(taskObj, null) is IEnumerator enumerator)
                        yield return enumerator;
                    else
                        yield return null;
                }
                else
                    yield return null;
            }

            Logger.LogInfo($"Repaired airbase '{nearest.name}'.");
        }
        #endregion
        // — Raycasting & Position Math —
        #region
        private Ray GetCursorRay()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                Logger.LogError("Main camera not found.");
                return new Ray();
            }
            return cam.ScreenPointToRay(Input.mousePosition);
        }
        private bool Raycast(out RaycastHit hit, Ray? ray = null, float? maxDist = null, int layerMask = Physics.DefaultRaycastLayers)
        {
            Ray localRay = ray ?? GetCursorRay();
            float maxDistance = maxDist ?? this.maxDistance.Value;
            float localWaterY = waterLevel.Value + Datum.origin.position.y;
            if (Physics.Raycast(localRay, out RaycastHit terrainHit, maxDistance, layerMask))
            {
                if (terrainHit.point.y >= localWaterY)
                {
                    hit = terrainHit;
                    return true;
                }
            }
            Plane waterPlane = new(Vector3.up, new Vector3(0f, localWaterY, 0f));

            if (waterPlane.Raycast(localRay, out float enter))
            {
                if (enter <= maxDistance)
                {
                    Vector3 wp = localRay.GetPoint(enter);
                    var waterHit = new RaycastHit
                    {
                        point = wp,
                        normal = Vector3.up,
                        distance = enter
                    };
                    hit = waterHit;
                    return true;
                }
            }
            hit = default;
            return false;
        }
        private GameObject GetClickedObject(RaycastHit hit)
        {
            // water plane & other non-collider hits
            if (hit.collider == null)
                return null;

            var root = hit.collider.transform.root.gameObject;
            if (root.name != "Datum") return root;
            if (hit.collider.gameObject.name.Contains("(Clone)") && !hit.collider.gameObject.name.ToLower().Contains("airbase"))
                return hit.collider.gameObject;
            return null;
        }
        private Vector3 SnapToGlobalGrid(Vector3 worldPos)
        {
            if (SnapGrid.Value <= 0f)
                return worldPos;

            // Convert to absolute GlobalPosition
            var gp = new GlobalPosition(worldPos);

            // Round each component to nearest multiple of SnapGrid.Value
            float size = SnapGrid.Value;
            gp.x = Mathf.Round(gp.x / size) * size;
            gp.y = Mathf.Round(gp.y / size) * size;
            gp.z = Mathf.Round(gp.z / size) * size;

            return gp.AsVector3();
        }
        private void PropagateMovement(GameObject go)
        {
            if (go == null) return;
    
            var unitComp = go.GetComponentInChildren<Unit>();
            if (unitComp == null) return;
    
            UnitDefinition def = unitComp.definition;
            FactionHQ    hq  = unitComp.NetworkHQ;
    
            var rec = new UndoManager.ActionRecord {
                Type            = UndoManager.ActionType.Spawn,
                Obj             = go,
                PrefabDef       = def,
                Position        = new GlobalPosition(go.transform.position),
                Rotation        = go.transform.rotation,
                Faction         = hq,
                Skill           = spawnSkill.Value,                     
                InitialVelocity = go.GetComponent<Rigidbody>()?.velocity ?? Vector3.zero
            };
    
            undoMgr.Record(rec);
            undoMgr.Redo();
        }

        // — Dragging —
        private void BeginDrag()
        {
            if (!Raycast(out var hit))
                return;
            if (hit.collider == null)
            {
                Logger.LogInfo("[BeginDrag] Hit water plane, no object to drag.");
                return;
            }
            dragTarget = GetClickedObject(hit);


            if (dragTarget == null) return;
            dragGroundY = hit.point.y;
            dragPlane = new Plane(Vector3.up, new Vector3(0, dragGroundY, 0));
            verticalOffset = dragTarget.transform.position.y - dragGroundY;

            DisablePhysicsAndColliders(dragTarget);
            lastMouseY = Input.mousePosition.y;

            undoMgr.Record(new UndoManager.ActionRecord
            {
                Type = UndoManager.ActionType.Move,
                Obj = dragTarget,
                Position = new GlobalPosition(dragTarget.transform.position)
            });
        }
        private void HandleDrag()
        {
            if (dragTarget == null) return;
         //   Debug.Log("HandleDrag CALLED");
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift);

            if (!shiftHeld && lastShiftHeld)
            {
                dragPlane = new Plane(Vector3.up, dragTarget.transform.position);
            }

            var cam = Camera.main;

            if (shiftHeld && !lastShiftHeld)
            {
                var downOrigin = dragTarget.transform.position + Vector3.up * 1000f;
                if (Physics.Raycast(downOrigin, Vector3.down, out var downHit, Mathf.Infinity))
                {
                    dragGroundY = downHit.point.y;
                    verticalOffset = dragTarget.transform.position.y - dragGroundY;
                }
            }

            if (shiftHeld)
            {
                float dy = Input.mousePosition.y - lastMouseY;
                float sensitivity = Vector3.Distance(cam.transform.position,dragTarget.transform.position)* 0.001f;
                verticalOffset += dy * sensitivity;
                lastMouseY = Input.mousePosition.y;

                float newY = dragGroundY + verticalOffset;
                newY = Mathf.Max(newY, dragGroundY + spawnHeight.Value);

                if (SnapGrid.Value > 0f)
                    newY = Mathf.Round(newY / SnapGrid.Value) * SnapGrid.Value;

                var pos = dragTarget.transform.position;
                pos.y = newY;
                dragTarget.transform.position = pos;
            }
            else
            {
                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                if (dragPlane.Raycast(ray, out float enter))
                {
                    Vector3 planeHit = ray.GetPoint(enter);
                    if (SnapToGround.Value)
                    {
                        float localWaterY = waterLevel.Value + Datum.origin.position.y;
                        var downOrigin = planeHit + Vector3.up * 1000f;
                        if (Physics.Raycast(downOrigin, Vector3.down, out var downHit, Mathf.Infinity))
                        {
                            float realGroundY = Mathf.Max(downHit.point.y, localWaterY);
                            float yPos = realGroundY + verticalOffset;
                            yPos = Mathf.Max(yPos, realGroundY + spawnHeight.Value);
                            var newPos = new Vector3(planeHit.x, yPos, planeHit.z);
                            dragTarget.transform.position = SnapToGlobalGrid(newPos);
                        }
                    }
                    else
                    {
                        float yPos = planeHit.y + verticalOffset;
                        float minY = spawnHeight.Value + Datum.origin.position.y;
                        yPos = Mathf.Max(yPos, minY);
                        var newPos = new Vector3(planeHit.x, yPos, planeHit.z);
                        dragTarget.transform.position = SnapToGlobalGrid(newPos);
                    }
                }
                lastMouseY = Input.mousePosition.y;
            }
            lastShiftHeld = shiftHeld;
        }
        private void EndDrag()
        {
            if (dragTarget == null) return;
            EnablePhysicsAndColliders();
            PropagateMovement(dragTarget);

            dragTarget = null;
            dragRigidbody = null;
            dragColliders = null;
            dragOffset = Vector3.zero;
        }

        // — Rotation —
        private void BeginRotation()
        {
            if (!Raycast(out var hit))
                return;
            if (hit.collider == null)
            {
                Logger.LogInfo("[BeginRotation] Hit water plane, no object to rotate.");
                return;
            }
            rotatingTarget = GetClickedObject(hit);


            if (rotatingTarget == null)
                return;

            isRotating = true;
            lastMouseX = Input.mousePosition.x;
            lastMouseY = Input.mousePosition.y;
            undoMgr.Record(new UndoManager.ActionRecord
            {
                Type = UndoManager.ActionType.Rotate,
                Obj = rotatingTarget,
                Rotation = rotatingTarget.transform.rotation
            });
            DisablePhysicsAndColliders(rotatingTarget);
        }
        private void HandleRotation()
        {
            if (rotatingTarget == null) return;

            float currentX = Input.mousePosition.x;
            float currentY = Input.mousePosition.y;
            float deltaX = currentX - lastMouseX;
            float deltaY = currentY - lastMouseY;
            lastMouseX = currentX;
            lastMouseY = currentY;

            const float rotationSpeed = 0.2f;
            float grid = SnapAngle.Value;

            if (Input.GetKey(KeyCode.LeftShift))
            {
                rotatingTarget.transform.Rotate(rotatingTarget.transform.forward,deltaX * rotationSpeed,Space.World);
                rotatingTarget.transform.Rotate(rotatingTarget.transform.right,-deltaY * rotationSpeed,Space.World);
                if (grid > 0f)
                {
                    Vector3 e = rotatingTarget.transform.eulerAngles;
                    e.x = Mathf.Round(e.x / grid) * grid;
                    e.y = Mathf.Round(e.y / grid) * grid;
                    e.z = Mathf.Round(e.z / grid) * grid;
                    rotatingTarget.transform.eulerAngles = e;
                }
            }
            else
            {
                var cam = Camera.main.transform;
                Ray ray = cam.GetComponent<Camera>().ScreenPointToRay(Input.mousePosition);
                Plane plane = new(Vector3.up, rotatingTarget.transform.position);
                if (!plane.Raycast(ray, out float enter)) return;

                Vector3 hitPoint = ray.GetPoint(enter);
                Vector3 toCursor = hitPoint - rotatingTarget.transform.position;
                toCursor.y = 0;
                if (toCursor.sqrMagnitude < 1e-6) return;

                Quaternion targetRot = Quaternion.LookRotation(toCursor.normalized, Vector3.up);
                Vector3 e = targetRot.eulerAngles;
                // snap yaw only
                if (grid > 0f)
                    e.y = Mathf.Round(e.y / grid) * grid;

                rotatingTarget.transform.rotation = Quaternion.Euler(0, e.y, 0);
            }
        }
        private void EndRotation()
        {
            if (rotatingTarget == null) return;

            EnablePhysicsAndColliders();
            PropagateMovement(rotatingTarget);

            isRotating = false;
            rotatingTarget = null;
            rotatingRigidbody = null;
            rotatingColliders = null;
        }

        // — Physics Toggle Helpers —
        private void DisablePhysicsAndColliders(GameObject go)
        {
            if (_savedRigidbodies != null &&
               _savedRbKinematic != null &&
               //_savedRbUseGravity != null &&
               _savedColliders != null &&
               _savedColliderEnabled != null)
            {
            EnablePhysicsAndColliders();
            }
            _savedRigidbodies = go.GetComponentsInChildren<Rigidbody>(true);
            _savedRbKinematic = new bool[_savedRigidbodies.Length];
            //_savedRbUseGravity = new bool[_savedRigidbodies.Length];

            for (int i = 0; i < _savedRigidbodies.Length; i++)
            {
                var rb = _savedRigidbodies[i];
                _savedRbKinematic[i] = rb.isKinematic;
                //_savedRbUseGravity[i] = rb.useGravity;
                rb.isKinematic = true;
                rb.useGravity = false;
            }
            _savedColliders = go.GetComponentsInChildren<Collider>(true);
            _savedColliderEnabled = new bool[_savedColliders.Length];
            for (int i = 0; i < _savedColliders.Length; i++)
            {
                var col = _savedColliders[i];
                _savedColliderEnabled[i] = col.enabled;
                col.enabled = false;
            }
        }
        private void EnablePhysicsAndColliders()
        {
            if (_savedRigidbodies != null)
            {
                for (int i = 0; i < _savedRigidbodies.Length; i++)
                {
                    var rb = _savedRigidbodies[i];
                    rb.isKinematic = _savedRbKinematic[i];
                    // rb.useGravity = _savedRbUseGravity[i];
                }
                _savedRigidbodies = null;
                _savedRbKinematic = null;
               // _savedRbUseGravity = null;
            }
            if (_savedColliders != null)
            {
                for (int i = 0; i < _savedColliders.Length; i++)
                {
                    var col = _savedColliders[i];
                    col.enabled = _savedColliderEnabled[i];
                }
                _savedColliders = null;
                _savedColliderEnabled = null;
            }
        }
        #endregion
        // — UI Construction —
        #region
        private void EnsureSelectionBoxUI()
        {
            if (boxGO != null) return;
            boxGO = new GameObject("SelectionBox");
            boxGO.transform.SetParent(_uiCanvas.transform, false);
            boxRT = boxGO.AddComponent<RectTransform>();
            var img = boxGO.AddComponent<Image>();
            img.color = new Color(0.8f, 0.2f, 0.2f, SelectionOpacity.Value);
            boxGO.SetActive(false);
        }
        private void StartBoxSelect(Vector2 screenPos)
        {
            EnsureSelectionBoxUI();
            isBoxSelecting = true;
            boxStartScreen = screenPos;
            boxCurrentScreen = screenPos;
            boxGO.SetActive(true);
            UpdateBoxSelect(screenPos);
        }
        private void UpdateBoxSelect(Vector2 current)
        {
            if (!isBoxSelecting) return;
            boxCurrentScreen = current;
            Vector2 min = Vector2.Min(boxStartScreen, current);
            Vector2 max = Vector2.Max(boxStartScreen, current);
            Vector2 size = max - min;

            boxRT.anchorMin = Vector2.zero;
            boxRT.anchorMax = Vector2.zero;
            boxRT.pivot = Vector2.zero;
            boxRT.anchoredPosition = min;
            boxRT.sizeDelta = size;
        }
        private void EndBoxSelect()
        {
            if (!isBoxSelecting) return;
            Vector2 min = Vector2.Min(boxStartScreen, boxCurrentScreen);
            Vector2 max = Vector2.Max(boxStartScreen, boxCurrentScreen);

            selectedMovers.Clear();
            Camera cam = Camera.main;

            void TryAdd<T>(T[] objs) where T : Component
            {
                foreach (var o in objs)
                {
                    Vector3 screen = cam.WorldToScreenPoint(o.transform.position);
                    if (screen.z < 0) continue; // behind camera
                    if ((o.transform.position - cam.transform.position).sqrMagnitude > maxDistance.Value * maxDistance.Value) continue;
                    if (screen.x >= min.x && screen.x <= max.x &&
                        screen.y >= min.y && screen.y <= max.y)
                    {
                        selectedMovers.Add(o.gameObject);
                    }
                }
            }

            TryAdd(FindObjectsOfType<GroundVehicle>());
            TryAdd(FindObjectsOfType<Ship>());

            Logger.LogInfo($"Box-selected {selectedMovers.Count} movers.");
            isBoxSelecting = false;
            boxGO.SetActive(false);
        }
        private void RefreshSelectionBoxes()
        {
            EnsureSelectionBoxUI();
            // 1) remove stale boxes
            foreach (var kv in selectionBoxes.ToArray())
                if (!selectedMovers.Contains(kv.Key))
                {
                    Destroy(kv.Value.gameObject);
                    selectionBoxes.Remove(kv.Key);
                }

            // 2) update / create boxes
            foreach (var go in selectedMovers)
            {
                if (!selectionBoxes.TryGetValue(go, out var rt))
                {
                    var box = new GameObject("SelBox", typeof(RectTransform), typeof(Image));
                    box.transform.SetParent(_uiCanvas.transform, false);
                    rt = box.GetComponent<RectTransform>();
                    var img = box.GetComponent<Image>();
                    img.color = new Color(0.8f, 0.2f, 0.2f, SelectionOpacity.Value);
                    img.sprite = null;
                    img.type = Image.Type.Sliced;
                    selectionBoxes[go] = rt;
                }

                // project bounds > screen rect
                var rend = go.GetComponentInChildren<Renderer>();
                if (rend == null) continue;
                var b = rend.bounds;
                Vector3[] corners =
                [
                    Camera.main.WorldToScreenPoint(b.min),
                    Camera.main.WorldToScreenPoint(new Vector3(b.min.x,b.min.y,b.max.z)),
                    Camera.main.WorldToScreenPoint(new Vector3(b.min.x,b.max.y,b.min.z)),
                    Camera.main.WorldToScreenPoint(new Vector3(b.max.x,b.min.y,b.min.z)),
                    Camera.main.WorldToScreenPoint(b.max)
                ];
                float minX = corners.Min(c => c.x);
                float maxX = corners.Max(c => c.x);
                float minY = corners.Min(c => c.y);
                float maxY = corners.Max(c => c.y);

                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.pivot = Vector2.zero;
                rt.anchoredPosition = new Vector2(minX, minY);
                rt.sizeDelta = new Vector2(maxX - minX, maxY - minY);
            }
        }

        private void CleanupUI()
        {
            if (_uiCanvas) Destroy(_uiCanvas);
            _uiCanvas = null;
            _menuPanelGO = null;

            // selection rectangles
            foreach (var rt in selectionBoxes.Values)
                if (rt) Destroy(rt.gameObject);
            selectionBoxes.Clear();
        }
        private void CreateMenuUI()
        {
            // 1) Canvas
            _uiCanvas = new GameObject("SpawnerMenuUI");
            var canvas = _uiCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _uiCanvas.AddComponent<CanvasScaler>();
            _uiCanvas.AddComponent<GraphicRaycaster>();

            // 2) Main panel
            _menuPanelGO = new GameObject("MenuPanel");
            _menuPanelGO.transform.SetParent(_uiCanvas.transform, false);
            var rt = _menuPanelGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 1);
            rt.anchoredPosition = new Vector2(0, 0);
            rt.sizeDelta = new Vector2(220, Screen.height);


            var img = _menuPanelGO.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0.8f);

            var allUnits = Resources.FindObjectsOfTypeAll<UnitDefinition>();
            var vehicles = Resources.FindObjectsOfTypeAll<VehicleDefinition>().Cast<UnitDefinition>().ToList();
            var ships = Resources.FindObjectsOfTypeAll<ShipDefinition>().Cast<UnitDefinition>().ToList();
            var aircraft = Resources.FindObjectsOfTypeAll<AircraftDefinition>().Cast<UnitDefinition>().ToList();
            var buildings = Resources.FindObjectsOfTypeAll<BuildingDefinition>().Cast<UnitDefinition>().ToList();
            var bombs = allUnits.Where(u => string.Equals(u.code, "BOMB", StringComparison.OrdinalIgnoreCase)).ToList();
            var missiles = allUnits.Where(u => string.Equals(u.code, "MSL", StringComparison.OrdinalIgnoreCase)).ToList();
            var containers = allUnits.Where(u => string.Equals(u.code, "CTNR", StringComparison.OrdinalIgnoreCase)).ToList();
            var pilots = allUnits.Where(u => string.Equals(u.code, "PILOT", StringComparison.OrdinalIgnoreCase)).ToList();
            var munitions = bombs.Concat(missiles).Concat(containers).Concat(pilots).ToList();
            _categories =
            [
                ("Vehicles", vehicles),
                ("Ships", ships),
                ("Aircraft", aircraft),
                ("Buildings",buildings),
                ("Munitions",munitions),
                //("Bombs", bombs),
                //("Missiles", missiles),
                //("Containers", containers)
            ];

            // 4) Show the first layer
            ShowCategories();
        }

        private void ClearPanel()
        {
            foreach (Transform c in _menuPanelGO.transform)
                Destroy(c.gameObject);
        }

        //MENU
        private void ShowCategories()
        {
            ClearPanel();
            float y = -10f;

            // ——— Faction selector ———
            CreateLabel($"Select Faction", y);
            y -= 20f;
            for (int i = 0; i < _factionNames.Length; i++)
            {
                string facName = _factionNames[i];
                var btn = new GameObject($"{facName}Btn", typeof(RectTransform), typeof(Image), typeof(Button));
                btn.transform.SetParent(_menuPanelGO.transform, false);

                var rt = btn.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(0, 1);
                rt.pivot = new Vector2(0, 1);
                rt.anchoredPosition = new Vector2(10, y);
                rt.sizeDelta = new Vector2(200, 30);

                var img = btn.GetComponent<Image>();
                // highlight the selected index
                img.color = (SelectedFactionIndex.Value == i)
                    ? new Color(0.4f, 0.8f, 1f, 0.9f)
                    : new Color(1f, 1f, 1f, 0.8f);

                var button = btn.GetComponent<Button>();
                int capture = i;  // closure safety
                button.onClick.AddListener(() =>
                {
                    SelectedFactionIndex.Value = capture;
                    Logger.LogInfo($"Menu: selected faction '{_factionNames[capture]}'");
                    ShowCategories();  // refresh highlights
                });

                // Text
                CreateButtonText(btn.transform, facName);

                y -= 35f;
            }

            CreateLabel($"Spawn Settings / utility", y);
            y -= 20f;

            // ——— Physics-on-spawn toggle ———
            {
                var btn = new GameObject("StaticSpawnBtn",
                                         typeof(RectTransform), typeof(Image), typeof(Button));
                btn.transform.SetParent(_menuPanelGO.transform, false);

                var rt = btn.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(0, 1);
                rt.pivot = new Vector2(0, 1);
                rt.anchoredPosition = new Vector2(10, y);
                rt.sizeDelta = new Vector2(200, 30);

                btn.GetComponent<Image>().color = staticSpawn
                    ? new Color(0.4f, 0.8f, 1f, 0.9f)   // highlighted when ON
                    : new Color(1f, 1f, 1f, 0.8f);

                var b = btn.GetComponent<Button>();
                b.onClick.AddListener(() =>
                {
                    staticSpawn = !staticSpawn;
                    ShowCategories();          // refresh highlight / text
                });

                CreateButtonText(btn.transform,
                    "Spawn Static: " + (staticSpawn ? "ON" : "OFF"));

            }
            y -= 35f;

            // ——— Hold-Position toggle ———
            {
                var btn = new GameObject("HoldPosBtn",
                                          typeof(RectTransform), typeof(Image), typeof(Button));
                btn.transform.SetParent(_menuPanelGO.transform, false);

                var rtHP = btn.GetComponent<RectTransform>();
                rtHP.anchorMin = new Vector2(0, 1);
                rtHP.anchorMax = new Vector2(0, 1);
                rtHP.pivot = new Vector2(0, 1);
                rtHP.anchoredPosition = new Vector2(10, y);
                rtHP.sizeDelta = new Vector2(200, 30);

                btn.GetComponent<Image>().color = holdPositionEnabled
                    ? new Color(0.4f, 0.8f, 1f, 0.9f)   // highlighted when ON
                    : new Color(1f, 1f, 1f, 0.8f);

                var bHP = btn.GetComponent<Button>();
                bHP.onClick.AddListener(() =>
                {
                    holdPositionEnabled = !holdPositionEnabled;
                    ShowCategories();        // redraw to refresh highlight/text
                });

                CreateButtonText(btn.transform,
                    "Hold Pos on spawn: " + (holdPositionEnabled ? "ON" : "OFF"));

            }
            y -= 35f;

            // ——— Repair-Nearest-Airbase button ———
            {
                var btn = new GameObject("RepairNearestBtn", typeof(RectTransform), typeof(Image), typeof(Button));
                btn.transform.SetParent(_menuPanelGO.transform, false);

                var rtR = btn.GetComponent<RectTransform>();
                rtR.anchorMin = new Vector2(0, 1);
                rtR.anchorMax = new Vector2(0, 1);
                rtR.pivot = new Vector2(0, 1);
                rtR.anchoredPosition = new Vector2(10, y);
                rtR.sizeDelta = new Vector2(200, 30);

                btn.GetComponent<Image>().color = new Color(1, 1, 1, 0.8f);

                btn.GetComponent<Button>().onClick.AddListener(() =>
                {
                    StartCoroutine(RepairNearestAirbaseCoroutine());
                });

                CreateButtonText(btn.transform, "Repair Nearest Airbase");
            }
            y -= 35f;

            // ——— Current selections ———
            // CreateLabel($"Current Selection", y);
            // y -= 20f;
            CreateLabel($"Selected: {_selectedDisplayName}", y);
            y -= 20f;

            // ——— Category buttons ———
            foreach (var (catName, defs) in _categories)
            {
                if (defs.Count == 0) continue;

                var btn = new GameObject(catName + "Btn", typeof(RectTransform), typeof(Image), typeof(Button));
                btn.transform.SetParent(_menuPanelGO.transform, false);
                var rt2 = btn.GetComponent<RectTransform>();
                rt2.anchorMin = new Vector2(0, 1);
                rt2.anchorMax = new Vector2(0, 1);
                rt2.pivot = new Vector2(0, 1);
                rt2.anchoredPosition = new Vector2(10, y);
                rt2.sizeDelta = new Vector2(200, 30);

                btn.GetComponent<Image>().color = new Color(1, 1, 1, 0.8f);
                var b = btn.GetComponent<Button>();
                string capture = catName;
                b.onClick.AddListener(() => ShowCategoryEntries(capture));

                CreateButtonText(btn.transform, $"{catName} ({defs.Count})");

                y -= 35f;
            }

            CreateLabel($"Faction Messaging", y);
            y -= 20f;
            // ─── Message Text box ───                                    
            {

                // InputField container
                var inpGO = new GameObject("MessageInput",
                           typeof(RectTransform), typeof(Image), typeof(InputField));
                inpGO.transform.SetParent(_menuPanelGO.transform, false);

                var rtI = inpGO.GetComponent<RectTransform>();
                rtI.anchorMin = new Vector2(0, 1);
                rtI.anchorMax = new Vector2(0, 1);
                rtI.pivot = new Vector2(0, 1);
                rtI.anchoredPosition = new Vector2(10, y);
                rtI.sizeDelta = new Vector2(200, 28);

                // background colour
                inpGO.GetComponent<Image>().color = new Color(1, 1, 1, 0.9f);

                var inp = inpGO.GetComponent<InputField>();
                _messageInput = inp;                 // keep a handle for later

                // ---- child Text component (actual input text) ----
                var textGO = new GameObject("Text", typeof(RectTransform), typeof(Text));
                textGO.transform.SetParent(inpGO.transform, false);
                var rtT = textGO.GetComponent<RectTransform>();
                rtT.anchorMin = Vector2.zero;
                rtT.anchorMax = Vector2.one;
                rtT.offsetMin = new Vector2(5, 1);
                rtT.offsetMax = new Vector2(-5, -1);

                var txt = textGO.GetComponent<Text>();
                txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                txt.color = Color.black;
                txt.alignment = TextAnchor.MiddleLeft;
                txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                txt.verticalOverflow = VerticalWrapMode.Overflow;

                // ---- child Placeholder ----
                var phGO = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
                phGO.transform.SetParent(inpGO.transform, false);
                var rtP = phGO.GetComponent<RectTransform>();
                rtP.anchorMin = Vector2.zero;
                rtP.anchorMax = Vector2.one;
                rtP.offsetMin = rtT.offsetMin;
                rtP.offsetMax = rtT.offsetMax;

                var ph = phGO.GetComponent<Text>();
                ph.font = txt.font;
                ph.fontStyle = FontStyle.Italic;
                ph.color = new Color(.4f, .4f, .4f, .8f);
                ph.text = "enter message …";
                ph.alignment = TextAnchor.MiddleLeft;

                // wire them up
                inp.textComponent = txt;
                inp.placeholder = ph;

                // start value = current config entry
                inp.text = _messageText;

                // update config entry whenever the field changes
                inp.onValueChanged.AddListener(value => _messageText = value);

                // send message automatically when RETURN is pressed (onEndEdit fires)
                inp.onEndEdit.AddListener(_ =>
                {
                    if (Input.GetKeyDown(KeyCode.Return)          // only if Return pressed
                        || Input.GetKeyDown(KeyCode.KeypadEnter))
                    {
                        MessageOut();
                    }
                });

                y -= 35f;            // push subsequent UI elements down
            }

            // ——— Message-Sound toggle ———
            {
                var btn = new GameObject("MsgSoundBtn",
                                         typeof(RectTransform), typeof(Image), typeof(Button));
                btn.transform.SetParent(_menuPanelGO.transform, false);

                var rtMS = btn.GetComponent<RectTransform>();
                rtMS.anchorMin = new Vector2(0, 1);
                rtMS.anchorMax = new Vector2(0, 1);
                rtMS.pivot = new Vector2(0, 1);
                rtMS.anchoredPosition = new Vector2(10, y);
                rtMS.sizeDelta = new Vector2(200, 30);

                btn.GetComponent<Image>().color = _messageSound
                    ? new Color(0.4f, 0.8f, 1f, 0.9f)
                    : new Color(1f, 1f, 1f, 0.8f);

                btn.GetComponent<Button>().onClick.AddListener(() =>
                {
                    _messageSound = !_messageSound;
                    ShowCategories();
                });

                CreateButtonText(btn.transform,
                    "Message Sound: " + (_messageSound ? "ON" : "OFF"));

                y -= 35f;          // push following UI down
            }

            // ——— Send-Message button ———
            {
                var btn = new GameObject("SendMessageBtn",
                                         typeof(RectTransform), typeof(Image), typeof(Button));
                btn.transform.SetParent(_menuPanelGO.transform, false);

                var rtSM = btn.GetComponent<RectTransform>();
                rtSM.anchorMin = new Vector2(0, 1);
                rtSM.anchorMax = new Vector2(0, 1);
                rtSM.pivot = new Vector2(0, 1);
                rtSM.anchoredPosition = new Vector2(10, y);
                rtSM.sizeDelta = new Vector2(200, 30);

                btn.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.8f);

                btn.GetComponent<Button>().onClick.AddListener(MessageOut);

                CreateButtonText(btn.transform, "Send to faction");
                y -= 35f;                 // shift everything below it down
            }

            // ─── Key-bind legend ───
            string[] legend =
            {
                "─ GENERAL ─",
                $"{ToggleEditModeKey.Value}: Toggle Edit Mode",

                "─ SELECTION ─",
                "LMB-Click: Select unit",
                "LMB-drag: Box-select units",
                "RMB-Click: Command unit(s) to move",
                "",

                "─ MOVE ─",
                "LMB-drag: Move unit (horizontal)",
                "Shift+LMB-drag: Move unit (vertical)",
                "RMB-drag: Rotate unit (yaw)",
                "Shift+RMB-drag: Free-axis rotate",
                "",

                "─ SPAWN / DELETE ─",
                "Ctrl+LMB-Click: Spawn unit",
                "Ctrl+RMB-Click: Delete unit",
                "Delete: Delete all selected units",
                "",

                "─ CLIPBOARD ─",
                "Ctrl+C: Copy unit + faction",
                "Ctrl+Z: Undo",
                "Ctrl+Shift+Z: Redo",
                "",

                "─ ADVANCED SETTINGS ─",
                "F1: ConfigManager (separate mod)"
            };


            foreach (var line in legend)
            {
                CreateLabel(line, y, 12);
                y -= 18f;
            }
            // y -= 10f;
        }
        private void ShowCategoryEntries(string categoryName)
        {
            ClearPanel();
            {
                var backBtn = new GameObject("BackBtn", typeof(RectTransform), typeof(Image), typeof(Button));
                backBtn.transform.SetParent(_menuPanelGO.transform, false);

                var rt = backBtn.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(0, 1);
                rt.pivot = new Vector2(0, 1);
                rt.anchoredPosition = new Vector2(10, -10);
                rt.sizeDelta = new Vector2(200, 30);

                backBtn.GetComponent<Image>().color = new Color(1, 1, 1, 0.8f);
                var btn = backBtn.GetComponent<Button>();
                btn.onClick.AddListener(ShowCategories);

                var txtGO = new GameObject("Text", typeof(RectTransform), typeof(Text));
                txtGO.transform.SetParent(backBtn.transform, false);
                var txtRT = txtGO.GetComponent<RectTransform>();
                txtRT.anchorMin = Vector2.zero;
                txtRT.anchorMax = Vector2.one;
                txtRT.offsetMin = new Vector2(5, 0);
                txtRT.offsetMax = new Vector2(-5, 0);
                var txt = txtGO.GetComponent<Text>();
                txt.text = "< Back";
                txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                txt.alignment = TextAnchor.MiddleLeft;
                txt.color = Color.black;
                txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                txt.verticalOverflow = VerticalWrapMode.Overflow;
            }

            // Definitions
            var defs = _categories.First(c => c.Name == categoryName).Defs;
            float y = -50;
            var x = 0;
            foreach (var def in defs)
            {
                var btn = new GameObject(def.name + "Btn", typeof(RectTransform), typeof(Image), typeof(Button));
                btn.transform.SetParent(_menuPanelGO.transform, false);

                var rt = btn.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(0, 1);
                rt.pivot = new Vector2(0, 1);
                rt.anchoredPosition = new Vector2(10, y);
                rt.sizeDelta = new Vector2(200, 30);

                btn.GetComponent<Image>().color = new Color(1, 1, 1, 0.8f);
                var button = btn.GetComponent<Button>();
                string capture = def.name;
                button.onClick.AddListener(() =>
                {
                    SelectedDefinition.Value = capture;
                    _selectedDisplayName = def.unitName;
                    ShowCategories();
                    Logger.LogInfo($"Menu: selected '{capture}'");
                });

                var txtGO = new GameObject("Text", typeof(RectTransform), typeof(Text));
                txtGO.transform.SetParent(btn.transform, false);
                var txtRT = txtGO.GetComponent<RectTransform>();
                txtRT.anchorMin = Vector2.zero;
                txtRT.anchorMax = Vector2.one;
                txtRT.offsetMin = new Vector2(5, 0);
                txtRT.offsetMax = new Vector2(-5, 0);
                var txt = txtGO.GetComponent<Text>();
                txt.text = def.unitName;
                txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                txt.alignment = TextAnchor.MiddleLeft;
                txt.color = Color.black;
                txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                txt.verticalOverflow = VerticalWrapMode.Overflow;
                x++;
                if (x is 8 or 23 or 25) y -= 50;
                else y -= 35;
            }
        }
        private void CreateLabel(string text, float y, int fontSize = 14)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(_menuPanelGO.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(10, y);
            rt.sizeDelta = new Vector2(200, fontSize + 4);

            var txt = go.GetComponent<Text>();
            txt.text = text;
            txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.fontSize = fontSize;
            txt.alignment = TextAnchor.UpperLeft;
            txt.color = Color.white;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
        }
        private void CreateButtonText(Transform parent, string text)
        {
            var txtGO = new GameObject("Text", typeof(RectTransform), typeof(Text));
            txtGO.transform.SetParent(parent, false);
            var txtRT = txtGO.GetComponent<RectTransform>();
            txtRT.anchorMin = Vector2.zero;
            txtRT.anchorMax = Vector2.one;
            txtRT.offsetMin = new Vector2(5, 0);
            txtRT.offsetMax = new Vector2(-5, 0);

            var txt = txtGO.GetComponent<Text>();
            txt.text = text;
            txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.alignment = TextAnchor.MiddleLeft;
            txt.color = Color.black;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
        }
        #endregion
    }
}