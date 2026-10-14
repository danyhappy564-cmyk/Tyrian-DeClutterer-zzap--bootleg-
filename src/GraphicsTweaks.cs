using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using DeferredDecals;
using GPUInstancer;
using HarmonyLib;
using UnityEngine;

namespace TYR_DeClutterer
{
    // QualitySettings 변경. 원래 값은 "레이드 시작 때" 저장하고 레이드가 끝나면 되돌린다
    // (원본은 플러그인 로드 시점 값을 저장해서, 그 뒤 게임 설정을 바꾸면 옛 값으로 되돌리는 문제가 있었음).
    internal static class GraphicsTweaks
    {
        private static bool _captured;
        private static bool _softParticles, _softVegetation, _realtimeReflections;
        private static int _particleBudget, _pixelLights, _cascades, _textureLimit;
        private static ShadowQuality _shadows;
        private static float _lodBias;

        public static void Capture()
        {
            if (_captured) return;
            _softParticles = QualitySettings.softParticles;
            _particleBudget = QualitySettings.particleRaycastBudget;
            _softVegetation = QualitySettings.softVegetation;
            _realtimeReflections = QualitySettings.realtimeReflectionProbes;
            _pixelLights = QualitySettings.pixelLightCount;
            _shadows = QualitySettings.shadows;
            _cascades = QualitySettings.shadowCascades;
#pragma warning disable CS0618
            _textureLimit = QualitySettings.masterTextureLimit;
#pragma warning restore CS0618
            _lodBias = QualitySettings.lodBias;
            _captured = true;
        }

        public static void Apply()
        {
            if (!_captured) return;
            if (!Plugin.Active || !Settings.FramesaverEnabled.Value) { Restore(); return; }

            QualitySettings.softParticles = Settings.SoftParticlesOff.Value ? false : _softParticles;
            int div = Settings.ParticleBudgetDivider.Value;
            QualitySettings.particleRaycastBudget = div > 1 ? Mathf.Max(4, _particleBudget / div) : _particleBudget;
            QualitySettings.softVegetation = Settings.SoftVegetationOff.Value ? false : _softVegetation;
            QualitySettings.realtimeReflectionProbes = Settings.RealtimeReflectionsOff.Value ? false : _realtimeReflections;
            QualitySettings.shadows = Settings.HardShadowsOnly.Value && _shadows == ShadowQuality.All ? ShadowQuality.HardOnly : _shadows;
            QualitySettings.shadowCascades = CascadeValue();
            // 원본은 조명 개수 값이 그림자 분할 값(>1)일 때만 적용되는 버그가 있었음 → 각각 독립 적용.
            QualitySettings.pixelLightCount = Mathf.Max(0, _pixelLights / Mathf.Max(1, Settings.PixelLightDivider.Value));
#pragma warning disable CS0618
            QualitySettings.masterTextureLimit = Mathf.Max(_textureLimit, Settings.TextureReduction.Value);
#pragma warning restore CS0618
            QualitySettings.lodBias = _lodBias / Mathf.Max(1f, Settings.LodBiasDivider.Value);
        }

        private static int CascadeValue()
        {
            string v = Settings.ShadowCascades.Value;
            if (v == Settings.CascadeTwo) return Mathf.Min(_cascades, 2);
            if (v == Settings.CascadeNone) return 0;
            return _cascades;
        }

        public static void Restore()
        {
            if (!_captured) return;
            QualitySettings.softParticles = _softParticles;
            QualitySettings.particleRaycastBudget = _particleBudget;
            QualitySettings.softVegetation = _softVegetation;
            QualitySettings.realtimeReflectionProbes = _realtimeReflections;
            QualitySettings.pixelLightCount = _pixelLights;
            QualitySettings.shadows = _shadows;
            QualitySettings.shadowCascades = _cascades;
#pragma warning disable CS0618
            QualitySettings.masterTextureLimit = _textureLimit;
#pragma warning restore CS0618
            QualitySettings.lodBias = _lodBias;
        }

        public static void EndRaid()
        {
            Restore();
            _captured = false;
        }
    }

    // 레이드(맵) 단위 설정: 총알 자국 상한, 풀 거리, 주변 반사 갱신 간격.
    internal static class WorldTweaks
    {
        private static readonly FieldInfo MaxDecals = AccessTools.Field(typeof(DeferredDecalRenderer), "_maxDecals");
        private static readonly FieldInfo MaxDynamicDecals = AccessTools.Field(typeof(DeferredDecalRenderer), "_maxDynamicDecals");
        private static readonly FieldInfo CullDistance = AccessTools.Field(typeof(DeferredDecalRenderer), "_cullDistance");
        private static readonly FieldInfo DecalIndex = AccessTools.Field(typeof(DeferredDecalRenderer), "_currentDecalIndex");
        private static readonly FieldInfo DynamicDecalIndex = AccessTools.Field(typeof(DeferredDecalRenderer), "_currentDynamicDecalIndex");

