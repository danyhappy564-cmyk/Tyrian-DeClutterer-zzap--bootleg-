using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Comfort.Common;
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
            Try(h, "Muzzle flash shadows", AccessTools.Method(typeof(MuzzleLight), nameof(MuzzleLight.OnShot)), postfix: nameof(MuzzleShotPostfix));
            Try(h, "Muzzle smoke indoor fade", AccessTools.Method(typeof(Systems.Effects.Effects), nameof(Systems.Effects.Effects.UpdateMBOITIndoorAttenuation), Type.EmptyTypes),
                prefix: nameof(MuzzleSmokePrefix));
        }

        // ── 남의 총구 화염 그림자 끄기 ───────────────────────────────
        // 총구 불빛(Light)에 그림자가 켜져 있으면 쏠 때마다 주변을 그림자 맵에 다시 그린다(점광원은 6번).
        // 쏠 때마다 주인을 확인한다: 총은 풀에서 재사용되어 봇 총이 다음엔 내 총이 될 수 있다.
        // 원래 값은 처음 본 순간(아직 안 바꿨을 때) 저장하고, 끄거나 내 총이면 그 값으로 되돌린다.
        private static readonly ConditionalWeakTable<MuzzleLight, LightShadows[]> MuzzleOriginal = new ConditionalWeakTable<MuzzleLight, LightShadows[]>();
        private static bool _muzzleTouched;

        private static void MuzzleShotPostfix(MuzzleLight __instance)
        {
            try
            {
                Light[] lights = __instance.Lights;
                bool want = Plugin.Active && Settings.FramesaverEnabled.Value && Settings.MuzzleShadowsOff.Value;
                // 한 번도 바꾼 적 없으면 되돌릴 것도 없다.
                if (lights == null || (!want && !_muzzleTouched)) return;
                LightShadows[] original = MuzzleOriginal.GetValue(__instance, m => Array.ConvertAll(m.Lights, l => l != null ? l.shadows : LightShadows.None));
                bool off = want && !IsMyGun(__instance);
                if (off) _muzzleTouched = true;
                for (int i = 0; i < lights.Length && i < original.Length; i++)
                {
                    if (lights[i] == null) continue;
                    LightShadows target = off ? LightShadows.None : original[i];
                    if (lights[i].shadows != target) lights[i].shadows = target;
                }
            }
            catch (Exception e)
            {
                LogOnce(ref _muzzleError, "muzzle shadows", e);
            }
        }

        private static bool IsMyGun(MuzzleLight light)
        {
            var me = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
            // Player.transform는 게임이 막아 둔(obsolete) 속성이라 Component로 읽는다 = 플레이어 오브젝트의 맨 위.
            return me != null && light.transform.IsChildOf(((Component)me).transform);
        }

        // ── 총구 연기 실내 처리 프레임당 1번 ─────────────────────────
        // MuzzleManager마다(= 세상의 총마다) LateUpdate에서 부른다. 처리 대상(MuzzleEffect.CommonSystems)은
        // 모든 총이 공유하므로 한 프레임에 한 번이면 같은 결과이고, 실내 전환이 총 수만큼 빨라지던 것도 바로잡힌다.
        private static int _muzzleSmokeFrame = -1;

        private static bool MuzzleSmokePrefix()
        {
            if (!Plugin.Active || !Settings.FramesaverEnabled.Value || !Settings.MuzzleSmokeOncePerFrame.Value) return true;
            int frame = Time.frameCount;
            if (_muzzleSmokeFrame == frame) return false;
            _muzzleSmokeFrame = frame;
            return true;
        }

        private static bool _muzzleError;

        private static void LogOnce(ref bool logged, string what, Exception e)
        {
            if (logged) return;
            logged = true;
            Plugin.Log.LogWarning($"[DeClutter] {what} failed (logged once): {e}");
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
