using System.Collections.Generic;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace TYR_DeClutterer
{
    // 맵 데칼(낙서·얼룩·핏자국)은 끄지 않고, 맵이 로드될 때 '그리기 목록에 안 넣는' 방식으로 숨긴다.
    // 이유: 게임은 데칼 하나를 끌 때마다(StaticDeferredDecalRenderer.RemoveDecal) 문자열 키를 새로 만들고
    // 같은 종류 데칼 전체의 범위를 다시 계산하며, 다음 프레임에 그리기 버퍼를 통째로 다시 만든다.
    // 수천 개를 레이드 중에 끄면 그동안 매 프레임 수백 KB씩 쓰레기가 생겼다(2.0.0 세관 실측: 6,226개, 분당 764MB).
    // 그래서 데칼은 '레이드 단위' 설정이다 — 레이드 중 바꾸면 다음 레이드부터 적용, 토글 키·측정에서도 그대로.
    internal static class DecalGate
    {
        private static readonly HashSet<int> Blocked = new HashSet<int>();
        private static string _excludeMap;
        private static bool _excludeLoaded;

        public static int BlockedCount => Blocked.Count;

        public static bool IsBlocked(StaticDeferredDecal d) => d != null && Blocked.Contains(d.GetInstanceID());

        public static void Reset()
        {
            Blocked.Clear();
            _excludeLoaded = false;
        }

        // StaticDeferredDecalRenderer.AddDecal 프리픽스에서 부른다(맵 로드 중, 데칼마다 1회).
        public static bool ShouldBlock(StaticDeferredDecal decal)
        {
            if (decal == null || !Settings.DeclutterEnabled.Value || !Settings.Decals.Value) return false;
            if (Singleton<GameWorld>.Instantiated && Singleton<GameWorld>.Instance is HideoutGameWorld) return false;
            EnsureExcludes();
            string lower = decal.gameObject.name.ToLowerInvariant();
            if (!Settings.HideUnscrutinized.Value && ClutterRules.Match(lower, out _) == Cat.None) return false;
            if (ClutterRules.IsNeverName(lower, out _) || ClutterRules.IsUserExcluded(lower, out _)) return false;
            Blocked.Add(decal.GetInstanceID());
            return true;
        }

        // 맵 ID를 알 수 있으면 그 맵용 제외 목록까지, 아니면 공통 줄만.
        private static void EnsureExcludes()
        {
            string map = null;
            if (Singleton<GameWorld>.Instantiated) map = Singleton<GameWorld>.Instance.LocationId;
            if (_excludeLoaded && map == _excludeMap) return;
            ClutterRules.LoadUserExcludes(Plugin.PluginDir, map);
            _excludeMap = map;
            _excludeLoaded = true;
        }
    }
}
