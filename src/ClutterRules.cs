using System;
using System.Collections.Generic;
using System.IO;
using EFT;
using EFT.AssetsManager;
using EFT.Ballistics;
using EFT.Game.Spawning;
using EFT.Interactive;
using Koenigz.PerfectCulling.EFT;
using UnityEngine;

namespace TYR_DeClutterer
{
    [Flags]
    internal enum Cat
    {
        None = 0,
        Garbage = 1 << 0,
        Heaps = 1 << 1,
        Cartridges = 1 << 2,
        FakeFood = 1 << 3,
        Decals = 1 << 4,
        Puddles = 1 << 5,
        Shards = 1 << 6,
        FireSmoke = 1 << 7,
    }

    // 무엇을 잡동사니로 볼지(이름 조각), 무엇은 절대 안 숨길지(이름·컴포넌트·사용자 제외 목록).
    internal static class ClutterRules
    {
        // 원본 v1.1.5 이름 목록 그대로 + 불·연기(새 항목).
        private static readonly (Cat cat, string[] keys)[] Keywords =
        {
            (Cat.Garbage, new[] { "turniket_", "tray_", "electronic_box", "styrofoam_", "polyethylene_set", "penyok_", "kaska", "boot_", "garbage_stone", "garbage_paper", "cable", "drawing_", "paper_", "_paper", "paper1", "paper2", "paper3", "paper4", "paper5", "paper6", "paper7", "paper8", "paper9", "pan1", "pan2", "pan3", "pan4", "pan5", "pan6", "pan7", "pan8", "pan9", "poster1", "poster2", "poster3", "poster4", "poster5", "poster6", "poster7", "poster8", "poster9", "_junk", "junk_", "_trash", "trash_", "cardboard_", "_cardboard", "sticks", "cloth_", "pants_", "shirt_", "dishes_", "cutlery_", "book_", "books_", "folder_", "folders_", "magazine_", "magazines_", "fuel_tube", "city_garbage_", "city_road_garbage", "reserve_garbage_", "reserve_road_garbage", "garbage_parking_", "goshan_garbage", "package_garbage", "wood_board", "leaves_" }),
            (Cat.Heaps, new[] { "trash_pile_", "_trash_pile", "crushed_concrete", "crushed_concreate", "baked_garbage", "garbage", "garbage_", "_garbage", "garbage_constructor", "_garb", "garb_", "_scrap", "scrap_", "heap_", "_heap", "_pile", "pile_", "_stuff", "_rubble", "rubble_", "scatter_", "_scatter", "scattered_", "_scattered", "_floorset", "floorset_", "brick_pile", "poletelen01", "poletelen02", "poletelen03", "poletelen04", "poletelen05", "poletelen06", "poletelen07", "poletelen08", "poletelen09", "vetky1", "vetky2", "vetky1_", "vetky2_", "vetky3_", "vetky4_", "vetky5_", "vetky6_", "tile_broken_" }),
            (Cat.Cartridges, new[] { "shotshell_", "shells_", "_shotshell", "_shells", "rifleshell_", "_rifleshell", "rifle_shells_" }),
            (Cat.FakeFood, new[] { "canned", "canned_", "can_", "juice_", "carton_", "_creased", "bottle", "bottle_", "crackers_", "oat_flakes", "chocolate_", "biscuits", "package_", "cigarette_", "medkit_", "_cup", "plasticcup_" }),
            (Cat.Decals, new[] { "goshan_decal", "ground_decal", "decalgraffiti", "blood_", "_blood", "sand_decal", "decal_dirt", "decal_drip", "decal_", "decals_" }),
            (Cat.Puddles, new[] { "puddle", "puddles_", "_puddles", "puddle group" }),
            (Cat.Shards, new[] { "_glass", "brokenglass_", "glass_crush", "plite_crush", "lesa_crush", "shards_", "_shards" }),
            (Cat.FireSmoke, new[] { "fire", "smoke", "steam", "dust" }),
        };

        // 이름에 이게 들어가면 어떤 경우에도 안 숨김(원본 목록 그대로).
        private static readonly string[] NeverNames =
        {
            "item_", "weapon_", "barter_", "mod_", "audio", "container", "trigger", "culling", "collider", "colider",
            "group", "manager", "scene", "player", "portal", "bakelod", "door", "shadow", "mine",
        };

        private static readonly List<string> UserExcludes = new List<string>();

        public static Cat Match(string lowerName, out string firstKeyword)
        {
            Cat mask = Cat.None;
            firstKeyword = null;
            foreach (var (cat, keys) in Keywords)
            {
                foreach (var k in keys)
                {
                    if (lowerName.IndexOf(k, StringComparison.Ordinal) >= 0)
                    {
                        mask |= cat;
                        if (firstKeyword == null) firstKeyword = k;
                        break;
                    }
                }
            }
            return mask;
        }

