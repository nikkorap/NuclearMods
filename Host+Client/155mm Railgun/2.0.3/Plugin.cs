using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration; 
using BepInEx.Logging;  
using HarmonyLib;
using HarmonyLib.Tools;
using UnityEngine; 
using UnityEngine.SceneManagement;

namespace _155mm_Railgun
{

    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        /*
        [HarmonyPatch(typeof(Gun), "FixedUpdate")]
        internal static class RailgunFireOnRelease
        {
            // ── tweakables ─────────────────────────────────────── 
            private const float powerRequested = 100f; 
            // ───────────────────────────────────────────────────── 
            internal enum ShotKind { NoShot, NoFx, SmallFx, BigFx }
            public static readonly Dictionary<int, ShotKind> ShotKinds = new();
            internal static ShotKind KindForNextBullet;          // set during RELEASE

            public class ChargeState
            {
                public bool Holding;
                public float Accumulated;          // powerRequested drawn during this charge-cycle
                public float LastShotEnergy;       // powerRequested drawn for the most recent shot
            }

            private static readonly ConditionalWeakTable<Gun, ChargeState> _states = new();

            private static readonly AccessTools.FieldRef<Gun, bool>
                _triggerPulled = AccessTools.FieldRefAccess<Gun, bool>("triggerPulled");

            private static readonly AccessTools.FieldRef<Gun, Unit>
                _attachedUnit = AccessTools.FieldRefAccess<Gun, Unit>("attachedUnit");

            private static bool IsRailgun(Gun g)
                => g.info != null && g.info.name == "155 mm Railgun";

            [HarmonyPatch(typeof(BulletSim.Bullet))]
            static class BulletCtorPatch
            {
                // 2) tell Harmony *which* constructor to patch at runtime
                static MethodBase TargetMethod()
                {
                    // pick **the first instance constructor you find**
                    // (works whether it is public, private, or has IL2CPP stubs)
                    return typeof(BulletSim.Bullet)
                           .GetConstructors(BindingFlags.Instance |
                                            BindingFlags.Public |
                                            BindingFlags.NonPublic)
                           .First();
                }

                // 3) the actual patch
                [HarmonyPostfix]
                static void Postfix(BulletSim.Bullet __instance)
                {
                    if (RailgunFireOnRelease.KindForNextBullet
                        != RailgunFireOnRelease.ShotKind.NoShot)
                    {
                        RailgunFireOnRelease.ShotKinds[__instance.GetHashCode()] =
                            RailgunFireOnRelease.KindForNextBullet;
                    }
                }
            }

            // ------------------------------------------------------------------ 
            private static void Prefix(Gun __instance)
            {
                if (!IsRailgun(__instance)) return;

                bool pressedThisFrame = _triggerPulled(__instance);
                var state = _states.GetOrCreateValue(__instance);

                // --- 1. CHARGING ------------------------------------------------ 
                if (pressedThisFrame)
                {
                    state.Holding = true;
                    _triggerPulled(__instance) = false;          // suppress vanilla shot
                    DrainPower(__instance, state);               // draw & accumulate energy
                  //  Debug.Log(state.Accumulated);
                }
                // --- 2. RELEASE ------------------------------------------------- 
                else if (state.Holding)
                {
                    state.Holding = false;
                  //  DrainPower(__instance, state);               // (optional final tick)
                    state.LastShotEnergy = state.Accumulated;    // expose for others
                    state.Accumulated = 0f;                      // reset for next shot


                    ShotKind kind;

                    float e = state.LastShotEnergy;     // kJ (or J – just keep the same unit)

                    // threshold table ----------------------------------------------------- 
                    if (e <= 0f) kind = ShotKind.NoShot;
                    else if (e <= 50f) kind = ShotKind.NoFx;
                    else if (e <= 150) kind = ShotKind.SmallFx;
                    else kind = ShotKind.BigFx;
                    Debug.Log($"energy is {e} shotkind is {kind}");
                    // --------------------------------------------------------------------- 

                    switch (kind)
                    {
                        case ShotKind.NoShot:
                            _triggerPulled(__instance) = false;  // abort – no round spawned
                            return;                              // leave Prefix()
                        default:
                            _triggerPulled(__instance) = true;   // let FixedUpdate spawn a round
                            KindForNextBullet = kind;            // remember for ctor patch
                            break;
                    }

                   // Debug.Log($"Railgun fired – {state.LastShotEnergy/100:F0} kJ consumed in charge.");

                   // if (state.LastShotEnergy > 1000) _triggerPulled(__instance) = true;           // allow one shot
                  //  else _triggerPulled(__instance) = false; 
                }
            }
            [HarmonyPatch(typeof(Gun), "FixedUpdate")]
            static class FixedUpdatePost                  // add just this one
            {
                static void Postfix() => KindForNextBullet = ShotKind.NoShot;
            }
            // ------------------------------------------------------------------
            private static void DrainPower(Gun gun, ChargeState state)
            {
                var unit = _attachedUnit(gun);

                if (unit is Aircraft a)
                {
                    PowerSupply ps = a.GetPowerSupply();
                    if (ps != null)
                    {
                        
                        float charge = ps.DrawPower(powerRequested) * Time.deltaTime;
                        state.Accumulated += charge;
                        Debug.Log($"Drawing:{charge} accumulated:{state.Accumulated}");
                    }
                }
            }
        }
        */
        // ── Config ───────────────────────────────────────
        private ConfigEntry<bool> _modEnabled;
        public ConfigEntry<bool> bigBoom;
        public ConfigEntry<int> maxTracers;
        private ConfigEntry<int> _recoilMultiplier;
        private ConfigEntry<Vector3> _localPosition;
        private ConfigEntry<Vector3> _localRotation;
        private ConfigEntry<Vector3> _localScale;

