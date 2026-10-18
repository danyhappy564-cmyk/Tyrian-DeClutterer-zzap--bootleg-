using System.Collections.Generic;
using BepInEx.Configuration;

namespace TYR_DeClutterer
{
    // BepInEx ConfigurationManager(F12 창)가 이름으로 찾아 읽는 표시용 속성.
    internal sealed class ConfigurationManagerAttributes
    {
        public int? Order;
        public string DispName;
        public string Category;
        public string Description;
        public bool? IsAdvanced;
    }

    internal static class Loc
    {
        public static bool Ko = true;
        public static string L(string ko, string en) => Ko ? ko : en;
    }

    // F12 설정. .cfg의 섹션·키는 영어로 고정하고, 화면 표시(이름·설명·분류)만 언어에 따라 바꾼다.
    internal static class Settings
    {
        public const string SecGeneral = "1. General";
        public const string SecDeclutter = "2. DeClutter";
        public const string SecFramesaver = "3. Framesaver";
        public const string SecExperimental = "4. Experimental";

        private static readonly Dictionary<string, string[]> SectionNames = new Dictionary<string, string[]>
        {
            { SecGeneral, new[] { "1. 일반", "1. General" } },
            { SecDeclutter, new[] { "2. 잡동사니 숨기기", "2. DeClutter" } },
            { SecFramesaver, new[] { "3. 그래픽 가볍게 (Framesaver)", "3. Framesaver" } },
            { SecExperimental, new[] { "4. 실험적 (기본 꺼짐)", "4. Experimental (off by default)" } },
        };

        private sealed class Text
        {
            public string Section, KoName, KoDesc, EnName, EnDesc;
            public ConfigurationManagerAttributes Attr;
        }

        private static readonly List<Text> Texts = new List<Text>();
        private static int _order = 10000;

        // 선택지 값은 .cfg에 저장되므로 두 언어를 한 값에 넣는다(언어를 바꿔도 값이 안 깨지게).
        public const string LangKo = "한국어 · Korean";
        public const string LangEn = "English";

        public const string PresetCustom = "직접 설정 · Custom";
        public const string PresetOff = "모두 끔 · All off";
        public const string PresetLight = "가벼움 · Light";
        public const string PresetBalanced = "균형 · Balanced";
        public const string PresetMax = "최대 · Max";

        public const string ModeHide = "완전히 숨김 · Hide completely";
        public const string ModeFar = "멀리서만 숨김 · Hide at distance only";

        public const string CascadeDefault = "게임 설정 그대로 · Game default";
        public const string CascadeTwo = "2단계 · Two cascades";
        public const string CascadeNone = "없음 · No cascades";

        // 1. General
        public static ConfigEntry<string> Language;
        public static ConfigEntry<string> Preset;
        public static ConfigEntry<KeyboardShortcut> ToggleKey;
        public static ConfigEntry<KeyboardShortcut> BenchmarkKey;
        public static ConfigEntry<KeyboardShortcut> InspectKey;
        public static ConfigEntry<bool> Notifications;
        public static ConfigEntry<bool> DebugLog;

        // 2. DeClutter
        public static ConfigEntry<bool> DeclutterEnabled;
        public static ConfigEntry<string> Mode;
        public static ConfigEntry<float> FarDistance;
        public static ConfigEntry<float> SizeScale;
        public static ConfigEntry<bool> Garbage;
        public static ConfigEntry<bool> Heaps;
        public static ConfigEntry<bool> Cartridges;
        public static ConfigEntry<bool> FakeFood;
        public static ConfigEntry<bool> Decals;
        public static ConfigEntry<bool> Puddles;
        public static ConfigEntry<bool> Shards;
        public static ConfigEntry<bool> FireSmoke;
        public static ConfigEntry<float> ScanBudgetMs;

