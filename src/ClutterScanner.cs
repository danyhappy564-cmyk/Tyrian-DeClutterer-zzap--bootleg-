using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using EFT.CameraControl;
using EFT.Interactive;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TYR_DeClutterer
{
    internal enum Kind : byte { Lod, Decal, Particle }

    internal enum Verdict : byte
    {
        NotAnalyzed, Gone, NoMatch, NeverName, UserExcluded, Protected, ProtectedParent, HasCollider, NoMesh, Candidate, DecalAtLoad,
    }

    internal sealed class Entry
    {
        public GameObject Go;
        public LODGroup Lod;
        public Kind Kind;
        public Cat Mask;
        public string Keyword;
        public Verdict V;
        public string Detail;
        public bool Deep;
        public float Height;
        public Vector3 Center;
        public bool HiddenByUs;
        public LOD[] OrigLods;
        public float FarApplied = -1f;
    }

    // 레이드 시작 때 장식 물체 후보를 모으고(1), 여러 프레임에 나눠 검사(2)·적용(3)한다.
    // 한 프레임에 쓰는 시간은 Settings.ScanBudgetMs, 처리 개수도 상한이 있어 멈춤이 생기지 않게 한다.
    internal static class ClutterScanner
    {
        private enum Phase { Idle, CollectRoots, CollectWalk, Analyze, Apply, Done }

        private const int MaxItemsPerFrame = 4000;
        private const int MaxDebugLines = 300; // 자세한 로그도 한 번 적용에 이만큼만(레이드 중 GC가 꺼져 문자열이 쌓임)
        private const int MaxParentDepth = 64;

        private static readonly List<Entry> Entries = new List<Entry>();
        private static readonly HashSet<int> Seen = new HashSet<int>();
        private static readonly Stopwatch Sw = new Stopwatch();
        private static readonly Stopwatch Total = new Stopwatch();
        private static readonly List<Component> CompBuf = new List<Component>();
        private static readonly List<Renderer> RendBuf = new List<Renderer>();
        // 수집도 씬 루트 단위로 나눠서(한 번에 FindObjectsByType 하면 Streets 15만 개에서 한 프레임 92ms였음).
        private static readonly List<GameObject> Roots = new List<GameObject>();
        private static readonly List<GameObject> RootBuf = new List<GameObject>();
        private static readonly List<LODGroup> LodBuf = new List<LODGroup>();
        private static readonly List<StaticDeferredDecal> DecalBuf = new List<StaticDeferredDecal>();
        private static readonly List<ParticleSystem> ParticleBuf = new List<ParticleSystem>();

        private static Phase _phase = Phase.Idle;
        private static int _cursor;
        private static bool _reanalyze;
        private static bool _firstPassLogged;
        private static string _mapId;
        private static int _frames;
        private static double _maxFrameMs;

        // 적용 결과 집계
        private static int _hidden, _far;
        // 분류별 숨김 수. Dictionary<enum,int>는 Mono에서 조회마다 박싱이 생겨 배열로 둔다. [8] = 기타(이름 무관 옵션)
        private static readonly int[] HiddenByCat = new int[9];
        private static int _debugLines;

        public static bool Busy => _phase != Phase.Idle && _phase != Phase.Done;
        public static int HiddenCount => _hidden;
        public static int FarCount => _far;

        public static void Begin(string mapId)
        {
            Clear();
            _mapId = mapId;
            _phase = Phase.CollectRoots;
            _frames = 0;
            _maxFrameMs = 0;
            _firstPassLogged = false;
            Total.Reset();
            Total.Start();
        }

        public static void Clear()
        {
            Entries.Clear();
            Seen.Clear();
            Roots.Clear();
            _phase = Phase.Idle;
            _cursor = 0;
            _reanalyze = false;
            _hidden = _far = 0;
            System.Array.Clear(HiddenByCat, 0, HiddenByCat.Length);
            _debugLines = 0;
        }

        // 설정이 바뀌면 처음부터 다시 적용(필요하면 다시 검사).
        public static void MarkDirty(bool reanalyze)
        {
            if (_phase == Phase.Idle) return;
            if (_phase == Phase.CollectRoots || _phase == Phase.CollectWalk) return;
            if (reanalyze) _reanalyze = true;
            if (_phase == Phase.Analyze)
            {
                // 검사 중이면 끝난 뒤 적용 단계가 어차피 처음부터 돈다. 다시 검사가 필요하면 처음부터(끝난 것은 빨리 건너뜀).
                if (reanalyze) _cursor = 0;
                return;
            }
            _phase = _reanalyze ? Phase.Analyze : Phase.Apply;
            _cursor = 0;
            ResetStats();
            // 이번 적용 한 번의 시간·프레임만 요약에 남게 새로 잰다.
            _frames = 0;
            _maxFrameMs = 0;
            Total.Reset();
            Total.Start();
        }

        public static void Tick()
        {
            if (!Busy) return;
            Sw.Reset();
            Sw.Start();
            double budget = Settings.ScanBudgetMs.Value;
            int processed = 0;

            switch (_phase)
            {
                case Phase.CollectRoots:
                    Roots.Clear();
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                    {
                        var scene = SceneManager.GetSceneAt(i);
                        if (!scene.isLoaded) continue;
                        RootBuf.Clear();
                        scene.GetRootGameObjects(RootBuf);
                        Roots.AddRange(RootBuf);
                    }
                    _phase = Phase.CollectWalk;
                    _cursor = 0;
                    break;
                case Phase.CollectWalk:
                    // 켜진 것만(FindObjectsOfType와 같은 기준). 루트 하나가 아주 크면 그 프레임만 조금 길어진다.
                    while (_cursor < Roots.Count && processed < MaxItemsPerFrame && Sw.Elapsed.TotalMilliseconds < budget)
                    {
                        var root = Roots[_cursor];
                        _cursor++;
                        processed++;
                        if (root == null || !root.activeInHierarchy) continue;
                        root.GetComponentsInChildren(false, LodBuf);
                        foreach (var lg in LodBuf) Add(lg.gameObject, Kind.Lod, lg);
                        root.GetComponentsInChildren(false, DecalBuf);
                        foreach (var d in DecalBuf) Add(d.gameObject, Kind.Decal, null);
                        root.GetComponentsInChildren(false, ParticleBuf);
                        foreach (var p in ParticleBuf) Add(p.gameObject, Kind.Particle, null);
                    }
                    if (_cursor >= Roots.Count)
                    {
                        Roots.Clear();
                        LodBuf.Clear();
                        DecalBuf.Clear();
                        ParticleBuf.Clear();
                        _phase = Phase.Analyze;
                        _cursor = 0;
                    }
                    break;
                case Phase.Analyze:
                    while (_cursor < Entries.Count && processed < MaxItemsPerFrame && Sw.Elapsed.TotalMilliseconds < budget)
                    {
                        Analyze(Entries[_cursor], false);
                        _cursor++;
                        processed++;
                    }
                    if (_cursor >= Entries.Count)
                    {
                        _reanalyze = false;
                        _phase = Phase.Apply;
                        _cursor = 0;
                        ResetStats();
                    }
                    break;
                case Phase.Apply:
                    while (_cursor < Entries.Count && processed < MaxItemsPerFrame && Sw.Elapsed.TotalMilliseconds < budget)
                    {
                        ApplyOne(Entries[_cursor]);
                        _cursor++;
                        processed++;
                    }
                    if (_cursor >= Entries.Count)
                    {
                        _phase = Phase.Done;
                        LogSummary();
                    }
                    break;
            }

            Sw.Stop();
            _frames++;
            if (Sw.Elapsed.TotalMilliseconds > _maxFrameMs) _maxFrameMs = Sw.Elapsed.TotalMilliseconds;
        }

        private static void Add(GameObject go, Kind kind, LODGroup lod)
        {
            if (go == null || !Seen.Add(go.GetInstanceID())) return;
            Entries.Add(new Entry { Go = go, Kind = kind, Lod = lod });
        }

        private static void ResetStats()
        {
            _hidden = _far = 0;
            System.Array.Clear(HiddenByCat, 0, HiddenByCat.Length);
            _debugLines = 0;
        }

        // 2단계: 이름·컴포넌트·충돌체·크기를 본다. 이름이 안 맞으면 비싼 검사는 생략(이름 무관 옵션이 켜지면 다시).
        private static void Analyze(Entry e, bool forceDeep)
        {
            if (e.Go == null) { e.V = Verdict.Gone; return; }
            bool needDeep = forceDeep || Settings.HideUnscrutinized.Value;
            // 이미 끝까지 본 것은 건너뜀. 이름만 보고 넘긴 것(NoMatch)은 깊은 검사가 필요해졌을 때만 다시.
            if (e.V != Verdict.NotAnalyzed && (e.Deep || !needDeep)) return;

            string lower = e.Go.name.ToLowerInvariant();
            e.Mask = ClutterRules.Match(lower, out e.Keyword);
            // 파티클은 '불·연기·먼지' 항목으로만, 맵 데칼은 '데칼' 항목으로만 다룬다(원본도 각 옵션이 켜져야 후보였음).
            if (e.Kind == Kind.Particle) e.Mask = e.Mask != Cat.None ? Cat.FireSmoke : Cat.None;
            else if (e.Kind == Kind.Decal) e.Mask = e.Mask != Cat.None ? Cat.Decals : Cat.None;
            else e.Mask &= ~Cat.FireSmoke;

            if (e.Kind == Kind.Decal)
            {
                // 데칼은 여기서 판정하지 않는다 — 맵 로드 때 DecalGate가 이미 그리기에서 뺐는지만 기록.
                bool blocked = DecalGate.IsBlocked(e.Go.GetComponent<StaticDeferredDecal>());
                Set(e, blocked ? Verdict.DecalAtLoad : Verdict.NoMatch, null);
                return;
            }
            if (ClutterRules.IsNeverName(lower, out var never)) { Set(e, Verdict.NeverName, never); return; }
            if (ClutterRules.IsUserExcluded(lower, out var ex)) { Set(e, Verdict.UserExcluded, ex); return; }
            if (e.Mask == Cat.None && !needDeep) { e.V = Verdict.NoMatch; return; }

            e.Deep = true;
            // 레이드 중에 만들어져 '살아남는' 오브젝트(DontDestroyOnLoad)는 맵 장식이 아니다(다른 모드의 풀·UI 등).
            if (e.Go.scene.name == "DontDestroyOnLoad") { Set(e, Verdict.Protected, "DontDestroyOnLoad"); return; }
            if (e.Kind == Kind.Particle)
            {
                // 맵에 깔린 불·연기는 시작부터 반복 재생 중이다. 멈춰 있는 것은 이펙트 풀(게임·HollywoodFX)이라 손대지 않는다.
                var ps = e.Go.GetComponent<ParticleSystem>();
                if (ps == null || !ps.main.loop || !ps.isPlaying) { Set(e, Verdict.Protected, "EffectPool"); return; }
            }
            // 하위(꺼진 것 포함)에 보호 컴포넌트나 충돌체가 하나라도 있으면 안 숨김 — 총알·발소리·봇 판단에 영향 없게.
            CompBuf.Clear();
            e.Go.GetComponentsInChildren(true, CompBuf);
            foreach (var c in CompBuf)
            {
                if (c == null) continue;
                if (ClutterRules.IsProtectedComponent(c)) { Set(e, Verdict.Protected, c.GetType().Name); return; }
            }
            foreach (var c in CompBuf)
            {
                if (c is Collider) { Set(e, Verdict.HasCollider, c.gameObject.name); return; }
            }

            var parent = e.Go.transform.parent;
            if (e.Kind == Kind.Particle && parent != null)
            {
                // 불 파티클 옆(형제)에 데미지 구역이 있으면 안 숨김(안 보이는 불 방지).
                var dmg = parent.GetComponentInChildren<DamageTrigger>(true);
                if (dmg != null) { Set(e, Verdict.Protected, dmg.GetType().Name); return; }
            }
            int depth = 0;
            for (var t = parent; t != null && depth < MaxParentDepth; t = t.parent, depth++)
            {
                CompBuf.Clear();
                t.GetComponents(CompBuf);
                foreach (var c in CompBuf)
                {
                    if (c != null && ClutterRules.IsProtectedParent(c)) { Set(e, Verdict.ProtectedParent, c.GetType().Name); return; }
                }
            }

            if (e.Kind == Kind.Lod)
            {
                RendBuf.Clear();
                e.Go.GetComponentsInChildren(false, RendBuf);
                bool any = false;
                Bounds b = default;
                foreach (var r in RendBuf)
                {
                    if (r == null || r.gameObject.name.ToLowerInvariant().Contains("shadow")) continue;
                    if (!any) { b = r.bounds; any = true; }
                    else b.Encapsulate(r.bounds);
                }
                if (!any) { Set(e, Verdict.NoMesh, null); return; }
                e.Height = b.size.y;
                e.Center = b.center;
            }
            else
            {
                e.Height = 0.1f;
                e.Center = e.Go.transform.position;
            }
            e.V = Verdict.Candidate;
            e.Detail = null;
        }

        private static void Set(Entry e, Verdict v, string detail)
        {
            e.V = v;
            e.Detail = detail;
            e.Deep = true;
        }

        private static bool Wants(Entry e, Cat enabled, out Cat countAs)
        {
            countAs = Cat.None;
            if (!Plugin.DeclutterActive || e.V != Verdict.Candidate || e.Go == null || e.Kind == Kind.Decal) return false;
            if (e.Kind == Kind.Lod && e.Height > 2f * Settings.SizeScale.Value) return false;
            if (Settings.HideUnscrutinized.Value)
            {
                if (e.Kind == Kind.Particle && !Settings.FireSmoke.Value) return false;
                countAs = e.Mask == Cat.None ? Cat.None : First(e.Mask);
                return true;
            }
            Cat m = e.Mask & enabled;
            if (m == Cat.None) return false;
            countAs = First(m);
            return true;
        }

        private static Cat First(Cat m)
        {
            for (int i = 0; i < 8; i++)
            {
                var c = (Cat)(1 << i);
                if ((m & c) != 0) return c;
            }
            return Cat.None;
        }

        private static int CatIndex(Cat c)
        {
            for (int i = 0; i < 8; i++) if (c == (Cat)(1 << i)) return i;
            return 8;
        }

        // 3단계: 원하는 상태로 맞춘다(숨김 / 멀리서만 숨김 / 원래대로).
        private static void ApplyOne(Entry e)
        {
            if (e.Go == null) { e.HiddenByUs = false; return; }
            if (e.Kind == Kind.Decal)
            {
                if (e.V == Verdict.DecalAtLoad) { _hidden++; HiddenByCat[CatIndex(Cat.Decals)]++; }
                return;
            }
            bool want = Wants(e, ClutterRules.EnabledMask(), out var cat);
            bool far = want && Settings.IsFarMode && e.Kind == Kind.Lod && e.Lod != null;

            if (far)
            {
                if (e.HiddenByUs) { e.Go.SetActive(true); e.HiddenByUs = false; }
                if (ApplyFar(e, Settings.FarDistance.Value)) _far++;
                return;
            }
            RestoreLod(e);
            if (want)
            {
                if (!e.HiddenByUs && e.Go.activeSelf)
                {
                    e.Go.SetActive(false);
                    e.HiddenByUs = true;
                    if (Settings.DebugLog.Value && _debugLines++ < MaxDebugLines)
                        Plugin.Log.LogInfo($"[DeClutter] hide {e.Go.name} ({ClutterRules.CatName(cat)}, '{e.Keyword}')");
                }
                if (e.HiddenByUs)
                {
                    _hidden++;
                    HiddenByCat[CatIndex(cat)]++;
                }
            }
            else if (e.HiddenByUs)
            {
                e.Go.SetActive(true);
                e.HiddenByUs = false;
            }
        }

        // 마지막 LOD 전환 높이를 앞당겨 '이 거리보다 멀면 안 그림'이 되게 한다. 원래 LOD는 보관했다가 되돌린다.
        private static bool ApplyFar(Entry e, float dist)
        {
            var lg = e.Lod;
            if (lg == null) return false;
            if (e.OrigLods == null) e.OrigLods = lg.GetLODs();
            int n = e.OrigLods.Length;
            if (n == 0) return false;

            float fov = 75f;
            var cm = CameraManager.Instance;
            if (cm != null && cm.Camera != null) fov = cm.Camera.fieldOfView;
            var s = lg.transform.lossyScale;
            float scale = Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
            float h = lg.size * scale * QualitySettings.lodBias / (2f * dist * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));
            h = Mathf.Clamp(h, 0.0001f, 0.9f);

            if (e.OrigLods[n - 1].screenRelativeTransitionHeight >= h)
            {
                RestoreLod(e);
                return false; // 원래도 그 거리 전에 사라짐
            }
            var lods = (LOD[])e.OrigLods.Clone();
            lods[n - 1].screenRelativeTransitionHeight = h;
            for (int i = n - 2; i >= 0; i--)
            {
                if (lods[i].screenRelativeTransitionHeight <= lods[i + 1].screenRelativeTransitionHeight)
                    lods[i].screenRelativeTransitionHeight = Mathf.Min(1f, lods[i + 1].screenRelativeTransitionHeight + 0.001f);
            }
            lg.SetLODs(lods);
            e.FarApplied = dist;
            return true;
        }

        private static void RestoreLod(Entry e)
        {
            if (e.FarApplied < 0f) return;
            if (e.Lod != null && e.OrigLods != null) e.Lod.SetLODs(e.OrigLods);
            e.FarApplied = -1f;
        }

        private static void LogSummary()
        {
            Total.Stop();
            var sb = new StringBuilder();
            for (int i = 0; i < HiddenByCat.Length; i++)
            {
                if (HiddenByCat[i] == 0) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(ClutterRules.CatName(i < 8 ? (Cat)(1 << i) : Cat.None)).Append(' ').Append(HiddenByCat[i]);
                if (i == CatIndex(Cat.Decals)) sb.Append(Loc.L("(로드 때 제외)", " (at load)"));
            }
            if (Settings.DebugLog.Value && _debugLines > MaxDebugLines)
                Plugin.Log.LogInfo($"[DeClutter] hide ... +{_debugLines - MaxDebugLines} more (debug log capped)");
            int prot = 0, coll = 0, never = 0, user = 0;
            foreach (var e in Entries)
            {
                switch (e.V)
                {
                    case Verdict.Protected:
                    case Verdict.ProtectedParent: prot++; break;
                    case Verdict.HasCollider: coll++; break;
                    case Verdict.NeverName: never++; break;
                    case Verdict.UserExcluded: user++; break;
                }
            }
            string head = _firstPassLogged ? Loc.L("다시 적용", "re-applied") : Loc.L("레이드 시작 검사", "raid-start scan");
            Plugin.Log.LogInfo(Loc.L(
                $"[DeClutter] {head} ({_mapId}): 후보 {Entries.Count}개 → 숨김 {_hidden}개({sb}), 멀리서만 {_far}개 / 안 숨김: 충돌체 {coll}, 보호 {prot}, 이름규칙 {never}, exclude.txt {user} / {_frames}프레임, 프레임당 최대 {_maxFrameMs:0.0}ms, 총 {Total.Elapsed.TotalSeconds:0.0}초",
                $"[DeClutter] {head} ({_mapId}): {Entries.Count} candidates → hidden {_hidden} ({sb}), far-only {_far} / kept: collider {coll}, protected {prot}, name rule {never}, exclude.txt {user} / {_frames} frames, max {_maxFrameMs:0.0} ms/frame, total {Total.Elapsed.TotalSeconds:0.0} s"));
            if (!_firstPassLogged && Entries.Count == 0)
                Plugin.Log.LogWarning(Loc.L("[DeClutter] 후보가 0개입니다 — 맵이 덜 실린 시점에 검사했을 수 있습니다. 로그와 함께 제보해 주세요.",
                    "[DeClutter] 0 candidates — the scan may have run before the map finished loading. Please report with your log."));
            _firstPassLogged = true;
            _frames = 0;
            _maxFrameMs = 0;
            Total.Reset();
            Total.Start();
        }

        // 화면 가운데에 가장 가까운 후보 물체를 찾아 이름과 판정 이유를 알려 준다.
        public static string Inspect()
        {
            var cm = CameraManager.Instance;
            Camera cam = cm != null ? cm.Camera : Camera.main;
            if (cam == null) return Loc.L("카메라를 찾지 못했습니다.", "Camera not found.");
            if (Entries.Count == 0) return Loc.L("아직 검사한 물체가 없습니다.", "Nothing scanned yet.");
            Vector3 o = cam.transform.position, f = cam.transform.forward;
            Entry best = null;
            float bestScore = float.MaxValue, bestDist = 0f;
            foreach (var e in Entries)
            {
                if (e.Go == null) continue;
                Vector3 p = e.Deep ? e.Center : e.Go.transform.position;
                Vector3 v = p - o;
                float t = Vector3.Dot(v, f);
                if (t < 0.3f || t > 80f) continue;
                float perp = (v - f * t).magnitude;
                if (perp > 2f) continue;
                float score = perp / t;
                if (score < bestScore) { bestScore = score; best = e; bestDist = t; }
            }
            if (best == null) return Loc.L("화면 가운데 근처에 장식 물체가 없습니다.", "No decoration object near the screen centre.");
            if (!best.Deep) Analyze(best, true);
            string state = best.HiddenByUs ? Loc.L("숨김", "hidden") : best.FarApplied >= 0f ? Loc.L("멀리서만 숨김", "far-only") : Loc.L("보임", "visible");
            string why = Reason(best);
            string cats = best.Mask == Cat.None ? "-" : best.Mask.ToString();
            return Loc.L(
                $"'{best.Go.name}' ({best.Kind}, {bestDist:0.0}m, 높이 {best.Height:0.00}m, 분류 {cats}) — {state}: {why}",
                $"'{best.Go.name}' ({best.Kind}, {bestDist:0.0} m, height {best.Height:0.00} m, categories {cats}) — {state}: {why}");
        }

        private static string Reason(Entry e)
        {
            switch (e.V)
            {
                case Verdict.NoMatch: return Loc.L("이름이 잡동사니 목록에 없음", "name not in clutter list");
                case Verdict.NeverName: return Loc.L($"이름에 '{e.Detail}' 포함 — 항상 유지", $"name contains '{e.Detail}' — always kept");
                case Verdict.UserExcluded: return Loc.L($"exclude.txt의 '{e.Detail}'", $"exclude.txt entry '{e.Detail}'");
                case Verdict.Protected: return Loc.L($"보호 대상 컴포넌트 {e.Detail}", $"protected component {e.Detail}");
                case Verdict.ProtectedParent: return Loc.L($"부모가 상호작용 물체({e.Detail})", $"parent is interactive ({e.Detail})");
                case Verdict.HasCollider: return Loc.L($"충돌체 있음({e.Detail}) — 총알·발소리·봇 판단 유지", $"has collider ({e.Detail}) — kept for bullets/footsteps/AI");
                case Verdict.NoMesh: return Loc.L("보이는 메시 없음", "no visible mesh");
                case Verdict.Gone: return Loc.L("이미 사라진 물체", "object destroyed");
                case Verdict.DecalAtLoad: return Loc.L("맵 로드 때 그리기에서 뺀 데칼(데칼은 레이드 단위 — 바꾸면 다음 레이드부터)", "decal excluded from drawing at map load (decals are per-raid; changes apply next raid)");
                case Verdict.Candidate:
                    if (e.Kind == Kind.Lod && e.Height > 2f * Settings.SizeScale.Value) return Loc.L("크기가 커서 유지(숨길 크기 배율)", "too big (size scale)");
                    if (!Plugin.DeclutterActive) return Loc.L("잡동사니 숨기기 꺼짐", "DeClutter is off");
                    if ((e.Mask & ClutterRules.EnabledMask()) == Cat.None && !Settings.HideUnscrutinized.Value) return Loc.L("해당 분류가 꺼져 있음", "its category is turned off");
                    return Loc.L($"잡동사니('{e.Keyword}')", $"clutter ('{e.Keyword}')");
                default: return Loc.L("아직 검사 전", "not analyzed yet");
            }
        }
    }
}