        private const string DonorMountName = "gun_27mm_internal";
        private const string DonorSourcePath = "Destroyer1/Hull_CF/Hull_CFF/turret_F/cannon_F";

        private const string NewName = "155 mm Railgun";
        private const string NewDescription ="This 155 mm railgun fires slugs at 2380 m/s, capable of devastating heavily armoured targets from range.";

        private GameObject _weaponGO;
        private WeaponInfo _targetInfo;
        private Sprite _origIcon;
        private bool _origBoresight;
        private string _origDescription;
        private float _origFireInterval;

        private Gun _donorGun;
        private Transform _origRecoilTransform;
        private float _origRecoilTravel;
        private float _origRecoilImpulse;

        private Coroutine _findCoroutine;
        private Harmony _harmony;

        internal class ConfigurationManagerAttributes
        {
            public bool? Browsable;
            public bool? IsAdvanced;
        }
        internal static Plugin Instance { get; private set; }
        private void Awake()
        {
            this.hideFlags = HideFlags.HideAndDontSave;
            Logger.LogInfo("Railgun-only plugin loading…");
            Instance = this;

            _modEnabled = Config.Bind("General", "ModEnabled", true, "Enable/disable the mod.");
            maxTracers = Config.Bind("General", "Max nr of tracers", 10);
            bigBoom = Config.Bind("General", "Big boom mode", false,
                new ConfigDescription("", null,
                new ConfigurationManagerAttributes { IsAdvanced = true }));

            _recoilMultiplier = Config.Bind("Overrides", "RecoilMultiplier", 100,
                new ConfigDescription("Recoil impulse as percentage (0–200%)",
                new AcceptableValueRange<int>(0, 200),
                new ConfigurationManagerAttributes { IsAdvanced = true }));

            _localPosition = Config.Bind("Transform", "LocalPosition", new Vector3(0f, -0.5f, -2f),
                new ConfigDescription("Local position of the weapon.", null,
                new ConfigurationManagerAttributes { IsAdvanced = true }));

            _localRotation = Config.Bind("Transform", "LocalRotation", new Vector3(0f, 0f, 180f),
                new ConfigDescription("Local Euler rotation of the weapon.", null,
                new ConfigurationManagerAttributes { IsAdvanced = true }));

            _localScale = Config.Bind("Transform", "LocalScale", Vector3.one,
                new ConfigDescription("Local scale of the weapon.", null,
                new ConfigurationManagerAttributes { IsAdvanced = true }));
            
            _modEnabled.SettingChanged += (_, __) => ToggleMod(_modEnabled.Value);
            _harmony = new Harmony("com.nikkorap.railgun");
            _harmony.PatchAll();
            _harmony.PatchAll(typeof(Bullet155TracerTrail));
            SceneManager.sceneLoaded += Bullet155TracerTrail.OnSceneLoaded;

            ToggleMod(_modEnabled.Value);

        }

        private void ToggleMod(bool enable)
        {
            if (enable)
            {
                Logger.LogInfo("Railgun mod enabled");
                _findCoroutine = StartCoroutine(WaitAndInject());
            }
            else
            {
                Logger.LogInfo("Railgun mod disabled – reverting");
                if (_findCoroutine != null) StopCoroutine(_findCoroutine);
                Revert();
            }
        }