        public static bool IsNeverName(string lowerName, out string hit)
        {
            foreach (var n in NeverNames)
            {
                if (lowerName.IndexOf(n, StringComparison.Ordinal) >= 0) { hit = n; return true; }
            }
            hit = null;
            return false;
        }

        public static bool IsUserExcluded(string lowerName, out string hit)
        {
            foreach (var n in UserExcludes)
            {
                if (lowerName.IndexOf(n, StringComparison.Ordinal) >= 0) { hit = n; return true; }
            }
            hit = null;
            return false;
        }

        public static Cat EnabledMask()
        {
            Cat m = Cat.None;
            if (Settings.Garbage.Value) m |= Cat.Garbage;
            if (Settings.Heaps.Value) m |= Cat.Heaps;
            if (Settings.Cartridges.Value) m |= Cat.Cartridges;
            if (Settings.FakeFood.Value) m |= Cat.FakeFood;
            if (Settings.Decals.Value) m |= Cat.Decals;
            if (Settings.Puddles.Value) m |= Cat.Puddles;
            if (Settings.Shards.Value) m |= Cat.Shards;
            if (Settings.FireSmoke.Value) m |= Cat.FireSmoke;
            return m;
        }

        public static string CatName(Cat c)
        {
            switch (c)
            {
                case Cat.Garbage: return Loc.L("쓰레기", "garbage");
                case Cat.Heaps: return Loc.L("더미", "heaps");
                case Cat.Cartridges: return Loc.L("탄피", "cartridges");
                case Cat.FakeFood: return Loc.L("가짜음식", "fake food");
                case Cat.Decals: return Loc.L("데칼", "decals");
                case Cat.Puddles: return Loc.L("웅덩이", "puddles");
                case Cat.Shards: return Loc.L("파편", "shards");
                case Cat.FireSmoke: return Loc.L("불연기", "fire/smoke");
                default: return Loc.L("기타", "other");
            }
        }

        // exclude.txt: "이름조각" 또는 "맵ID:이름조각", # 주석. 레이드 시작마다 다시 읽는다.
        public static void LoadUserExcludes(string pluginDir, string mapId)
        {
            UserExcludes.Clear();
            string path = Path.Combine(pluginDir, "exclude.txt");
            if (!File.Exists(path)) return;
            string map = (mapId ?? "").ToLowerInvariant();
            try
            {
                foreach (var raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int colon = line.IndexOf(':');
                    if (colon > 0)
                    {
                        if (line.Substring(0, colon).Trim().ToLowerInvariant() != map) continue;
                        line = line.Substring(colon + 1).Trim();
                    }
                    if (line.Length > 0) UserExcludes.Add(line.ToLowerInvariant());
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("exclude.txt read failed: " + e.Message);
            }
        }

        public static int UserExcludeCount => UserExcludes.Count;

        // 이 컴포넌트가 하위에 있으면 숨기지 않는다: 루팅·퀘스트·상호작용·봇·컬링·소리·조명 등.
        public static bool IsProtectedComponent(Component c)
        {
            // AssetPoolObject = 게임·HollywoodFX의 오브젝트 풀(탄피·무기·아이템), BorderZone·SpawnPointMarker = ORBIT 등이 레이드 시작 때 찾는 것.
            return c is LootableContainer || c is LootableContainersGroup || c is LootItem || c is AssetPoolObject
                || c is BorderZone || c is SpawnPointMarker
                || c is RainCondensator || c is Player || c is BotOwner
                || c is CullingObject || c is CullingLightObject || c is DisablerCullingObject || c is ObservedCullingManager
                || c is PerfectCullingCrossSceneGroup || c is ScreenDistanceSwitcher || c is BakedLodContent || c is MultisceneSharedOccluder
                || c is GuidComponent || c is OcclusionPortal || c is WindowBreaker || c is BallisticCollider
                || c is WorldInteractiveObject || c is ExfiltrationPoint || c is TriggerWithId || c is DamageTrigger || c is StationaryWeapon
                || c is AudioSource || c is Light;
        }

        // 부모 쪽에 있으면 안 숨기는 것(상호작용 물체의 일부일 수 있음).
        public static bool IsProtectedParent(Component c)
        {
            // Systems.Effects.Effects 아래 = 게임 기본 이펙트 + HollywoodFX가 미리 만들어 붙여 둔 이펙트 풀.
            return c is WorldInteractiveObject || c is LootItem || c is LootableContainer || c is Player || c is AssetPoolObject
                || c is TriggerWithId || c is ExfiltrationPoint || c is DamageTrigger || c is StationaryWeapon
                || c is Systems.Effects.Effects;
        }
    }
}
