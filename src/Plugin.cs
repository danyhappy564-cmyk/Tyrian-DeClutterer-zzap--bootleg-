using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace TYR_DeClutterer
{
    // GUID는 원본과 같게 둔다 → 원본 dll과 같이 깔려도 하나만 로드된다.
    [BepInPlugin("com.TYR.DeClutter", "TYR_DeClutter", "2.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static string PluginDir;

        // 레이드 중이고, 키로 잠시 끄지 않은 상태.
        internal static bool InRaid;
        internal static bool Suspended;
        internal static bool Active => InRaid && !Suspended;
        internal static bool DeclutterActive => Active && Settings.DeclutterEnabled.Value;

        private const float StartDelay = 2f;          // 맵 오브젝트가 다 자리 잡을 시간
        private const float LateWorldRetry = 10f;     // 풀·반사 장치가 늦게 생기는 맵 대비 한 번 더 적용

        private float _raidSeenAt = -1f;
        private float _lateRetryAt = -1f;
        private bool _applyingPreset;

        private void Awake()
        {
            Log = Logger;
            PluginDir = Path.GetDirectoryName(Info.Location);
            Settings.Init(Config);
            Config.SettingChanged += OnSettingChanged;
            Patches.ApplyAll(new Harmony("com.TYR.DeClutter"));
            Log.LogInfo("TYR DeClutterer 2.0.0 loaded");
        }

        private void Update()
        {
            bool worldReady = Singleton<GameWorld>.Instantiated;
            GameWorld gw = worldReady ? Singleton<GameWorld>.Instance : null;

            if (InRaid && (gw == null || gw.MainPlayer == null))
            {
                OnRaidEnd();
                return;
            }

            if (!InRaid)
            {
                // 하이드아웃은 제외(원본은 씬 이름 'bunker_2'로 판정했음).
                if (gw == null || gw.MainPlayer == null || gw is HideoutGameWorld)
                {
                    _raidSeenAt = -1f;
                    return;
                }
                if (_raidSeenAt < 0f) _raidSeenAt = Time.realtimeSinceStartup;
                if (Time.realtimeSinceStartup - _raidSeenAt < StartDelay) return;
                OnRaidStart(gw);
            }

            ClutterScanner.Tick();
            Benchmark.Tick();
            if (Settings.GrassDistance.Value > 0) WorldTweaks.SetScoped(IsScopedAim(gw.MainPlayer));

            if (_lateRetryAt > 0f && Time.realtimeSinceStartup >= _lateRetryAt)
            {
                _lateRetryAt = -1f;
                SafeWorldApply();
            }

            if (Settings.ToggleKey.Value.IsDown() && !Benchmark.Running)
            {
                SetSuspended(!Suspended);
                Notifier.Show(Suspended ? Loc.L("DeClutter: 잠시 끔", "DeClutter: paused") : Loc.L("DeClutter: 다시 켬", "DeClutter: resumed"));
            }
            if (Settings.BenchmarkKey.Value.IsDown()) Benchmark.Start();
            if (Settings.InspectKey.Value.IsDown())
            {
                string msg = ClutterScanner.Inspect();
                Log.LogInfo("[DeClutter] Inspect: " + msg);
                Notifier.Show(msg, 5f);
            }
        }

        private void OnGUI() => Notifier.Draw();

        // 배율 스코프(광학 조준경)로 조준 중인지. PiP 스코프든 PiP-Disabler든 같은 값이다.
        private static bool IsScopedAim(Player player)
        {
            var pwa = player != null ? player.ProceduralWeaponAnimation : null;
            if (pwa == null || !pwa.IsAiming || pwa.ScopeAimTransforms == null || pwa.ScopeAimTransforms.Count == 0) return false;
            var scope = pwa.CurrentScope;
            return scope != null && scope.IsOptic;
        }

        private void OnRaidStart(GameWorld gw)
        {
            InRaid = true;
            Suspended = false;
            string map = gw.LocationId ?? "?";
            ClutterRules.LoadUserExcludes(PluginDir, map);
            GraphicsTweaks.Capture();
            GraphicsTweaks.Apply();
            SafeWorldApply();
            _lateRetryAt = Time.realtimeSinceStartup + LateWorldRetry;
            ClutterScanner.Begin(map);
            if (ClutterRules.UserExcludeCount > 0)
                Log.LogInfo($"[DeClutter] exclude.txt: {ClutterRules.UserExcludeCount} entries for {map}");
        }

        private void OnRaidEnd()
        {
            InRaid = false;
            Suspended = false;
            _raidSeenAt = -1f;
            _lateRetryAt = -1f;
            Benchmark.Cancel();
            ClutterScanner.Clear();
            DecalGate.Reset();
            GraphicsTweaks.EndRaid();
            WorldTweaks.EndRaid();
        }

        private static void SafeWorldApply()
        {
            try { WorldTweaks.ApplyDecals(); } catch (Exception e) { Log.LogWarning("[DeClutter] decals: " + e.Message); }
            try { WorldTweaks.ApplyGrass(); } catch (Exception e) { Log.LogWarning("[DeClutter] grass: " + e.Message); }
            try { WorldTweaks.ApplyReflections(); } catch (Exception e) { Log.LogWarning("[DeClutter] reflections: " + e.Message); }
        }

        // 토글 키·측정에서 쓰는 '잠시 끄기'. 설정 파일은 안 바꾼다. 총알 자국 상한은 레이드 단위라 그대로 둔다.
        internal static void SetSuspended(bool value)
        {
            if (!InRaid || Suspended == value) return;
            Suspended = value;
            if (Suspended) ClutterScanner.RestoreAll();
            else ClutterScanner.MarkDirty(false);
            GraphicsTweaks.Apply();
            try { WorldTweaks.ApplyGrass(); WorldTweaks.ApplyReflections(); } catch (Exception e) { Log.LogWarning("[DeClutter] " + e.Message); }
        }

        private void OnSettingChanged(object sender, SettingChangedEventArgs e)
        {
            var entry = e.ChangedSetting;
            if (entry == Settings.Language) return;

            if (entry == Settings.Preset)
            {
                // 고른 프리셋 이름은 그대로 남긴다. 그 뒤 항목을 직접 바꾸면 '직접 설정'으로 바뀐다(아래).
                if (_applyingPreset || Settings.Preset.Value == Settings.PresetCustom) return;
                _applyingPreset = true;
                try { Settings.ApplyPreset(Settings.Preset.Value); }
                finally { _applyingPreset = false; }
                RefreshAll(true);
                Notifier.Show(Loc.L("프리셋을 적용했습니다: ", "Preset applied: ") + Settings.Preset.Value);
                return;
            }
            if (_applyingPreset) return;

            string section = entry.Definition.Section;
            if ((section == Settings.SecDeclutter || section == Settings.SecFramesaver) && entry != Settings.ScanBudgetMs
                && Settings.Preset.Value != Settings.PresetCustom)
            {
                _applyingPreset = true;
                try { Settings.Preset.Value = Settings.PresetCustom; }
                finally { _applyingPreset = false; }
            }
            if (!InRaid) return;

            // 맵 데칼은 로드 때 정해진다(DecalGate) → 레이드 중 변경은 다음 레이드부터.
            if (entry == Settings.Decals || (entry == Settings.HideUnscrutinized && Settings.Decals.Value))
                Notifier.Show(Loc.L("데칼 설정은 다음 레이드부터 적용됩니다", "Decal changes apply from the next raid"), 4f);

            if (section == Settings.SecDeclutter)
            {
                ClutterScanner.MarkDirty(false);
            }
            else if (section == Settings.SecFramesaver)
            {
                GraphicsTweaks.Apply();
                // '멀리서만 숨김' 거리는 LOD 배율에 따라 계산되므로 다시 맞춘다.
                if (Settings.IsFarMode && (entry == Settings.FramesaverEnabled || entry == Settings.LodBiasDivider))
                    ClutterScanner.MarkDirty(false);
                if (entry == Settings.FramesaverEnabled || entry == Settings.DecalMaxStatic || entry == Settings.DecalMaxDynamic || entry == Settings.DecalCullDistance)
                    SafeWorldApply();
            }
            else if (section == Settings.SecExperimental)
            {
                if (entry == Settings.HideUnscrutinized) ClutterScanner.MarkDirty(true);
                else if (entry == Settings.GrassDistance || entry == Settings.ReflectionDelay) SafeWorldApply();
            }
        }

        private static void RefreshAll(bool reanalyze)
        {
            if (!InRaid) return;
            ClutterScanner.MarkDirty(reanalyze);
            GraphicsTweaks.Apply();
            SafeWorldApply();
        }
    }

    // 화면 위쪽에 잠깐 뜨는 알림.
    internal static class Notifier
    {
        private static string _text;
        private static float _until;
        private static GUIStyle _style;

        public static void Show(string text, float seconds = 3f)
        {
            if (!Settings.Notifications.Value) return;
            _text = text;
            _until = Time.realtimeSinceStartup + seconds;
        }

        public static void Draw()
        {
            if (_text == null || Time.realtimeSinceStartup > _until) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                _style.normal.textColor = Color.white;
            }
            float w = Mathf.Min(900f, Screen.width - 40f);
            GUI.Box(new Rect((Screen.width - w) / 2f, 40f, w, 48f), _text, _style);
        }
    }

    // 효과 측정: 켠 상태 5초 → 끈 상태 5초 평균 FPS. 상태를 바꾼 직후 1.5초는 버린다(끊김 제외).
    internal static class Benchmark
    {
        private enum Step { Idle, SettleOn, MeasureOn, SettleOff, MeasureOff }

        private const float Settle = 1.5f;
        private const float Measure = 5f;

        private static Step _step = Step.Idle;
        private static float _stepStart;
        private static int _frames;
        private static float _time;
        private static float _fpsOn, _fpsOff;
        private static bool _wasSuspended;

        public static bool Running => _step != Step.Idle;

        public static void Start()
        {
            if (Running || !Plugin.InRaid) return;
            _wasSuspended = Plugin.Suspended;
            Plugin.SetSuspended(false);
            Go(Step.SettleOn);
            Notifier.Show(Loc.L("효과 측정 시작 — 약 13초 동안 가만히 있어 주세요", "Benchmark started — stand still for ~13 s"), 4f);
        }

        public static void Cancel()
        {
            _step = Step.Idle;
        }

        public static void Tick()
        {
            if (_step == Step.Idle) return;
            float now = Time.realtimeSinceStartup;
            float elapsed = now - _stepStart;
            switch (_step)
            {
                case Step.SettleOn:
                    if (elapsed >= Settle && !ClutterScanner.Busy) Go(Step.MeasureOn);
                    else if (elapsed > 60f) { Cancel(); Notifier.Show(Loc.L("측정 취소: 검사가 끝나지 않음", "Benchmark cancelled: scan not finished")); }
                    break;
                case Step.MeasureOn:
                    Accumulate();
                    if (elapsed >= Measure)
                    {
                        _fpsOn = _frames / Mathf.Max(0.001f, _time);
                        Plugin.SetSuspended(true);
                        Go(Step.SettleOff);
                    }
                    break;
                case Step.SettleOff:
                    if (elapsed >= Settle) Go(Step.MeasureOff);
                    break;
                case Step.MeasureOff:
                    Accumulate();
                    if (elapsed >= Measure)
                    {
                        _fpsOff = _frames / Mathf.Max(0.001f, _time);
                        Plugin.SetSuspended(_wasSuspended);
                        _step = Step.Idle;
                        Report();
                    }
                    break;
            }
        }

        private static void Go(Step s)
        {
            _step = s;
            _stepStart = Time.realtimeSinceStartup;
            _frames = 0;
            _time = 0f;
        }

        private static void Accumulate()
        {
            _frames++;
            _time += Time.unscaledDeltaTime;
        }

        private static void Report()
        {
            float diff = _fpsOff > 0f ? (_fpsOn - _fpsOff) / _fpsOff * 100f : 0f;
            string msg = Loc.L(
                $"효과 측정: 켬 {_fpsOn:0.0} FPS ({1000f / Mathf.Max(0.1f, _fpsOn):0.0}ms) / 끔 {_fpsOff:0.0} FPS ({1000f / Mathf.Max(0.1f, _fpsOff):0.0}ms) → {diff:+0.0;-0.0}% (숨김 {ClutterScanner.HiddenCount}, 멀리서만 {ClutterScanner.FarCount})",
                $"Benchmark: on {_fpsOn:0.0} FPS ({1000f / Mathf.Max(0.1f, _fpsOn):0.0} ms) / off {_fpsOff:0.0} FPS ({1000f / Mathf.Max(0.1f, _fpsOff):0.0} ms) → {diff:+0.0;-0.0}% (hidden {ClutterScanner.HiddenCount}, far-only {ClutterScanner.FarCount})");
            Plugin.Log.LogInfo("[DeClutter] " + msg);
            Notifier.Show(msg, 8f);
        }
    }
}