        // ── Find donor prefab & inject ──────────────────
        private IEnumerator WaitAndInject()
        {
            var wait = new WaitForSeconds(3f);
            var path = DonorSourcePath.Split('/');

            while (_weaponGO == null)
            {
                _weaponGO = FindPrefab(path);
                if (_weaponGO != null) Inject();
                else yield return wait;
            }
        }
        private void Inject()
        {
            // Record original transform
            var tf = _weaponGO.transform;
            var origPos = tf.localPosition;
            var origRot = tf.localRotation;
            var origScale = tf.localScale;

            // Recoil tweaks
            _donorGun = _weaponGO.GetComponentInChildren<Gun>(true);
            GetField(_donorGun, "recoilTransform", out _origRecoilTransform);
            GetField(_donorGun, "recoilTravel", out _origRecoilTravel);
            GetField(_donorGun, "recoilImpulse", out _origRecoilImpulse);

            SetField(_donorGun, "recoilTransform", tf);
            SetField(_donorGun, "recoilTravel", 0f);
            
            if (_origRecoilImpulse != 0)
            {
                var mod = _origRecoilImpulse * (_recoilMultiplier.Value / 100f);
                SetField(_donorGun, "recoilImpulse", mod);
            }
            
            // WeaponInfo
            _targetInfo = _donorGun.info;
            _origIcon = _targetInfo.weaponIcon;
            _origBoresight = _targetInfo.boresight;
            _origDescription = _targetInfo.description;
            _origFireInterval = _targetInfo.fireInterval;

            var donorMount = Resources.FindObjectsOfTypeAll<WeaponMount>().First(m => m.name.Equals(DonorMountName, System.StringComparison.OrdinalIgnoreCase));

            _targetInfo.weaponIcon = donorMount.info.weaponIcon;
            _targetInfo.boresight = true;
            _targetInfo.description = NewDescription;
            _targetInfo.name = NewName;
            _targetInfo.fireInterval = 0;

            // Replace mount
            donorMount.mountName = NewName;
            donorMount.info = _targetInfo;
            donorMount.prefab = _weaponGO;
            
            // Apply final transform overrides
            tf.localPosition = _localPosition.Value;
            tf.localRotation = Quaternion.Euler(_localRotation.Value);
            tf.localScale = _localScale.Value;
            
            foreach (var mgr in Resources.FindObjectsOfTypeAll<WeaponManager>())
            {
                foreach (var set in mgr.hardpointSets)
                {
                    if (!set.weaponOptions.Contains(donorMount))
                        set.weaponOptions.Add(donorMount);
                }
            }
        }

        // ── Revert on disable / shutdown ────────────────
        private void Revert()
        {
            if (_weaponGO == null) return;

            // restore info
            _targetInfo.weaponIcon = _origIcon;
            _targetInfo.boresight = _origBoresight;
            _targetInfo.description = _origDescription;
            _targetInfo.fireInterval = _origFireInterval;

            // recoil
            SetField(_donorGun, "recoilTransform", _origRecoilTransform);
            SetField(_donorGun, "recoilTravel", _origRecoilTravel);
            SetField(_donorGun, "recoilImpulse", _origRecoilImpulse);

            _weaponGO = null;
        }

