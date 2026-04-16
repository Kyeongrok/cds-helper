using System.Drawing;
using System.IO;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 게임 화면을 캡처하여 현재 어떤 화면인지 판별하는 경량 감지기.
/// 템플릿 매칭 없이 픽셀 분석 + OCR로 동작한다.
/// </summary>
public class GameScreenDetector
{
    private readonly CoordinateOcrService _ocrService;

    public GameScreenDetector(CoordinateOcrService ocrService)
    {
        _ocrService = ocrService;
    }

    /// <summary>
    /// 캡처된 화면에서 현재 게임 화면 종류를 판별한다.
    /// </summary>
    public async Task<GameScreen> DetectScreenAsync(Bitmap bitmap)
    {
        var result = await DetectScreenWithOcrAsync(bitmap);
        return result.Screen;
    }

    /// <summary>
    /// 캡처된 화면에서 현재 게임 화면 종류를 판별하고, OCR 결과도 함께 반환한다.
    /// 이벤트 다이얼로그 감지 시 어떤 텍스트로 판정됐는지 표시할 때 사용.
    /// </summary>
    public async Task<ScreenDetection> DetectScreenWithOcrAsync(Bitmap bitmap)
    {
        // 1. 전쟁 화면 감지 (대부분 검은 배경) — 도시보다 먼저 체크
        if (IsBattleScreen(bitmap))
            return new ScreenDetection(GameScreen.Battle, null);

        // 2. 도시 안인지 확인 (프레임 테두리 감지)
        if (IsInCity(bitmap))
            return new ScreenDetection(GameScreen.City, null);

        // 3. 다이얼로그/메뉴가 있는지 OCR로 확인
        var ocrResult = await OcrGameArea(bitmap);
        if (ocrResult != null)
        {
            if (ocrResult.Contains("중단") || ocrResult.Contains("힌트"))
                return new ScreenDetection(GameScreen.HintList, ocrResult);
            if (ocrResult.Contains("돌아간다") && ocrResult.Contains("정보"))
                return new ScreenDetection(GameScreen.InfoMenu, ocrResult);
            if (ocrResult.Contains("커맨드") && ocrResult.Contains("취소"))
                return new ScreenDetection(GameScreen.CommandMenu, ocrResult);
            // 이벤트 다이얼로그: "확인" 버튼만 있는 알림 (예: 이슬람 함대 조우)
            // 다른 메뉴 패턴에 매칭 안 될 때만 감지
            if (ocrResult.Contains("확인"))
                return new ScreenDetection(GameScreen.EventDialog, ocrResult);
        }

        // 4. 기본: 탐험 중
        return new ScreenDetection(GameScreen.Exploration, ocrResult);
    }

    /// <summary>
    /// 캡처된 화면에서 도시 안인지 판별한다.
    /// 도시 안이면 화면 중앙에 고정 크기의 장식 액자 프레임이 존재한다.
    /// 프레임의 좌/우 세로 테두리 위치에서 어두운 픽셀을 샘플링하여 판정.
    /// </summary>
    public static bool IsInCity(Bitmap bitmap)
    {
        int w = bitmap.Width;
        int h = bitmap.Height;
        if (w < 200 || h < 200) return false;

        // 좌측 가장자리(x: 0~3%)에서 파란 회색 배경을 감지
        // 도시 안이면 프레임 바깥이 파란 회색, 탐험 중이면 지형색
        int blueCount = 0;
        int totalSamples = 0;

        for (int yPct = 20; yPct <= 50; yPct += 3)
        {
            int y = h * yPct / 100;
            for (int x = 2; x <= 20; x += 4)
            {
                if (x >= w || y >= h) continue;
                totalSamples++;
                var p = bitmap.GetPixel(x, y);
                if (IsBlueGray(p)) blueCount++;
            }
        }

        // 80% 이상이 파란 회색이면 도시 안
        return totalSamples > 0 && blueCount >= totalSamples * 80 / 100;
    }

    /// <summary>도시 프레임 바깥 파란 회색 배경인지 판별</summary>
    private static bool IsBlueGray(Color p)
    {
        // 실제 측정값 기준: RGB(67-72, 86-91, 122-127)
        // 넓은 범위로 확장
        return p.B > p.R &&
               p.B > p.G &&
               (p.B - p.R) >= 20 &&
               p.R >= 40 && p.R <= 130 &&
               p.G >= 55 && p.G <= 140 &&
               p.B >= 90 && p.B <= 180;
    }

    private static Color GetPixelSafe(Bitmap bmp, int x, int y)
    {
        x = Math.Clamp(x, 0, bmp.Width - 1);
        y = Math.Clamp(y, 0, bmp.Height - 1);
        return bmp.GetPixel(x, y);
    }