        // 3. Framesaver
        public static ConfigEntry<bool> FramesaverEnabled;
        public static ConfigEntry<bool> SoftParticlesOff;
        public static ConfigEntry<int> ParticleBudgetDivider;
        public static ConfigEntry<bool> SoftVegetationOff;
        public static ConfigEntry<bool> RealtimeReflectionsOff;
        public static ConfigEntry<bool> HardShadowsOnly;
        public static ConfigEntry<string> ShadowCascades;
        public static ConfigEntry<int> PixelLightDivider;
        public static ConfigEntry<int> TextureReduction;
        public static ConfigEntry<float> LodBiasDivider;
        public static ConfigEntry<int> ShellCap;
        public static ConfigEntry<int> DecalMaxStatic;
        public static ConfigEntry<int> DecalMaxDynamic;
        public static ConfigEntry<int> DecalCullDistance;
        public static ConfigEntry<bool> MuzzleShadowsOff;
        public static ConfigEntry<bool> MuzzleSmokeOncePerFrame;

        // 4. Experimental
        public static ConfigEntry<bool> PhysicsHalfRate;
        public static ConfigEntry<bool> WeatherThrottle;
        public static ConfigEntry<float> ReflectionDelay;
        public static ConfigEntry<int> GrassDistance;
        public static ConfigEntry<bool> HideUnscrutinized;
        public static ConfigEntry<bool> SortByMaterial;

        public static bool IsFarMode => Mode.Value == ModeFar;

