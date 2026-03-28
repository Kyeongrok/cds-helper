using System.Drawing;
using System.IO;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 직업 선택 화면에서 능력치(지력 등)가 목표치 이상이 될 때까지
/// 두 직업 버튼을 번갈아 클릭하여 능력치를 리롤하는 서비스.
/// 게임 화면을 캡처하여 다이얼로그, 버튼, 능력치 영역을 자동 감지한다.
/// </summary>
public class StatRerollService : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _runningTask;
    private readonly Dictionary<int, Mat> _digitTemplates = new();
    private readonly string _numberTemplateDir;

    public bool IsRunning => _runningTask is { IsCompleted: false };

    public event Action<string>? LogMessage;
    public event Action<int, int>? Progress;
    public event Action<int, int>? Completed;
    public event Action? Stopped;

    /// <summary>감지된 레이아웃 정보.</summary>
    public record DetectedLayout(
        Rect Dialog,
        Rect[] StatRois,       // 5행 (체력~운) 숫자 영역
        OpenCvSharp.Point Btn1, // 발굴자 중심
        OpenCvSharp.Point Btn2  // 사냥꾼 중심
    );

    public StatRerollService(string? templateBaseDir = null)
    {
        var baseDir = templateBaseDir
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "git", "ml", "cds-ai", "assets", "templates");
        _numberTemplateDir = Path.Combine(baseDir, "numbers");
        LoadDigitTemplates();
    }

    /// <summary>리롤을 시작한다.</summary>
    public void Start(int targetStat, int clickDelay = 300)
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _runningTask = Task.Run(() => RunLoop(targetStat, clickDelay, _cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        Log("리롤 중지됨");
        Stopped?.Invoke();
    }

    /// <summary>테스트: 지력값 읽기.</summary>
    public int TestRead()
    {
        var (gray, bitmap, layout) = CaptureAndDetect();
        if (layout == null || bitmap == null) return -1;

        using (gray) using (bitmap)
        {
            if (layout.StatRois.Length < 4) { Log("박스 부족"); return -1; }
            var value = ReadTwoDigits(bitmap, layout.StatRois[2], layout.StatRois[3]);
            Log(value >= 0 ? $"인식 결과: {value}" : "인식 실패");
            return value;
        }
    }

    /// <summary>5개 능력치를 한 번에 학습한다. 입력: "79,50,74,59,80" (체력,지력,무력,매력,운).</summary>
    public void LearnDigits(string allValues)
    {
        // 입력 파싱
        var values = allValues
            .Split(new[] { ',', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => int.TryParse(s, out _))
            .Select(int.Parse)
            .ToList();

        if (values.Count == 0 || values.Count > 6)
        {
            Log("능력치 값을 입력하세요 (예: 79,50,74,59,80)");
            return;
        }

        var (gray, bitmap, layout) = CaptureAndDetect();
        if (layout == null || gray == null || bitmap == null) return;

        using (gray) using (bitmap)
        {
            // 5개 값 → 10자리 (십의자리, 일의자리 교차)
            var allDigits = new List<int>();
            foreach (var v in values)
            {
                string s = v.ToString().PadLeft(2, '0');
                allDigits.Add(s[0] - '0');
                allDigits.Add(s[1] - '0');
            }

            int count = Math.Min(allDigits.Count, layout.StatRois.Length);
            Log($"입력 {values.Count}개 → {count}자리 학습 (박스 {layout.StatRois.Length}개)");
            Directory.CreateDirectory(_numberTemplateDir);
            int savedCount = 0;

            for (int i = 0; i < count; i++)
            {
                var roi = ClampRect(layout.StatRois[i], gray.Cols, gray.Rows);
                if (roi.Width <= 0 || roi.Height <= 0) continue;

                using var roiMat = new Mat(gray, roi);
                using var binary = new Mat();
                Cv2.Threshold(roiMat, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

                var path = Path.Combine(_numberTemplateDir, $"{allDigits[i]}.png");
                if (File.Exists(path))
                {
                    Log($"  [{i}] '{allDigits[i]}' 이미 존재 — 건너뜀");
                    continue;
                }
                Cv2.ImWrite(path, binary);
                savedCount++;
            }

            Log($"학습: {savedCount}자리 저장");

            // 템플릿 리로드
            LoadDigitTemplates();
            Log($"학습 완료! {savedCount}개 저장, 총 {_digitTemplates.Count}종 템플릿 사용 가능");

            // 부족한 숫자 확인
            var missing = Enumerable.Range(0, 10).Where(d => !_digitTemplates.ContainsKey(d)).ToList();
            if (missing.Count > 0)
                Log($"미학습 숫자: {string.Join(", ", missing)} — 다른 값이 보일 때 추가 학습하세요");
        }
    }

    #region 레이아웃 자동 감지

    /// <summary>게임 윈도우를 활성화하고 캡처 + 레이아웃 감지.</summary>
    private (Mat? gray, Bitmap? bitmap, DetectedLayout? layout) CaptureAndDetect()
    {
        var hWnd = GameWindowHelper.FindGameWindow();
        if (hWnd == IntPtr.Zero) { Log("게임 윈도우를 찾을 수 없습니다."); return (null, null, null); }

        GameWindowHelper.BringToFront(hWnd);
        Thread.Sleep(500);

        var (cw, ch) = GameWindowHelper.GetClientSize(hWnd);
        Log($"클라이언트 크기: {cw}x{ch}");

        var bitmap = GameWindowHelper.CaptureClient(hWnd);
        if (bitmap == null) { Log("화면 캡처 실패"); return (null, null, null); }

        var mat = BitmapConverter.ToMat(bitmap);
        var gray = new Mat();
        Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY);

        Directory.CreateDirectory(_numberTemplateDir);
        mat.Dispose();

        var layout = DetectLayout(gray);
        if (layout == null) { gray.Dispose(); bitmap.Dispose(); return (null, null, null); }

        Log($"다이얼로그: ({layout.Dialog.X},{layout.Dialog.Y}) {layout.Dialog.Width}x{layout.Dialog.Height}");

        // 디버그: 캡처 위에 감지 영역 표시
        using var debugMat = BitmapConverter.ToMat(bitmap);
        Cv2.Rectangle(debugMat, layout.Dialog, new Scalar(0, 255, 0), 1);
        for (int i = 0; i < layout.StatRois.Length; i++)
            Cv2.Rectangle(debugMat, layout.StatRois[i], new Scalar(0, 0, 255), 1);
        Cv2.Circle(debugMat, layout.Btn1, 5, new Scalar(255, 0, 0), 1);
        Cv2.Circle(debugMat, layout.Btn2, 5, new Scalar(255, 0, 0), 1);

        var debugPath = Path.Combine(_numberTemplateDir, $"debug_{DateTime.Now:HHmmss}.png");
        Cv2.ImWrite(debugPath, debugMat);
        Log($"디버그 이미지 저장: {debugPath}");

        return (gray, bitmap, layout);
    }

    /// <summary>게임 화면에서 능력치 다이얼로그, 버튼, 5개 능력치 행 영역을 자동 감지한다.</summary>
    private DetectedLayout? DetectLayout(Mat gray)
    {
        // 1) 어두운 다이얼로그 영역 찾기
        using var darkMask = new Mat();
        Cv2.Threshold(gray, darkMask, 70, 255, ThresholdTypes.BinaryInv);

        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(7, 7));
        using var closed = new Mat();
        Cv2.MorphologyEx(darkMask, closed, MorphTypes.Close, kernel);

        Cv2.FindContours(closed, out var contours, out _,
            RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        // 적절한 크기/비율의 어두운 사각형 중 가장 큰 것 = 다이얼로그
        // 위치 제한 없음 (다이얼로그는 화면 어디에나 있을 수 있음)
        Rect? dialogRect = null;
        double maxArea = 0;

        foreach (var contour in contours)
        {
            var r = Cv2.BoundingRect(contour);
            var area = (double)r.Width * r.Height;
            var screenArea = (double)gray.Cols * gray.Rows;

            // 크기: 화면의 1~40%
            if (area < screenArea * 0.01 || area > screenArea * 0.40) continue;

            // 가로세로 비율: 1.0 ~ 2.5
            double aspect = (double)r.Width / r.Height;
            if (aspect < 1.0 || aspect > 2.5) continue;

            if (area > maxArea) { maxArea = area; dialogRect = r; }
        }

        if (dialogRect == null)
        {
            Log($"다이얼로그 영역을 찾을 수 없습니다. (후보 윤곽: {contours.Length}개)");
            return null;
        }
        var dlg = dialogRect.Value;

        // 2) 숫자 영역: 1자리씩 10개 박스 (5행 × 십의자리/일의자리)
        //    실측: 다이얼로그(288x208) 내 십의자리 (79,24)-(87,38), 일의자리 (87,24)-(95,38)
        double tensXPct = 0.277;  // 79/288
        double onesXPct = 0.305;  // 87/288
        double digitWPct = 0.028; // 8/288
        double digitHPct = 0.080; // 14/208
        double[] rowYPcts = { 0.116, 0.235, 0.350, 0.465, 0.580 };

        int tensX = dlg.X + (int)(dlg.Width * tensXPct);
        int onesX = dlg.X + (int)(dlg.Width * onesXPct);
        int digitW = Math.Max(1, (int)(dlg.Width * digitWPct));
        int digitH = Math.Max(1, (int)(dlg.Height * digitHPct));

        var statRois = rowYPcts.SelectMany(yPct =>
        {
            int y = dlg.Y + (int)(dlg.Height * yPct);
            return new[]
            {
                new Rect(tensX, y, digitW, digitH), // 십의 자리
                new Rect(onesX, y, digitW, digitH), // 일의 자리
            };
        }).ToArray();

        Log($"숫자 영역 5행 (다이얼로그 {dlg.Width}x{dlg.Height} 기준):");
        for (int i = 0; i < statRois.Length; i++)
            Log($"  행{i}: ({statRois[i].X},{statRois[i].Y}) {statRois[i].Width}x{statRois[i].Height}");

        // 3) 직업 버튼 찾기 (우측 상단 75%)
        int rightX = dlg.X + (int)(dlg.Width * 0.55);
        int rightW = dlg.X + dlg.Width - rightX;
        int btnRegionH = (int)(dlg.Height * 0.75);
        if (rightW <= 0) return null;

        var rightRegion = ClampRect(new Rect(rightX, dlg.Y, rightW, btnRegionH), gray.Cols, gray.Rows);
        using var rightRoi = new Mat(gray, rightRegion);
        using var btnMask = new Mat();
        Cv2.Threshold(rightRoi, btnMask, 100, 255, ThresholdTypes.Binary);

        using var btnKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(5, 3));
        using var btnClean = new Mat();
        Cv2.MorphologyEx(btnMask, btnClean, MorphTypes.Close, btnKernel);

        Cv2.FindContours(btnClean, out var btnContours, out _,
            RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var buttons = btnContours
            .Select(Cv2.BoundingRect)
            .Where(r => r.Width > rightW * 0.25 && r.Height >= 5 && r.Height < dlg.Height * 0.25)
            .OrderBy(r => r.Y)
            .Take(4) // 직업 버튼 4개만
            .ToList();

        if (buttons.Count < 3) { Log($"버튼 감지 실패 ({buttons.Count}개)"); return null; }

        var btn1 = new OpenCvSharp.Point(
            rightRegion.X + buttons[1].X + buttons[1].Width / 2,
            rightRegion.Y + buttons[1].Y + buttons[1].Height / 2);
        var btn2 = new OpenCvSharp.Point(
            rightRegion.X + buttons[2].X + buttons[2].Width / 2,
            rightRegion.Y + buttons[2].Y + buttons[2].Height / 2);

        Log($"버튼 {buttons.Count}개 감지 (발굴자: {btn1}, 사냥꾼: {btn2})");
        return new DetectedLayout(dlg, statRois, btn1, btn2);
    }

    #endregion

    #region 리롤 루프

    private async Task RunLoop(int target, int delay, CancellationToken token)
    {
        var hWnd = GameWindowHelper.FindGameWindow();
        if (hWnd == IntPtr.Zero) { Log("게임 윈도우를 찾을 수 없습니다."); return; }

        GameWindowHelper.BringToFront(hWnd);
        await Task.Delay(500, token);

        using var initBitmap = GameWindowHelper.CaptureClient(hWnd);
        if (initBitmap == null) { Log("화면 캡처 실패"); return; }

        using var initMat = BitmapConverter.ToMat(initBitmap);
        using var initGray = new Mat();
        Cv2.CvtColor(initMat, initGray, ColorConversionCodes.BGR2GRAY);

        var layout = DetectLayout(initGray);
        if (layout == null) { Log("다이얼로그 감지 실패. 직업 선택 화면이 열려 있는지 확인하세요."); return; }

        if (_digitTemplates.Count == 0)
        {
            Log("숫자 템플릿이 없습니다. 먼저 '학습'을 실행하세요.");
            return;
        }

        if (layout.StatRois.Length < 4)
        {
            Log("지력 박스를 감지할 수 없습니다.");
            return;
        }
        // 지력 = 박스[2](십의자리) + 박스[3](일의자리)
        var tensRoi = layout.StatRois[2];
        var onesRoi = layout.StatRois[3];
        Log($"리롤 시작 — 목표 지력: {target} 이상 (템플릿 {_digitTemplates.Count}종)");
        Log($"지력 영역: 십({tensRoi.X},{tensRoi.Y}), 일({onesRoi.X},{onesRoi.Y})");

        bool useBtn1 = true;
        int attempts = 0;

        while (!token.IsCancellationRequested)
        {
            try
            {
                hWnd = GameWindowHelper.FindGameWindow();
                if (hWnd == IntPtr.Zero) { await Task.Delay(2000, token); continue; }

                var pos = useBtn1 ? layout.Btn1 : layout.Btn2;
                GameWindowHelper.SendClickRelative(hWnd, pos.X, pos.Y);
                useBtn1 = !useBtn1;
                attempts++;

                await Task.Delay(delay, token);

                using var bitmap = GameWindowHelper.CaptureClient(hWnd);
                if (bitmap == null) continue;

                var value = ReadTwoDigits(bitmap, tensRoi, onesRoi);
                if (value < 0)
                {
                    if (attempts % 20 == 0) Log($"[{attempts}회] 인식 실패");
                    continue;
                }

                Progress?.Invoke(value, attempts);

                if (value >= target)
                {
                    Log($"[{attempts}회] 목표 달성! 지력 = {value}");
                    Completed?.Invoke(value, attempts);
                    return;
                }

                if (attempts % 50 == 0)
                    Log($"[{attempts}회] 현재 지력 = {value}");
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log($"오류: {ex.Message}");
                await Task.Delay(2000, token);
            }
        }
    }

    #endregion

    #region 숫자 인식

    /// <summary>2개 박스에서 십의자리 + 일의자리를 읽어 2자리 값을 반환한다.</summary>
    private int ReadTwoDigits(Bitmap screenshot, Rect tensRoi, Rect onesRoi)
    {
        int tens = ReadSingleDigit(screenshot, tensRoi);
        int ones = ReadSingleDigit(screenshot, onesRoi);
        if (tens < 0 || ones < 0) return -1;
        return tens * 10 + ones;
    }

    /// <summary>1개 박스에서 숫자 하나를 읽는다.</summary>
    private int ReadSingleDigit(Bitmap screenshot, Rect roi)
    {
        if (_digitTemplates.Count == 0) return -1;

        using var screenMat = BitmapConverter.ToMat(screenshot);
        using var gray = new Mat();
        Cv2.CvtColor(screenMat, gray, ColorConversionCodes.BGR2GRAY);

        var clamped = ClampRect(roi, gray.Cols, gray.Rows);
        if (clamped.Width <= 0 || clamped.Height <= 0) return -1;

        using var roiMat = new Mat(gray, clamped);
        using var binary = new Mat();
        Cv2.Threshold(roiMat, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        return MatchDigit(binary);
    }

    private int MatchDigit(Mat digitMat)
    {
        int bestDigit = -1;
        double bestScore = -1;

        foreach (var (digit, template) in _digitTemplates)
        {
            using var resized = new Mat();
            Cv2.Resize(digitMat, resized, new OpenCvSharp.Size(template.Cols, template.Rows));

            using var result = new Mat();
            Cv2.MatchTemplate(resized, template, result, TemplateMatchModes.CCoeffNormed);
            var score = result.At<float>(0, 0);

            if (score > bestScore) { bestScore = score; bestDigit = digit; }
        }

        return bestScore > 0.5 ? bestDigit : -1;
    }

    private void LoadDigitTemplates()
    {
        foreach (var (_, m) in _digitTemplates) m.Dispose();
        _digitTemplates.Clear();
        if (!Directory.Exists(_numberTemplateDir)) return;

        for (int d = 0; d <= 9; d++)
        {
            var path = Path.Combine(_numberTemplateDir, $"{d}.png");
            if (!File.Exists(path)) continue;
            var mat = Cv2.ImRead(path, ImreadModes.Grayscale);
            if (!mat.Empty()) _digitTemplates[d] = mat;
        }

        if (_digitTemplates.Count > 0)
            Log($"숫자 템플릿 {_digitTemplates.Count}종 로드됨");
    }

    #endregion

    #region 유틸

    private static Rect ClampRect(Rect r, int maxW, int maxH)
    {
        int x = Math.Clamp(r.X, 0, maxW - 1);
        int y = Math.Clamp(r.Y, 0, maxH - 1);
        int w = Math.Min(r.Width, maxW - x);
        int h = Math.Min(r.Height, maxH - y);
        return new Rect(x, y, Math.Max(0, w), Math.Max(0, h));
    }

    private void Log(string msg) => LogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");

    public void Dispose()
    {
        _cts?.Cancel(); _cts?.Dispose();
        foreach (var (_, m) in _digitTemplates) m.Dispose();
        _digitTemplates.Clear();
    }

    #endregion
}