    /// <summary>
    /// 힌트/정보 화면에서 자동으로 빠져나온다.
    /// HintList → ESC로 닫기, InfoMenu → "돌아간다" 선택
    /// </summary>
    public static async Task DismissDialogAsync(IntPtr hWnd, GameScreen screen, CancellationToken token = default)
    {
        switch (screen)
        {
            case GameScreen.HintList:
                // 힌트 목록 → ESC로 닫기
                GameWindowHelper.SendEscKey(hWnd);
                await Task.Delay(500, token);
                // 정보 메뉴가 나올 수 있으므로 한번 더
                GameWindowHelper.SendEscKey(hWnd);
                await Task.Delay(500, token);
                break;

            case GameScreen.InfoMenu:
                // 정보 메뉴 → ESC로 닫기
                GameWindowHelper.SendEscKey(hWnd);
                await Task.Delay(500, token);
                break;

            case GameScreen.CommandMenu:
                // 커맨드 메뉴 → ESC로 취소
                GameWindowHelper.SendEscKey(hWnd);
                await Task.Delay(500, token);
                break;

            case GameScreen.Battle:
                // 전투 → 돌격 (첫 번째 옵션이므로 Enter)
                GameWindowHelper.SendEnterKey(hWnd);
                await Task.Delay(1000, token);
                break;

            case GameScreen.EventDialog:
                // 이벤트 다이얼로그 → 확인 (Enter)
                GameWindowHelper.SendEnterKey(hWnd);
                await Task.Delay(500, token);
                break;
        }
    }

    /// <summary>게임 콘텐츠 영역을 OCR한다.</summary>
    private async Task<string?> OcrGameArea(Bitmap bitmap)
    {
        try
        {
            int regionY = Math.Min(20, bitmap.Height - 1);
            int regionH = Math.Max(bitmap.Height * 70 / 100, 1);
            var region = new Rectangle(0, regionY, bitmap.Width, regionH);
            return await _ocrService.RecognizeRegionAsync(bitmap, region);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 전쟁 화면 감지: 화면 상단 중앙에 초승달 모양이 있는지 확인.
    /// 상단 30% 영역에서 검은 배경 위에 밝은 픽셀(달)이 있으면 전쟁 화면.
    /// </summary>
    public static bool IsBattleScreen(Bitmap bitmap)
    {
        int w = bitmap.Width;
        int h = bitmap.Height;
        if (w < 100 || h < 100) return false;

        // 상단 15~35% 영역, 좌우 30~70% 범위에서 달 모양 탐색
        int darkCount = 0;
        int brightCount = 0;
        int totalSamples = 0;

        for (int yPct = 15; yPct <= 35; yPct += 2)
        {
            for (int xPct = 30; xPct <= 70; xPct += 2)
            {
                int x = w * xPct / 100;
                int y = h * yPct / 100;
                if (x >= w || y >= h) continue;
                totalSamples++;
                var p = bitmap.GetPixel(x, y);
                if (p.R < 30 && p.G < 30 && p.B < 30)
                    darkCount++;
                else if (p.R > 150 && p.G > 120 && p.B > 50)
                    brightCount++;
            }
        }

        if (totalSamples == 0) return false;

        // 해당 영역에서 대부분 검은색(60%+)이고 밝은 픽셀(달)이 일부(3%+) 존재
        double darkRatio = (double)darkCount / totalSamples;
        double brightRatio = (double)brightCount / totalSamples;
        return darkRatio >= 0.60 && brightRatio >= 0.03;
    }

    private static bool IsDarkAt(Bitmap bmp, int x, int y, int w, int h)
    {
        if (x < 0 || x >= w || y < 0 || y >= h) return false;
        var p = bmp.GetPixel(x, y);
        return p.R < 90 && p.G < 80 && p.B < 70;
    }
}

/// <summary>화면 감지 결과 (화면 종류 + OCR 텍스트).</summary>
public record ScreenDetection(GameScreen Screen, string? OcrText);

/// <summary>게임 화면 종류 (경량 감지용).</summary>
public enum GameScreen
{
    /// <summary>탐험 중 (이동 가능)</summary>
    Exploration,
    /// <summary>도시 안 (프레임 있음)</summary>
    City,
    /// <summary>힌트 목록 등 다이얼로그 표시 중</summary>
    HintList,
    /// <summary>정보 메뉴 표시 중</summary>
    InfoMenu,
    /// <summary>커맨드 메뉴 표시 중</summary>
    CommandMenu,
    /// <summary>전쟁/전투 화면</summary>
    Battle,
    /// <summary>"확인" 버튼만 있는 이벤트 알림 다이얼로그 (예: 이슬람 함대 조우)</summary>
    EventDialog,
}