        public static void Init(ConfigFile cfg)
        {
            // ── 1. 일반 ─────────────────────────────────────────────
            Language = Bind(cfg, SecGeneral, "Language", LangKo,
                "언어", "F12 화면 언어. 바꾼 뒤 F12 창을 닫았다 다시 열면 적용됩니다.",
                "Language", "Language of this F12 page. Close and reopen the F12 window to apply.",
                new AcceptableValueList<string>(LangKo, LangEn));
            Preset = Bind(cfg, SecGeneral, "Preset", PresetCustom,
                "프리셋 (한 번에 설정)", "고르면 아래 2·3번 항목을 한꺼번에 맞춥니다. 그 뒤 2·3번 항목을 직접 바꾸면 '직접 설정'으로 바뀝니다. 4번(실험적)은 건드리지 않습니다.\n" +
                "가벼움: 잡동사니를 멀리서만 숨김 + 탄피 개수 제한 + 총구 연기 처리 1번\n균형: 잡동사니 완전히 숨김 + 부드러운 파티클·풀·실시간 반사 끔 + 총알 자국 줄임 + 남의 총구 화염 그림자 끔\n최대: 균형 + 그림자 단순화·조명 수·LOD 낮춤",
                "Preset", "Picking one sets sections 2 and 3 at once. Changing any item in sections 2/3 afterwards switches this to 'Custom'. Section 4 (experimental) is never touched.\n" +
                "Light: hide clutter only at distance + shell cap + muzzle smoke update once per frame\nBalanced: hide clutter completely + soft particles/vegetation/realtime reflections off + fewer bullet decals + no shadows from others' muzzle flashes\nMax: Balanced + simpler shadows, fewer pixel lights, lower LOD",
                new AcceptableValueList<string>(PresetCustom, PresetOff, PresetLight, PresetBalanced, PresetMax));
            ToggleKey = Bind(cfg, SecGeneral, "Toggle Key", KeyboardShortcut.Empty,
                "레이드 중 켜기/끄기 키", "누를 때마다 이 모드 전체를 잠시 끄고 켭니다(설정 파일은 안 바뀜). 같은 자리에서 FPS를 비교할 때 쓰세요. 기본은 키 없음.",
                "In-raid on/off key", "Temporarily turns the whole mod off/on (does not change the config). Handy for comparing FPS on the spot. No key by default.");
            BenchmarkKey = Bind(cfg, SecGeneral, "Benchmark Key", KeyboardShortcut.Empty,
                "효과 측정 키", "누르면 약 15~20초 동안 켠 상태 5초 → 끈 상태 5초 평균 FPS를 재서 화면과 로그에 알려 줍니다. 측정 중엔 가만히 서 있으세요. FPS 제한(프레임 상한·수직동기·프레임 생성용 상한)이 걸려 있으면 차이가 안 보이니 측정할 때만 풀어 주세요. 맵 데칼은 양쪽 다 숨긴 채로 잽니다.",
                "Benchmark key", "Measures average FPS for 5 s with the mod on and 5 s with it off (~15-20 s total), then shows the result on screen and in the log. Stand still while it runs. A frame cap (FPS limit, V-Sync, frame-generation cap) hides any difference — lift it while measuring. Map decals stay hidden in both runs.");
            InspectKey = Bind(cfg, SecGeneral, "Inspect Key", KeyboardShortcut.Empty,
                "물체 이름 확인 키", "화면 가운데에 가장 가까운 장식 물체의 이름, 숨겨졌는지, 왜 그런지를 로그에 남깁니다. 숨기면 안 되는 물체를 exclude.txt에 넣을 때 쓰세요.",
                "Inspect key", "Logs the decoration object closest to the screen centre: its name, whether it is hidden and why. Use it to add entries to exclude.txt.");
            Notifications = Bind(cfg, SecGeneral, "On-screen Notices", true,
                "화면 알림", "켜기/끄기·측정 결과 같은 짧은 알림을 화면 위쪽에 2~4초 띄웁니다.",
                "On-screen notices", "Shows short notices (toggle, benchmark result) at the top of the screen for a few seconds.");
            DebugLog = Bind(cfg, SecGeneral, "Debug Log", false,
                "자세한 로그", "숨긴 물체를 하나하나 로그에 남깁니다. 로그가 커지니 문제를 찾을 때만 켜세요.",
                "Debug log", "Logs every hidden object. Makes the log large — only for troubleshooting.", advanced: true);

            // ── 2. 잡동사니 숨기기 ───────────────────────────────────
            DeclutterEnabled = Bind(cfg, SecDeclutter, "Enabled", true,
                "잡동사니 숨기기 켜기", "바닥의 쓰레기·종이·캔 같은 작은 장식 물체를 숨겨 FPS를 올립니다. 충돌체(총알·발에 걸리는 부분)가 있는 물체, 루팅·퀘스트·문·스위치 같은 상호작용 물체는 숨기지 않습니다.",
                "Enable DeClutter", "Hides small decorative objects (litter, paper, cans…) for FPS. Never hides objects with colliders (anything bullets or feet can hit) or interactive/loot/quest objects.");
            Mode = Bind(cfg, SecDeclutter, "Mode", ModeHide,
                "숨기는 방식", "완전히 숨김: 원래 모드와 같음(가장 가벼움).\n멀리서만 숨김: 가까이에선 그대로 보이고 아래 거리보다 멀면 사라집니다(분위기 유지). LOD가 없는 데칼·파티클은 완전히 숨깁니다.",
                "Mode", "Hide completely: same as the original mod (lightest).\nHide at distance only: stays visible up close and disappears beyond the distance below. Decals/particles without LOD are hidden completely.",
                new AcceptableValueList<string>(ModeHide, ModeFar));
            FarDistance = Bind(cfg, SecDeclutter, "Far Distance", 25f,
                "멀리서만 숨김 거리 (m)", "'멀리서만 숨김'일 때 이 거리보다 먼 잡동사니를 숨깁니다(대략값 — 물체 크기와 시야각에 따라 조금 달라짐). 배율 스코프로 보면 더 멀리까지 보이는 게 정상입니다.",
                "Far distance (m)", "In 'hide at distance' mode, clutter farther than this is hidden (approximate — depends on object size and FOV). Magnified scopes naturally show it farther away.",
                new AcceptableValueRange<float>(5f, 100f));
            SizeScale = Bind(cfg, SecDeclutter, "Size Scale", 1f,
                "숨길 크기 배율", "기본 1 = 높이 2m 이하만 숨김. 크게 하면 더 큰 물체도 숨깁니다(원본의 Scaler).",
                "Size scale", "1 = only objects up to 2 m tall are hidden. Larger values hide bigger objects (the original 'Scaler').",
                new AcceptableValueRange<float>(0.5f, 2f));
            Garbage = Bind(cfg, SecDeclutter, "Garbage", true,
                "쓰레기·종이·작은 잡동사니", "작은 쓰레기, 종이, 포스터, 박스 조각, 옷가지, 책 등.", "Garbage & litter", "Small litter, paper, posters, cardboard, clothes, books, etc.");
            Heaps = Bind(cfg, SecDeclutter, "Heaps", true,
                "쓰레기 더미·잔해", "큰 쓰레기 더미, 콘크리트 잔해, 흩어진 파편, 나뭇가지 더미.", "Heaps & piles", "Larger garbage piles, rubble, scattered debris, branch piles.");
            Cartridges = Bind(cfg, SecDeclutter, "Spent Cartridges", true,
                "맵에 깔린 탄피", "맵 제작 때 미리 놓인 탄피(사격으로 생기는 탄피는 3번 '탄피 최대 개수').", "Pre-placed spent cartridges", "Shell casings baked into the map (casings from shooting: see 'Shell cap' in section 3).");
            FakeFood = Bind(cfg, SecDeclutter, "Fake Food", true,
                "가짜 음식·병·캔", "주울 수 없는 장식용 캔, 병, 과자, 담배, 컵.", "Fake food", "Non-lootable decorative cans, bottles, snacks, cigarettes, cups.");
            Decals = Bind(cfg, SecDeclutter, "Decals", true,
                "맵 데칼 (핏자국·낙서·얼룩)", "맵에 미리 그려진 핏자국, 낙서, 흙 얼룩. 맵을 불러올 때 그리기에서 빼는 방식이라 레이드 중에 바꾸면 다음 레이드부터 적용되고, 켜기/끄기 키·효과 측정에도 영향받지 않습니다.", "Map decals", "Pre-placed blood, graffiti and dirt decals. They are excluded from drawing while the map loads, so changes apply from the next raid and the toggle/benchmark keys do not affect them.");
            Puddles = Bind(cfg, SecDeclutter, "Puddles", true,
                "가짜 물웅덩이", "반사만 하는 장식용 웅덩이(소리 나는 웅덩이는 충돌체가 있어 안 숨김).", "Puddles", "Decorative reflective puddles (puddles with a sound trigger have colliders and are kept).");
            Shards = Bind(cfg, SecDeclutter, "Shards", true,
                "유리·타일 파편", "깨진 유리·타일 조각 중 장식용만. 밟으면 소리 나는 파편은 충돌체가 있어 숨기지 않습니다(봇이 듣는 소리 유지).",
                "Glass & tile shards", "Decorative glass/tile shards only. Shards that make noise when stepped on have colliders and are kept (bots still hear them).");
            FireSmoke = Bind(cfg, SecDeclutter, "Fire And Smoke", false,
                "맵 불·연기·먼지 효과", "맵에 깔린 불, 연기, 김, 먼지 파티클을 숨깁니다. 불 데미지 구역이 붙은 불은 숨기지 않습니다(안 보이는 불에 데는 일 방지).",
                "Map fire, smoke & dust", "Hides map-placed fire, smoke, steam and dust particles. Fires with a damage zone are kept (no invisible burning).");
            ScanBudgetMs = Bind(cfg, SecDeclutter, "Scan Budget Ms", 2f,
                "프레임당 검사 시간 (ms)", "레이드 시작 때 물체 검사를 여러 프레임에 나눠서 합니다. 한 프레임에 쓸 최대 시간. 작을수록 끊김이 적고 끝나는 데 오래 걸립니다.",
                "Scan budget per frame (ms)", "The raid-start scan is spread over many frames; this is the most time it may use in one frame. Lower = smoother but slower to finish.",
                new AcceptableValueRange<float>(0.5f, 10f), advanced: true);

            // ── 3. 그래픽 가볍게 ────────────────────────────────────
            FramesaverEnabled = Bind(cfg, SecFramesaver, "Enabled", false,
                "그래픽 가볍게 켜기", "아래 항목들을 적용합니다. 끄면 레이드 시작 때의 게임 그래픽 값으로 되돌립니다. 레이드가 끝나면 항상 원래대로 돌아갑니다.",
                "Enable Framesaver", "Applies the items below. Turning it off restores the game's values from raid start. Always restored when the raid ends.");
            SoftParticlesOff = Bind(cfg, SecFramesaver, "Soft Particles Off", false,
                "부드러운 파티클 끄기", "연기·먼지가 벽에 닿는 부분의 부드러운 처리를 끕니다.", "Soft particles off", "Disables soft particle blending.");
            ParticleBudgetDivider = Bind(cfg, SecFramesaver, "Particle Budget Divider", 1,
                "파티클 충돌 계산 줄이기", "1 = 그대로. 클수록 파티클 충돌 계산 수를 나눠서 줄입니다.", "Particle raycast divider", "1 = unchanged. Higher divides the particle raycast budget.",
                new AcceptableValueRange<int>(1, 4));
            SoftVegetationOff = Bind(cfg, SecFramesaver, "Soft Vegetation Off", false,
                "부드러운 식물 끄기", "식물 가장자리 부드러운 처리를 끕니다.", "Soft vegetation off", "Disables soft vegetation.");
            RealtimeReflectionsOff = Bind(cfg, SecFramesaver, "Realtime Reflections Off", false,
                "실시간 반사 끄기", "실시간 반사 프로브를 끕니다(반사가 덜 정확해짐).", "Realtime reflections off", "Disables realtime reflection probes.");
            HardShadowsOnly = Bind(cfg, SecFramesaver, "Hard Shadows Only", false,
                "그림자 단순화", "부드러운 그림자 대신 딱딱한 그림자만 씁니다.", "Hard shadows only", "Uses hard shadows instead of soft ones.");
            ShadowCascades = Bind(cfg, SecFramesaver, "Shadow Cascades", CascadeDefault,
                "그림자 단계 수", "먼 그림자를 나눠 그리는 단계 수. 줄이면 먼 그림자가 거칠어지고 가벼워집니다.", "Shadow cascades", "Fewer cascades = rougher distant shadows, cheaper.",
                new AcceptableValueList<string>(CascadeDefault, CascadeTwo, CascadeNone));
            PixelLightDivider = Bind(cfg, SecFramesaver, "Pixel Light Divider", 1,
                "조명 개수 줄이기", "1 = 그대로. 클수록 픽셀 조명 수를 나눠서 줄입니다(원본은 그림자 값에 묶여 있던 버그 수정).", "Pixel light divider", "1 = unchanged. Higher divides the pixel light count (fixed: it no longer depends on the shadow setting).",
                new AcceptableValueRange<int>(1, 4));
            TextureReduction = Bind(cfg, SecFramesaver, "Texture Reduction", 0,
                "텍스처 해상도 낮추기", "0 = 그대로, 1 = 절반, 2 = 1/4, 3 = 1/8. 게임 설정보다 높게는 안 올립니다. VRAM이 부족할 때만 권장.", "Texture reduction", "0 = unchanged, 1 = half, 2 = quarter, 3 = eighth. Never higher than the game setting. Only for low VRAM.",
                new AcceptableValueRange<int>(0, 3));
            LodBiasDivider = Bind(cfg, SecFramesaver, "LOD Bias Divider", 1f,
                "모델 디테일 거리 줄이기", "1 = 그대로. 클수록 먼 물체가 더 빨리 단순한 모델로 바뀝니다.", "LOD bias divider", "1 = unchanged. Higher switches to simpler models sooner.",
                new AcceptableValueRange<float>(1f, 2f));
            ShellCap = Bind(cfg, SecFramesaver, "Shell Cap", 0,
                "튀는 탄피 최대 개수", "0 = 제한 없음. 사격으로 튀어나온 탄피가 이 개수를 넘으면 오래된 것부터 지웁니다(원본처럼 아예 끄는 대신). 봇이 많은 총격전에서 효과.",
                "Shell cap", "0 = no limit. When ejected casings exceed this, the oldest are removed (instead of disabling them like the original). Helps in big firefights.",
                new AcceptableValueRange<int>(0, 300));
            DecalMaxStatic = Bind(cfg, SecFramesaver, "Bullet Decal Max", 0,
                "총알 자국 최대 개수", "0 = 게임 기본(보통 500, HollywoodFX가 있으면 그 설정값). 벽·바닥의 총알 자국과 피 자국을 이 개수까지만 남깁니다. 줄이는 쪽으로만 바뀝니다. 레이드 시작 때 적용.",
                "Bullet decal max", "0 = game default (usually 500, or HollywoodFX's value). Keeps at most this many bullet holes/blood splats on surfaces. Only ever lowers the limit. Applied at raid start.",
                new AcceptableValueRange<int>(0, 500));
            DecalMaxDynamic = Bind(cfg, SecFramesaver, "Dynamic Decal Max", 0,
                "움직이는 물체 총알 자국 최대", "0 = 게임 기본. 움직이는 물체에 남는 자국 개수.", "Dynamic decal max", "0 = game default. Decals on moving objects.",
                new AcceptableValueRange<int>(0, 500), advanced: true);
            DecalCullDistance = Bind(cfg, SecFramesaver, "Decal Draw Distance", 0,
                "총알 자국 보이는 거리 (m)", "0 = 게임 기본(보통 100m). 이보다 먼 자국은 그리지 않습니다.", "Decal draw distance (m)", "0 = game default (usually 100 m). Decals farther than this are not drawn.",
                new AcceptableValueRange<int>(0, 100));
            MuzzleShadowsOff = Bind(cfg, SecFramesaver, "Muzzle Flash Shadows Off", false,
                "다른 사람 총구 화염 그림자 끄기", "총을 쏠 때 번쩍이는 불빛이 그림자를 만들면 게임이 주변을 그림자용으로 최대 6번 다시 그립니다(총격전에서 그림자 작업이 2배 넘게 늘어남). 켜면 봇·다른 플레이어의 총구 불빛은 그대로 주변을 밝히고 그림자만 안 만듭니다. 내 총은 그대로입니다.",
                "Others' muzzle flashes cast no shadows", "A muzzle flash that casts shadows makes the game redraw the area around it up to 6 times for the shadow map (more than doubles the shadow work in a firefight). With this on, bots' and other players' flashes still light things up, just without shadows. Your own gun is left alone.");
            MuzzleSmokeOncePerFrame = Bind(cfg, SecFramesaver, "Muzzle Smoke Fade Once Per Frame", false,
                "총구 연기 실내 처리 프레임당 1번", "게임은 총 한 자루마다 매 프레임 '총구 연기가 실내인지' 계산을 다시 합니다(모든 총이 같은 값을 공유하는데도). 그래서 총이 많을수록 일이 늘고, 실내·실외 전환이 총 수만큼 빨라지는 버그도 있습니다. 켜면 프레임당 한 번만 합니다. 보이는 차이는 실내로 들어갈 때 연기 색 전환이 원래 의도대로 1초에 걸쳐 바뀌는 정도입니다.",
                "Muzzle smoke indoor check once per frame", "Every gun in the world redoes the shared 'is the muzzle smoke indoors' update each frame, so the work grows with the number of guns and the indoor/outdoor fade runs that many times too fast (a game bug). With this on it runs once per frame. The only visible change: the smoke's indoor fade takes the intended ~1 s.");

            // ── 4. 실험적 ───────────────────────────────────────────
            PhysicsHalfRate = Bind(cfg, SecExperimental, "Physics Half Rate", false,
                "물리 계산 절반", "물리(시체·던진 수류탄·떨어진 물건)를 두 프레임에 한 번, 두 배 시간으로 계산합니다. 원본은 물리가 슬로모션이 되는 문제가 있어 고쳤습니다. 수류탄 궤적·시체 움직임이 살짝 거칠어질 수 있습니다. HollywoodFX의 래그돌(시체 연출)과 같이 쓰면 시체가 튈 수 있어 권장하지 않습니다.",
                "Physics half rate", "Simulates physics (corpses, thrown grenades, dropped items) every other frame with double time step. The original made physics run in slow motion; fixed. Grenade arcs and ragdolls may look slightly rougher. Not recommended with HollywoodFX ragdolls (corpses may jitter).");
            WeatherThrottle = Bind(cfg, SecExperimental, "Weather Throttle", false,
                "하늘·날씨 계산 격프레임", "하늘색·안개·구름 계산을 두 프레임에 한 번만 합니다. 해 그림자 방향이 조금 늦게 따라올 수 있습니다.",
                "Sky & weather every other frame", "Updates sky colour, fog and clouds every other frame. Sun shadow direction may lag slightly.");
            ReflectionDelay = Bind(cfg, SecExperimental, "Ambient Reflection Delay", 0f,
                "주변 반사 갱신 간격 (초)", "0 = 게임 기본(0.05초). 주변 반사 큐브맵을 다시 그리는 간격. 크게 하면 가벼워지지만 반사가 늦게 바뀝니다.",
                "Ambient reflection delay (s)", "0 = game default (0.05 s). Interval for re-rendering the ambient reflection cubemap. Higher is cheaper but reflections update later.",
                new AcceptableValueRange<float>(0f, 2f));
            GrassDistance = Bind(cfg, SecExperimental, "Grass Distance", 0,
                "풀 보이는 거리 (m)", "0 = 게임 기본. 일반 화면에서 풀을 그리는 최대 거리. 배율 스코프로 조준하는 동안에는 원래 거리로 돌아갑니다(스코프 화면은 그대로). 주의: 봇은 풀과 무관하게 보므로 줄이면 플레이어만 멀리 있는 적을 보기 쉬워집니다(공정성 주의).",
                "Grass distance (m)", "0 = game default. Max grass draw distance on the normal view. While aiming through a magnified scope the original distance is used (scope view unchanged). Note: bots are not affected by grass rendering, so lowering this only makes distant enemies easier for you to see.",
                new AcceptableValueRange<int>(0, 300));
            HideUnscrutinized = Bind(cfg, SecExperimental, "Hide Everything Small", false,
                "이름 상관없이 작은 장식 전부 숨김", "원본의 'Unscrutinized'. 이름 목록과 상관없이 충돌체 없는 작은 장식을 전부 숨깁니다. FPS는 크게 오르지만 맵이 휑해지고 엉뚱한 물체가 사라질 수 있습니다. 2번의 항목별 선택은 무시됩니다.",
                "Hide everything small", "The original 'Unscrutinized' option: hides every small collider-less decoration regardless of name. Big FPS gain, but maps look bare and odd objects may vanish. Per-category toggles in section 2 are ignored.");
            SortByMaterial = Bind(cfg, SecExperimental, "Sort By Material", false,
                "불투명 물체를 재질별로 묶어 그리기", "메인 카메라가 불투명 물체를 '가까운 것부터' 대신 '같은 재질끼리' 그립니다. 재질 전환이 줄어 CPU 일이 조금 줄고, 대신 그래픽카드가 뒤에 가려진 것까지 칠하는 일이 조금 늘어납니다. 화면은 똑같습니다. CPU가 병목인 PC(대부분의 타르코프)에서만 이득이고, 그래픽카드가 한계인 PC에선 오히려 느려질 수 있으니 '효과 측정 키'로 확인하세요.",
                "Draw opaque objects grouped by material", "The main camera draws solid objects grouped by material instead of nearest first: fewer material switches for the CPU, a little more shading for the graphics card (hidden things behind nearer ones get shaded too). The picture is the same. Only helps when the CPU is the limit (usual in Tarkov); on a GPU-limited PC it can be slower — check with the benchmark key.");

            ApplyLanguage();
            Language.SettingChanged += (_, __) => ApplyLanguage();
        }