        // ── Helpers ─────────────────────────────────────
        private static GameObject FindPrefab(string[] path)
        {
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (!go.activeInHierarchy && go.transform.parent == null &&
                    go.name.Equals(path[0], System.StringComparison.OrdinalIgnoreCase))
                {
                    Transform t = go.transform;
                    for (int i = 1; i < path.Length; i++) { t = t.Find(path[i]); if (t == null) break; }
                    if (t != null) return t.gameObject;
                }
            }
            return null;
        }

        private static void SetField(object obj, string name, object val)
        => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    ?.SetValue(obj, val);

        private static bool GetField<T>(object obj, string name, out T value)
        {
            var f = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null && f.GetValue(obj) is T v) { value = v; return true; }
            value = default; return false;
        }

        [HarmonyPatch(typeof(Application), nameof(Application.version), MethodType.Getter)]
        private static class VersionGetterPatch
        {
            static void Postfix(ref string __result)
            {
                __result += $"_{MyPluginInfo.PLUGIN_GUID}-v{MyPluginInfo.PLUGIN_VERSION}";
                Plugin.Instance.Logger.LogWarning($"Updated game version to {__result}");
            }
        }
    }

    public static class Bullet155TracerTrail
    {
        const int SkipInitial = 2;
        // Scene-root caching
        static Transform _datumRoot;

        public static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var go = GameObject.Find("Datum");
            if (go) _datumRoot = go.transform;
        }
        private static readonly List<int> _activeFx = [];  
        class BulletState
        {
            public Vector3 prevPoint;
            public int skipCount;
            public int segCount;
            public bool impacted;
            public bool disabled;
        }
        static readonly Dictionary<int, BulletState> _state = new();

        // Pre-compute a jitter lookup table
        const int JITTER_TABLE_SIZE = 64;
        const float JITTER_MAX = 5f;
        static readonly Vector3[] _jitters = new Vector3[JITTER_TABLE_SIZE];
        static Bullet155TracerTrail()
        {
            for (int i = 0; i < JITTER_TABLE_SIZE; i++)
            {
                var x = Random.Range(-JITTER_MAX, JITTER_MAX);
                var y = Random.Range(-JITTER_MAX, JITTER_MAX);
                _jitters[i] = new Vector3(x, y, 0f);
            }
        }

        // Prefab caches
        static GameObject smallExplosion, bigExplosion, nukeExplosion;
        static GameObject Explosion1000kg()
        {
            if (smallExplosion) return smallExplosion;
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go.name == "explosion_1000kg")
                { smallExplosion = go; break; }
            if (!smallExplosion) Debug.LogError("[Tracer] explosion_1000kg not found");
            return smallExplosion;
        }
        static GameObject Explosion10_000kg()
        {
            if (bigExplosion) return bigExplosion;
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go.name == "explosion_10000kg")
                { bigExplosion = go; break; }
            if (!bigExplosion) Debug.LogError("[Tracer] explosion_10000kg not found");
            return bigExplosion;
        }
        static GameObject Explosion20kt()
        {
            if (nukeExplosion) return nukeExplosion;
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go.name == "explosion_1kt")
                { nukeExplosion = go; break; }
            if (!nukeExplosion) Debug.LogError("[Tracer] explosion_1kt not found");
            return nukeExplosion;
        }

        [HarmonyPatch(typeof(BulletSim.Bullet), nameof(BulletSim.Bullet.TrajectoryTrace),
            new[]{ typeof(Transform), typeof(WeaponInfo), typeof(Unit),
               typeof(BulletSim.ImpactEffect[]), typeof(float), typeof(bool), typeof(float) })]
        static class Trajectory_Patch
        {
            const float CloneLife = 5f;
            const float WidthFactor = 2f;

            static void Postfix(
                BulletSim.Bullet __instance,
                Transform muzzle,
                WeaponInfo info,
                Unit owner,
                BulletSim.ImpactEffect[] impactEffects,
                float tracerSize,
                bool visualOnly,
                float deltaTime)
            {
                if (info?.name == null || !info.name.Contains("155")) return;
                //   if (owner is not Aircraft) return;

                //   RailgunFireOnRelease.ShotKinds.TryGetValue( __instance.GetHashCode(), out var kind);

                int id = __instance.GetHashCode();
                if (!_state.TryGetValue(id, out var st))
                {
                    // first frame for this bullet
                    st = new BulletState { skipCount = 1 };
                    _state[id] = st;

                    // ── 2) Enforce hard limit ───────────────────────
                    _activeFx.Add(id);
                    if (_activeFx.Count > Plugin.Instance.maxTracers.Value)
                    {
                        int victim = _activeFx[0];      // oldest
                        _activeFx.RemoveAt(0);          // drop it from the live list
                        if (_state.TryGetValue(victim, out var v))
                            v.disabled = true;          // stop that shell’s FX
                    }
                }
                // If this shell's FX were disabled, bail out early
                if (st.disabled) return;

                // handle impact once
                if (__instance.impacted && !st.impacted)
                {
                    st.impacted = true;

                    GameObject hitfx;
                    if (Plugin.Instance.bigBoom.Value)
                        hitfx = Explosion20kt();
                    else
                        hitfx = Explosion1000kg();
                    if (hitfx && _datumRoot)
                    {
                        var fx = Object.Instantiate(hitfx, _datumRoot, false);
                        fx.transform.localPosition = __instance.position.AsVector3();
                        fx.transform.localRotation = Quaternion.identity;
                        Object.Destroy(fx, 6f);
                    }

                    return;
                }
                if (st.impacted) return;

                // seed first frame
                Vector3 raw = __instance.position.AsVector3();
                if (raw.sqrMagnitude < 1e-6f)
                {
                    st.skipCount++;
                    return;
                }
                if (Plugin.Instance.bigBoom.Value)
                {
                    if (st.skipCount < SkipInitial + 3)
                    {
                        st.skipCount++;
                        return;
                    }

                    if (st.skipCount == SkipInitial + 3)
                    {
                        st.prevPoint = raw;
                        st.skipCount++;
                        return;
                    }
                }
                else
                {
                    if (st.skipCount < SkipInitial)
                    {
                        st.skipCount++;
                        return;
                    }

                    if (st.skipCount == SkipInitial)
                    {
                        st.prevPoint = raw;
                        st.skipCount++;
                        return;
                    }
                }
                var jitter = _jitters[st.skipCount % JITTER_TABLE_SIZE];
                if (Plugin.Instance.bigBoom.Value)
                    jitter = jitter * 5;
                Vector3 point = raw + jitter;
                // compute segment
                Vector3 dirVec = point - st.prevPoint;
                float len = dirVec.magnitude;
                if (len < 0.05f || len > 100f)
                {
                    st.prevPoint = point;
                    st.skipCount++;
                    return;
                }
                dirVec.Normalize();

                // draw tracer segment
                if (__instance.tracer is GameObject segPrefab && _datumRoot)
                {
                    var seg = Object.Instantiate(segPrefab, _datumRoot, false);
                    seg.name = $"Tracer_{id}";
                    seg.transform.localPosition = st.prevPoint;
                    seg.transform.localRotation = Quaternion.LookRotation(dirVec);
                    Vector3 sc;

                    sc = seg.transform.localScale;
                    if (Plugin.Instance.bigBoom.Value)
                        sc = seg.transform.localScale * 5;

                    seg.transform.localScale = new Vector3(sc.x * WidthFactor, sc.y * WidthFactor, len);
                    seg.AddComponent<ThinAndDestroy>().duration = CloneLife;
                }
                
                // periodic AoE explosion
                st.segCount++;

                if (st.segCount % 5 == 0 && _datumRoot)
                {
                    GameObject aoefx;
                    if (Plugin.Instance.bigBoom.Value)
                    {
                        aoefx = Explosion10_000kg();
                        if (aoefx)
                        {
                            var fx = Object.Instantiate(aoefx, _datumRoot, false);
                            fx.transform.localPosition = point;
                            fx.transform.localRotation = Quaternion.identity;
                            fx.transform.Find("explosion_smoke")?.gameObject.SetActive(false);
                            fx.transform.Find("persistentSmoke")?.gameObject.SetActive(false);
                            fx.transform.Find("MushroomCloud")?.gameObject.SetActive(false);
                            fx.transform.Find("shrapnel")?.gameObject.SetActive(false);
                            fx.GetComponent<ExplosionAudio>().enabled = false;
                            Object.Destroy(fx, 6f);
                        }
                    }
                    else
                    {
                        aoefx = Explosion1000kg();
                        if (aoefx)
                        {
                            var fx = Object.Instantiate(aoefx, _datumRoot, false);
                            fx.transform.localPosition = point;
                            fx.transform.localRotation = Quaternion.identity;

                            fx.transform.Find("explosion_smoke")?.gameObject.SetActive(false);
                            fx.transform.Find("smoke_slow")?.gameObject.SetActive(false);
                            fx.transform.Find("shrapnel")?.gameObject.SetActive(false);
                            //fx.transform.Find("shockwaveDecal")?.gameObject.SetActive(false);
                            fx.transform.Find("shockwave")?.gameObject.SetActive(false);
                            fx.GetComponent<ExplosionAudio>().enabled = false;
                            Object.Destroy(fx, 6f);
                        }
                    }
                }
                st.prevPoint = point;
                st.skipCount++;
            }
        }
        [HarmonyPatch(typeof(BulletSim.Bullet), nameof(BulletSim.Bullet.Remove))]
        static class RemovePatch
        {
            static void Postfix(BulletSim.Bullet __instance)
            {
                int id = __instance.GetHashCode();
                _state.Remove(id);
                _activeFx.Remove(id);
            }
        }
        /*
        // --------------------------------------------------------------------
        [HarmonyPatch(typeof(BulletSim.Bullet), nameof(BulletSim.Bullet.Remove))]
        static class RemovePatch
        {
            static void Postfix(BulletSim.Bullet __instance)
            {
                RailgunFireOnRelease.ShotKinds.Remove(__instance.GetHashCode());
                _state.Remove(__instance.GetHashCode());
            }
        }
        */
        // --------------------------------------------------------------------
        public class ThinAndDestroy : MonoBehaviour
        {
            public float duration = 5f;
            Vector3 startScale;
            float t0;
            void Start()
            {
                t0 = Time.time;
                startScale = transform.localScale;
            }
            void Update()
            {
                float t = (Time.time - t0) / duration;
                if (t >= 1f) { Destroy(gameObject); return; }
                transform.localScale = new Vector3(
                    Mathf.Lerp(startScale.x, 0, t),
                    Mathf.Lerp(startScale.y, 0, t),
                    startScale.z
                );
            }
        }
    }
}
