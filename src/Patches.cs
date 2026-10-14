using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.AssetsManager;
using EFT.Weather;
using HarmonyLib;
using UnityEngine;

namespace TYR_DeClutterer
{
    // 패치는 시작할 때 한 번만 건다. 켜고 끄기는 패치 안에서 설정값을 보고 결정한다
    // (원본은 설정을 바꿀 때마다 new XxxPatch().Enable()을 다시 불렀다).
    internal static class Patches
    {
        public static void ApplyAll(Harmony h)
        {
            Try(h, "Shell cap", AccessTools.Method(typeof(GameWorld), nameof(GameWorld.SpawnShellInTheWorld)), postfix: nameof(ShellCapPostfix));
            Try(h, "Physics (update)", AccessTools.Method(typeof(PhysicsExtensions.Simulation), nameof(PhysicsExtensions.Simulation.Update)), prefix: nameof(PhysicsUpdatePrefix));
            Try(h, "Physics (fixed)", AccessTools.Method(typeof(PhysicsExtensions.Simulation), nameof(PhysicsExtensions.Simulation.FixedUpdate)), prefix: nameof(PhysicsFixedUpdatePrefix));
            Try(h, "Sky", AccessTools.Method(typeof(TOD_Sky), "LateUpdate"), prefix: nameof(WeatherPrefix));
            Try(h, "Weather", AccessTools.Method(typeof(WeatherController), "LateUpdate"), prefix: nameof(WeatherPrefix));
            Try(h, "Map decals", AccessTools.Method(typeof(StaticDeferredDecalRenderer), nameof(StaticDeferredDecalRenderer.AddDecal)), prefix: nameof(DecalAddPrefix));
        }

        // ── 맵 데칼: 로드 때 그리기 목록에 안 넣기 (DecalGate 설명 참고) ─────
        private static bool DecalAddPrefix(StaticDeferredDecal decal)
        {
            try { return !DecalGate.ShouldBlock(decal); }
            catch { return true; }
        }

        private static void Try(Harmony h, string name, MethodInfo target, string prefix = null, string postfix = null)
        {
            try
            {
                if (target == null) throw new MissingMethodException("target not found");
                h.Patch(target,
                    prefix: prefix == null ? null : new HarmonyMethod(typeof(Patches), prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(typeof(Patches), postfix));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[DeClutter] patch '{name}' skipped (game version mismatch?): {e.Message}");
            }
        }

        // ── 탄피 개수 제한 ──────────────────────────────────────────
        private static readonly AccessTools.FieldRef<GameWorld, List<AmmoPoolObject>> AmmoShells =
            AccessTools.FieldRefAccess<GameWorld, List<AmmoPoolObject>>("_ammoShells");

        private static void ShellCapPostfix(GameWorld __instance, AmmoPoolObject __result)
        {
            int cap = Settings.ShellCap.Value;
            if (cap <= 0 || !Plugin.Active || !Settings.FramesaverEnabled.Value) return;
            var list = AmmoShells(__instance);
            if (list == null) return;
            // 가장 오래된 것부터 풀로 돌려보낸다. 방금 만든 탄피(__result)는 호출한 쪽이 곧 쓰므로 건드리지 않는다.
            int guard = 64;
            int i = 0;
            while (list.Count > cap && i < list.Count && guard-- > 0)
            {
                var old = list[i];
                if (old == __result) { i++; continue; }
                list.RemoveAt(i);
                if (old != null) AssetPoolObject.ReturnToPool(old.gameObject);
            }
        }

        // ── 물리 절반 (실험적) ──────────────────────────────────────
        // 원본: 두 프레임에 한 번만 같은 시간으로 계산 → 물리가 슬로모션.
        // 여기: 건너뛴 프레임의 시간을 모아 다음 계산에 더한다 → 속도는 그대로, 계산 횟수만 절반.
        private const float MaxStep = 0.1f;
        private static float _accUpdate, _accFixed;
        private static bool _oddFixed;

        private static bool PhysicsUpdatePrefix()
        {
            if (!Plugin.Active || !Settings.PhysicsHalfRate.Value)
            {
                _accUpdate = 0f;
                return true;
            }
            if ((Time.frameCount & 1) == 1)
            {
                _accUpdate = Mathf.Min(_accUpdate + Time.deltaTime, MaxStep);
                return false;
            }
            float dt = Mathf.Min(Time.deltaTime + _accUpdate, MaxStep);
            _accUpdate = 0f;
            switch (PhysicsExtensions.Simulation.UpdateMode)
            {
                case PhysicsExtensions.Simulation.UpdateModeType.Update:
                    PhysicsExtensions.Simulation.Simulate(dt);
                    break;
                case PhysicsExtensions.Simulation.UpdateModeType.QualityControl:
                    PhysicsExtensions.Simulation.QualityControlUpdate(dt);
                    break;
                case PhysicsExtensions.Simulation.UpdateModeType.SmoothSimulate:
                    PhysicsExtensions.Simulation.SmoothSimulate(dt);
                    break;
            }
            return false;
        }

        private static bool PhysicsFixedUpdatePrefix()
        {
            if (!Plugin.Active || !Settings.PhysicsHalfRate.Value || PhysicsExtensions.Simulation.UpdateMode != PhysicsExtensions.Simulation.UpdateModeType.FixedUpdate)
            {
                _accFixed = 0f;
                return true;
            }
            _oddFixed = !_oddFixed;
            if (_oddFixed)
            {
                _accFixed = Mathf.Min(_accFixed + Time.fixedDeltaTime, MaxStep);
                return false;
            }
            float dt = Mathf.Min(Time.fixedDeltaTime + _accFixed, MaxStep);
            _accFixed = 0f;
            PhysicsExtensions.Simulation.Simulate(dt);
            return false;
        }

        // ── 하늘·날씨 격프레임 (실험적) ────────────────────────────
        private static bool WeatherPrefix()
        {
            if (!Plugin.Active || !Settings.WeatherThrottle.Value) return true;
            return (Time.frameCount & 1) == 0;
        }
    }
}