        public static void ApplyLanguage()
        {
            Loc.Ko = Language.Value != LangEn;
            foreach (var t in Texts)
            {
                var names = SectionNames[t.Section];
                t.Attr.Category = Loc.Ko ? names[0] : names[1];
                t.Attr.DispName = Loc.Ko ? t.KoName : t.EnName;
                t.Attr.Description = Loc.Ko ? t.KoDesc : t.EnDesc;
            }
        }

        private static ConfigEntry<T> Bind<T>(ConfigFile cfg, string section, string key, T def,
            string koName, string koDesc, string enName, string enDesc, AcceptableValueBase acceptable = null, bool advanced = false)
        {
            var attr = new ConfigurationManagerAttributes { Order = _order--, IsAdvanced = advanced ? (bool?)true : null };
            var entry = cfg.Bind(section, key, def, new ConfigDescription(koName + " — " + koDesc + "\n" + enName + " — " + enDesc, acceptable, attr));
            Texts.Add(new Text { Section = section, KoName = koName, KoDesc = koDesc, EnName = enName, EnDesc = enDesc, Attr = attr });
            return entry;
        }

        // 프리셋: 2·3번 항목만 바꾼다. 4번(실험적)은 사용자가 직접 켠 것만 쓴다.
        public static void ApplyPreset(string preset)
        {
            switch (preset)
            {
                case PresetOff:
                    DeclutterEnabled.Value = false;
                    FramesaverEnabled.Value = false;
                    ShellCap.Value = 0;
                    DecalMaxStatic.Value = 0;
                    DecalMaxDynamic.Value = 0;
                    DecalCullDistance.Value = 0;
                    break;
                case PresetLight:
                    SetDeclutter(true, ModeFar);
                    FramesaverEnabled.Value = true;
                    SetGraphics(false, 1, false, false, false, CascadeDefault, 1, 0, 1f);
                    SetMuzzle(false, true);
                    ShellCap.Value = 100;
                    DecalMaxStatic.Value = 0;
                    DecalMaxDynamic.Value = 0;
                    DecalCullDistance.Value = 0;
                    break;
                case PresetBalanced:
                    SetDeclutter(true, ModeHide);
                    FramesaverEnabled.Value = true;
                    SetGraphics(true, 2, true, true, false, CascadeDefault, 1, 0, 1f);
                    SetMuzzle(true, true);
                    ShellCap.Value = 60;
                    DecalMaxStatic.Value = 250;
                    DecalMaxDynamic.Value = 0;
                    DecalCullDistance.Value = 60;
                    break;
                case PresetMax:
                    SetDeclutter(true, ModeHide);
                    FramesaverEnabled.Value = true;
                    SetGraphics(true, 4, true, true, true, CascadeTwo, 2, 0, 1.5f);
                    SetMuzzle(true, true);
                    ShellCap.Value = 30;
                    DecalMaxStatic.Value = 150;
                    DecalMaxDynamic.Value = 100;
                    DecalCullDistance.Value = 40;
                    break;
            }
        }

        private static void SetDeclutter(bool on, string mode)
        {
            DeclutterEnabled.Value = on;
            Mode.Value = mode;
            Garbage.Value = Heaps.Value = Cartridges.Value = FakeFood.Value = Decals.Value = Puddles.Value = Shards.Value = true;
        }

        private static void SetMuzzle(bool shadowsOff, bool smokeOnce)
        {
            MuzzleShadowsOff.Value = shadowsOff;
            MuzzleSmokeOncePerFrame.Value = smokeOnce;
        }

        private static void SetGraphics(bool softParticlesOff, int particleDiv, bool softVegOff, bool reflOff, bool hardShadows,
            string cascades, int lightDiv, int texture, float lodDiv)
        {
            SoftParticlesOff.Value = softParticlesOff;
            ParticleBudgetDivider.Value = particleDiv;
            SoftVegetationOff.Value = softVegOff;
            RealtimeReflectionsOff.Value = reflOff;
            HardShadowsOnly.Value = hardShadows;
            ShadowCascades.Value = cascades;
            PixelLightDivider.Value = lightDiv;
            TextureReduction.Value = texture;
            LodBiasDivider.Value = lodDiv;
        }
    }
}
