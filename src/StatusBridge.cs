using System.Text;

namespace TYR_DeClutterer
{
    // 다른 모드(RAM 클리너 웹 페이지의 'DeClutter' 탭)가 참조 없이 이름으로 찾아 읽는 상태 정보.
    // 이름·Version을 바꾸면 그쪽이 못 읽는다(버전이 다르면 그쪽은 상태 칸만 숨긴다). 메인 스레드에서 읽는다.
    public static class StatusBridge
    {
        public const int Version = 1;

        public static string Status
        {
            get
            {
                var sb = new StringBuilder();
                if (!Plugin.InRaid)
                {
                    sb.Append(Loc.L("레이드 밖 (레이드가 시작되면 2초 뒤 검사를 시작합니다)", "Not in a raid (the scan starts 2 s into a raid)"));
                }
                else
                {
                    sb.Append(Plugin.Suspended ? Loc.L("레이드 중 · 잠시 꺼짐 (켜기/끄기 키)", "In raid · paused (toggle key)") : Loc.L("레이드 중 · 켜짐", "In raid · on"));
                    sb.Append('\n').Append(ClutterScanner.Busy ? Loc.L("검사·적용 중...", "Scanning / applying...") : Loc.L("검사 끝", "Scan done"));
                    sb.Append('\n').Append(Loc.L("숨긴 물체: ", "Hidden objects: ")).Append(ClutterScanner.HiddenCount.ToString("N0"))
                      .Append(Loc.L(" (멀리서만 숨김 ", " (at distance only ")).Append(ClutterScanner.FarCount.ToString("N0")).Append(')');
                    sb.Append('\n').Append(Loc.L("로드 때 뺀 맵 데칼: ", "Map decals left out at load: ")).Append(DecalGate.BlockedCount.ToString("N0"));
                }
                sb.Append('\n').Append(Loc.L("프리셋: ", "Preset: ")).Append(Settings.Preset.Value);
                return sb.ToString();
            }
        }
    }
}