        private static DeferredDecalRenderer _decals;
        private static int _origStatic = -1, _origDynamic = -1, _origCull = -1;

        // 풀 프로토타입은 맵 오브젝트가 아니라 에셋(ScriptableObject)이라 다음 레이드까지 남는다 → 원래 값을 꼭 되돌린다.
        private static readonly Dictionary<GPUInstancerPrototype, float> GrassOrig = new Dictionary<GPUInstancerPrototype, float>();
        private static readonly Dictionary<AmbientLight, float> ReflectionOrig = new Dictionary<AmbientLight, float>();

        public static void ApplyDecals()
        {
            if (_decals == null)
            {
                if (!Singleton<Systems.Effects.Effects>.Instantiated) return;
                _decals = Singleton<Systems.Effects.Effects>.Instance.DeferredDecals;
                if (_decals == null || MaxDecals == null || MaxDynamicDecals == null || CullDistance == null) { _decals = null; return; }
                _origStatic = (int)MaxDecals.GetValue(_decals);
                _origDynamic = (int)MaxDynamicDecals.GetValue(_decals);
                _origCull = (int)CullDistance.GetValue(_decals);
            }

            // 줄이는 쪽으로만 바꾼다(게임이 처음 만든 배열보다 커지면 안 됨). 바꾼 뒤 내부 순번을 0으로.
            int wantStatic = Settings.DecalMaxStatic.Value > 0 ? Mathf.Min(_origStatic, Settings.DecalMaxStatic.Value) : _origStatic;
            int wantDynamic = Settings.DecalMaxDynamic.Value > 0 ? Mathf.Min(_origDynamic, Settings.DecalMaxDynamic.Value) : _origDynamic;
            int wantCull = Settings.DecalCullDistance.Value > 0 ? Mathf.Min(_origCull, Settings.DecalCullDistance.Value) : _origCull;
            if (!Settings.FramesaverEnabled.Value) { wantStatic = _origStatic; wantDynamic = _origDynamic; wantCull = _origCull; }

            try
            {
                if ((int)MaxDecals.GetValue(_decals) != wantStatic)
                {
                    _decals.SetMaxStaticDecals(wantStatic);
                    DecalIndex?.SetValue(_decals, 0);
                }
                if ((int)MaxDynamicDecals.GetValue(_decals) != wantDynamic)
                {
                    _decals.SetMaxDynamicDecals(wantDynamic);
                    DynamicDecalIndex?.SetValue(_decals, 0);
                }
                CullDistance.SetValue(_decals, wantCull);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("[DeClutter] decal limit failed: " + e.Message);
            }
        }

        // 풀(GPUInstancer detail)은 스코프 카메라도 같은 maxDistance를 쓴다(maxDistanceOptic은 terrainSettings가 없을 때만 쓰임).
        // 그래서 배율 스코프로 조준하는 동안에는 원래 거리로 되돌린다 → 스코프 화면은 바뀌지 않는다.
        private static bool _scoped;

        public static void SetScoped(bool scoped)
        {
            if (_scoped == scoped) return;
            _scoped = scoped;
            if (GrassOrig.Count > 0) ApplyGrass();
        }

        public static void ApplyGrass()
        {
            int dist = Settings.GrassDistance.Value;
            var managers = GPUInstancerManager.activeManagerList;
            if (managers == null) return;
            foreach (var m in managers)
            {
                if (!(m is GPUInstancerDetailManager) || m.prototypeList == null) continue;
                foreach (var p in m.prototypeList)
                {
                    if (p == null) continue;
                    if (!GrassOrig.TryGetValue(p, out float orig)) { orig = p.maxDistance; GrassOrig[p] = orig; }
                    p.maxDistance = (Plugin.Active && dist > 0 && !_scoped) ? Mathf.Min(orig, dist) : orig;
                }
            }
        }

        public static void ApplyReflections()
        {
            float delay = Settings.ReflectionDelay.Value;
            if (ReflectionOrig.Count == 0)
            {
                foreach (var a in Object.FindObjectsOfType<AmbientLight>()) ReflectionOrig[a] = a.RenderDelay;
            }
            foreach (var kv in ReflectionOrig)
            {
                if (kv.Key == null) continue;
                kv.Key.RenderDelay = (Plugin.Active && delay > 0f) ? Mathf.Max(kv.Value, delay) : kv.Value;
            }
        }

        public static void EndRaid()
        {
            foreach (var kv in GrassOrig) if (kv.Key != null) kv.Key.maxDistance = kv.Value;
            GrassOrig.Clear();
            ReflectionOrig.Clear();
            _scoped = false;
            _decals = null;
            _origStatic = _origDynamic = _origCull = -1;
        }
    }
}
