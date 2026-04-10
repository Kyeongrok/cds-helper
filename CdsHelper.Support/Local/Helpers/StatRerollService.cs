using System.Drawing;
using System.IO;
using CdsHelper.Support.Local.Settings;
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
    private readonly Lazy<OnnxDigitRecognizer> _onnx = new(() => new OnnxDigitRecognizer());

    public bool IsRunning => _runningTask is { IsCompleted: false };
    public string NumberTemplateDir => _numberTemplateDir;

    public event Action<string>? LogMessage;
    public event Action<int, int>? Progress;
    public event Action<int, int>? Completed;
    public event Action? Stopped;
    public event Action? TemplatesChanged;

    /// <summary>감지된 레이아웃 정보.</summary>
    public record DetectedLayout(
        Rect Dialog,
        Rect[] StatRois,            // 10개 박스 (5행 × 십/일의자리)
        OpenCvSharp.Point HunterBtn // 사냥꾼 버튼 중심
    );

    public StatRerollService(string? templateBaseDir = null)
    {
        var baseDir = templateBaseDir
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates");
        _numberTemplateDir = Path.Combine(baseDir, "numbers");
        LoadDigitTemplates();
    }

    /// <summary>리롤을 시작한다. targets: 체력,지력,무력,매력,운,보너스 (0=무시)</summary>
    public void Start(int[] targets, int clickDelay = 300, int maxAttempts = 50)
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _runningTask = Task.Run(() => RunLoop(targets, clickDelay, maxAttempts, _cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        Log("리롤 중지됨");
        Stopped?.Invoke();
    }

    /// <summary>테스트: 모든 능력치 읽기.</summary>
    public int TestRead()
    {
        var (gray, bitmap, layout) = CaptureAndDetect();
        if (layout == null || bitmap == null) return -1;

        using (gray) using (bitmap)
        {
            if (layout.StatRois.Length < 4) { Log("박스 부족"); return -1; }

            string[] names = { "체력", "지력", "무력", "매력", "운", "보너스" };
            var results = new List<string>();
            for (int i = 0; i < Math.Min(6, layout.StatRois.Length / 2); i++)
            {
                int val = ReadTwoDigits(bitmap, layout.StatRois[i * 2], layout.StatRois[i * 2 + 1]);
                results.Add($"{names[i]}={val}");
            }
            Log($"인식 결과: {string.Join(", ", results)}");

            // 지력 값 반환
            return ReadTwoDigits(bitmap, layout.StatRois[2], layout.StatRois[3]);
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

    /// <summary>ONNX 모델로 화면의 숫자를 자동 인식하여 템플릿을 생성한다.</summary>
    public void AutoLearnDigits()
    {
        var (gray, bitmap, layout) = CaptureAndDetect();
        if (layout == null || gray == null || bitmap == null)
        {
            Log("게임 화면 감지 실패");
            return;
        }

        using (gray) using (bitmap)
        {
            int count = layout.StatRois.Length;
            Log($"자동 학습: {count}개 박스 감지, ONNX 인식 중...");
            Directory.CreateDirectory(_numberTemplateDir);
            int savedCount = 0;

            for (int i = 0; i < count; i++)
            {
                var roi = ClampRect(layout.StatRois[i], gray.Cols, gray.Rows);
                if (roi.Width <= 0 || roi.Height <= 0) continue;

                using var roiMat = new Mat(gray, roi);
                using var binary = new Mat();
                Cv2.Threshold(roiMat, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

                int digit;
                float conf;
                try { (digit, conf) = _onnx.Value.Recognize(binary, 0.5f); }
                catch (Exception ex) { Log($"ONNX 오류: {ex.Message}"); return; }

                if (digit < 0)
                {
                    Log($"  [{i}] 인식 실패 (conf={conf:P0})");
                    continue;
                }

                Log($"  [{i}] → '{digit}' (conf={conf:P0})");

                var path = Path.Combine(_numberTemplateDir, $"{digit}.png");
                if (File.Exists(path))
                    continue;
                Cv2.ImWrite(path, binary);
                savedCount++;
            }

            LoadDigitTemplates();
            Log($"자동 학습 완료! {savedCount}개 저장, 총 {_digitTemplates.Count}종 템플릿");

            var missing = Enumerable.Range(0, 10).Where(d => !_digitTemplates.ContainsKey(d)).ToList();
            if (missing.Count > 0)
                Log($"미학습 숫자: {string.Join(", ", missing)}");
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

        var bgr = BitmapConverter.ToMat(bitmap);
        var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);

        Directory.CreateDirectory(_numberTemplateDir);

        var layout = DetectLayout(gray);
        bgr.Dispose();
        if (layout == null) { gray.Dispose(); bitmap.Dispose(); return (null, null, null); }

        Log($"다이얼로그: ({layout.Dialog.X},{layout.Dialog.Y}) {layout.Dialog.Width}x{layout.Dialog.Height}");

        // 디버그: 캡처 위에 감지 영역 표시
        using var debugMat = BitmapConverter.ToMat(bitmap);
        Cv2.Rectangle(debugMat, layout.Dialog, new Scalar(0, 255, 0), 1);
        for (int i = 0; i < layout.StatRois.Length; i++)
            Cv2.Rectangle(debugMat, layout.StatRois[i], new Scalar(0, 0, 255), 1);
        Cv2.Circle(debugMat, layout.HunterBtn, 5, new Scalar(255, 0, 0), 1);

        var debugPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"debug_{DateTime.Now:HHmmss}.png");
        Cv2.ImWrite(debugPath, debugMat);
        Log($"디버그 이미지 저장: {debugPath}");

        return (gray, bitmap, layout);
    }

    /// <summary>게임 화면에서 능력치 다이얼로그, 버튼, 숫자 영역을 자동 감지한다.</summary>
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

        Rect? dialogRect = null;
        double maxArea = 0;

        foreach (var contour in contours)
        {
            var r = Cv2.BoundingRect(contour);
            var area = (double)r.Width * r.Height;
            var screenArea = (double)gray.Cols * gray.Rows;

            if (area < screenArea * 0.01 || area > screenArea * 0.40) continue;
            double aspect = (double)r.Width / r.Height;
            if (aspect < 1.0 || aspect > 2.5) continue;

            if (area > maxArea) { maxArea = area; dialogRect = r; }
        }

        if (dialogRect == null)
        {
            // 실패 시에도 전체 화면 디버그 이미지 저장
            try
            {
                using var failDebug = new Mat();
                Cv2.CvtColor(gray, failDebug, ColorConversionCodes.GRAY2BGR);
                var failPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"detect_nodlg_{DateTime.Now:HHmmss}.png");
                Cv2.ImWrite(failPath, failDebug);
                Log($"다이얼로그 실패 디버그: {failPath}");
            }
            catch { }
            Log($"다이얼로그 영역을 찾을 수 없습니다. (후보 윤곽: {contours.Length}개)");
            return null;
        }
        var dlg = dialogRect.Value;

        // 2) 다이얼로그 왼쪽 절반에서 Gray 이진화 → contour → 겹침 머지 → ONNX 필터
        //    능력치 숫자 + 보너스 포인트 모두 왼쪽 절반에 위치
        var dlgArea = ClampRect(
            new Rect(dlg.X, dlg.Y, dlg.Width / 2, dlg.Height),
            gray.Cols, gray.Rows);
        using var dlgGray = new Mat(gray, dlgArea);

        double minH = dlg.Height * 0.03;
        double maxH = dlg.Height * 0.20;
        double maxW = dlg.Width * 0.15; // 한 글자 최대 폭

        using var mask = new Mat();
        Cv2.Threshold(dlgGray, mask, 80, 255, ThresholdTypes.Binary);

        // List: 하얀 테두리 안쪽 contour도 찾음
        Cv2.FindContours(mask, out var contourArr, out _,
            RetrievalModes.List, ContourApproximationModes.ApproxSimple);

        // 모든 contour의 bounding rect 수집 (최소 크기만 필터)
        var rawRects = new List<Rect>();
        foreach (var c in contourArr)
        {
            var r = Cv2.BoundingRect(c);
            if (r.Height < 2 || r.Width < 1) continue;
            // 너무 큰 건 제외 (다이얼로그 전체, 하얀 테두리 등)
            if (r.Height > maxH || r.Width > maxW) continue;
            rawRects.Add(r);
        }

        // 겹치는 rect만 머지 (gap=1: 1px 이내 = 같은 글자의 조각)
        var merged = MergeCloseRects(rawRects, 1);

        // 머지 후 크기 필터 + 두 자리 분리 + ONNX 판별
        var digitRects = new List<Rect>();
        foreach (var r in merged)
        {
            if (r.Height < minH || r.Height > maxH) continue;

            double aspect = (double)r.Width / r.Height;
            var singles = new List<Rect>();
            if (aspect >= 0.8)
            {
                int halfW = r.Width / 2;
                singles.Add(new Rect(r.X, r.Y, halfW, r.Height));
                singles.Add(new Rect(r.X + halfW, r.Y, r.Width - halfW, r.Height));
            }
            else
            {
                singles.Add(r);
            }

            foreach (var s in singles)
            {
                var roiClamped = ClampRect(s, dlgGray.Cols, dlgGray.Rows);
                if (roiClamped.Width <= 0 || roiClamped.Height <= 0) continue;

                using var roiMat = new Mat(dlgGray, roiClamped);
                using var binary = new Mat();
                Cv2.Threshold(roiMat, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

                try
                {
                    var (digit, conf) = _onnx.Value.Recognize(binary, 0.8f);
                    if (digit >= 0)
                        digitRects.Add(new Rect(dlgArea.X + s.X, dlgArea.Y + s.Y, s.Width, s.Height));
                }
                catch { }
            }
        }

        Log($"숫자 감지: contour {contourArr.Length}개 → 머지 {merged.Count}개 → ONNX {digitRects.Count}개");

        // ONNX 통과한 모든 후보를 디버그 이미지로 저장 (행 그룹핑/필터 전)
        SaveContourDebugImage(gray, dlg, digitRects, "detect_onnx");

        // Y좌표 기준으로 행 그룹핑
        digitRects = digitRects.OrderBy(r => r.Y).ThenBy(r => r.X).ToList();
        var rows = new List<List<Rect>>();
        foreach (var r in digitRects)
        {
            int centerY = r.Y + r.Height / 2;
            var matched = rows.FirstOrDefault(row =>
                Math.Abs(row[0].Y + row[0].Height / 2 - centerY) < maxH);

            if (matched != null) matched.Add(r);
            else rows.Add(new List<Rect> { r });
        }
        foreach (var row in rows) row.Sort((a, b) => a.X.CompareTo(b.X));

        // 각 행에서 가장 가까운 인접 쌍 선택 (십의자리-일의자리는 붙어있고, 화살표/라벨은 떨어져 있음)
        var statRows = new List<List<Rect>>();
        foreach (var row in rows.OrderBy(r => r[0].Y))
        {
            if (row.Count < 2) continue;
            int bestIdx = 0;
            int bestGap = int.MaxValue;
            for (int i = 0; i < row.Count - 1; i++)
            {
                int gap = row[i + 1].X - (row[i].X + row[i].Width);
                if (gap < bestGap) { bestGap = gap; bestIdx = i; }
            }
            statRows.Add(new List<Rect> { row[bestIdx], row[bestIdx + 1] });
        }

        if (statRows.Count < 5)
        {
            // 실패 시에도 디버그 이미지 저장
            var failRects = statRows.SelectMany(r => r).ToList();
            SaveContourDebugImage(gray, dlg, failRects, "detect_fail");
            Log($"능력치 행이 부족합니다: {statRows.Count}행 (최소 5행 필요)");
            return null;
        }

        // 각 숫자의 중심점 기준 고정 크기 ROI 생성 (겹침 방지)
        // 십의자리와 일의자리 사이 간격의 절반을 폭으로 사용
        var statRois = new List<Rect>();
        foreach (var pair in statRows)
        {
            var tens = pair[0];
            var ones = pair[1];
            // 두 숫자 사이 간격 기준으로 개별 폭 결정
            int gap = ones.X - (tens.X + tens.Width);
            int digitW = Math.Max(tens.Width, ones.Width);
            int digitH = Math.Max(tens.Height, ones.Height);
            // 패딩: 상하 2px, 좌우는 간격의 절반 이하
            int padY = 2;
            int padX = Math.Max(1, Math.Min(2, gap / 2));

            foreach (var r in pair)
            {
                int cx = r.X + r.Width / 2;
                int cy = r.Y + r.Height / 2;
                int roiW = digitW + padX * 2;
                int roiH = digitH + padY * 2;
                statRois.Add(ClampRect(
                    new Rect(cx - roiW / 2, cy - roiH / 2, roiW, roiH),
                    gray.Cols, gray.Rows));
            }
        }

        SaveContourDebugImage(gray, dlg, statRois, "detect_digits");
        Log($"자동 감지: {statRows.Count}행, {statRois.Count}개 숫자 (다이얼로그 {dlg.Width}x{dlg.Height})");

        // 3) 사냥꾼 버튼: 오른쪽 절반에서 텍스트 행 자동 감지 → 템플릿 매칭
        var hunterBtn = FindHunterButton(gray, dlg);
        if (hunterBtn == null)
        {
            Log("사냥꾼 버튼을 찾을 수 없습니다.");
            return null;
        }

        Log($"사냥꾼 버튼: ({hunterBtn.Value.X},{hunterBtn.Value.Y})");
        return new DetectedLayout(dlg, statRois.ToArray(), hunterBtn.Value);
    }

    /// <summary>다이얼로그 오른쪽에서 사냥꾼 버튼을 찾는다.</summary>
    private OpenCvSharp.Point? FindHunterButton(Mat gray, Rect dlg)
    {
        var hunterTemplatePath = Path.Combine(_numberTemplateDir, "hunter_btn.png");

        // 오른쪽 절반 ROI
        var rightHalf = ClampRect(
            new Rect(dlg.X + dlg.Width / 2, dlg.Y, dlg.Width / 2, dlg.Height),
            gray.Cols, gray.Rows);
        using var rightRoi = new Mat(gray, rightHalf);

        // 저장된 사냥꾼 템플릿이 있으면 → 템플릿 매칭
        if (File.Exists(hunterTemplatePath))
        {
            using var tmpl = Cv2.ImRead(hunterTemplatePath, ImreadModes.Grayscale);
            if (!tmpl.Empty() && tmpl.Rows <= rightRoi.Rows && tmpl.Cols <= rightRoi.Cols)
            {
                using var matchResult = new Mat();
                Cv2.MatchTemplate(rightRoi, tmpl, matchResult, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(matchResult, out _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);

                if (maxVal > 0.7)
                {
                    int cx = rightHalf.X + maxLoc.X + tmpl.Cols / 2;
                    int cy = rightHalf.Y + maxLoc.Y + tmpl.Rows / 2;
                    Log($"사냥꾼 템플릿 매칭: score={maxVal:F2}");
                    return new OpenCvSharp.Point(cx, cy);
                }
                Log($"사냥꾼 템플릿 매칭 실패 (score={maxVal:F2}), 재감지...");
            }
        }

        // 템플릿 없음 → 오른쪽 절반에서 텍스트 행 자동 감지
        using var bright = new Mat();
        Cv2.Threshold(rightRoi, bright, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        Cv2.FindContours(bright, out var btnContours, out _,
            RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        double minH = dlg.Height * 0.04;
        double maxH = dlg.Height * 0.15;

        var charRects = new List<Rect>();
        foreach (var c in btnContours)
        {
            var r = Cv2.BoundingRect(c);
            if (r.Height < minH || r.Height > maxH) continue;
            if (r.Width < 2) continue;
            charRects.Add(r);
        }

        // 디버그: 오른쪽 후보 contour 표시
        var rightCharRectsAbs = charRects.Select(r =>
            new Rect(rightHalf.X + r.X, rightHalf.Y + r.Y, r.Width, r.Height)).ToList();
        SaveContourDebugImage(gray, dlg, rightCharRectsAbs, "detect_right");

        if (charRects.Count == 0) return null;

        // Y기준 행 그룹핑
        charRects = charRects.OrderBy(r => r.Y).ToList();
        var btnRows = new List<List<Rect>>();
        foreach (var r in charRects)
        {
            int cy = r.Y + r.Height / 2;
            var matched = btnRows.FirstOrDefault(row =>
                Math.Abs(row[0].Y + row[0].Height / 2 - cy) < maxH);

            if (matched != null) matched.Add(r);
            else btnRows.Add(new List<Rect> { r });
        }

        // 글자 3개인 행 = 사냥꾼 (3글자) 후보
        // 직업 목록: 항해사(3), 측량사(3), 사냥꾼(3), 의사(2), 회계사(3) 등
        // 3글자 행 중 위에서 3번째 행 = 사냥꾼
        var threeCharRows = btnRows
            .Where(row => row.Count == 3)
            .OrderBy(row => row[0].Y)
            .ToList();

        List<Rect>? hunterRow = null;
        if (threeCharRows.Count >= 3)
            hunterRow = threeCharRows[2]; // 3번째 3글자 행
        else if (btnRows.Count >= 3)
            hunterRow = btnRows.OrderBy(r => r[0].Y).ElementAtOrDefault(2); // 전체 3번째 행

        if (hunterRow == null || hunterRow.Count == 0) return null;

        // 행의 전체 bounding box → 사냥꾼 템플릿으로 저장
        int minX = hunterRow.Min(r => r.X);
        int minY = hunterRow.Min(r => r.Y);
        int maxX = hunterRow.Max(r => r.X + r.Width);
        int maxY = hunterRow.Max(r => r.Y + r.Height);
        var hunterRect = new Rect(minX, minY, maxX - minX, maxY - minY);

        // 여유 패딩
        int pad = 2;
        var padded = new Rect(hunterRect.X - pad, hunterRect.Y - pad,
            hunterRect.Width + pad * 2, hunterRect.Height + pad * 2);
        padded = ClampRect(padded, rightRoi.Cols, rightRoi.Rows);

        using var hunterTemplate = new Mat(rightRoi, padded);
        Directory.CreateDirectory(_numberTemplateDir);
        Cv2.ImWrite(hunterTemplatePath, hunterTemplate);
        Log($"사냥꾼 버튼 템플릿 저장: {hunterTemplatePath} ({hunterRow.Count}글자)");

        int centerX = rightHalf.X + hunterRect.X + hunterRect.Width / 2;
        int centerY = rightHalf.Y + hunterRect.Y + hunterRect.Height / 2;
        return new OpenCvSharp.Point(centerX, centerY);
    }

    #endregion

    #region 리롤 루프

    // 직업버튼 능력치 갱신 패치 바이트
    private const int JobButtonPatchOffset = 0x0005CCDA;
    private const int JobButtonPatchLength = 31;
    private static readonly byte[] JobButtonPatchedBytes = new byte[]
    {
        0x89, 0x86, 0x50, 0x01, 0x00, 0x00, 0xB9, 0x48, 0x0C, 0x58, 0x00, 0x6A, 0x05, 0xFF, 0x34, 0x85,
        0x18, 0x27, 0x55, 0x00, 0xE8, 0xAD, 0x07, 0xFB, 0xFF, 0x8B, 0xCE, 0xE8, 0x56, 0xFB, 0xFF
    };

    private bool IsJobButtonPatchApplied()
    {
        var savePath = AppSettings.LastSaveFilePath;
        if (string.IsNullOrEmpty(savePath)) return false;

        var gameFolder = Path.GetDirectoryName(savePath);
        if (string.IsNullOrEmpty(gameFolder)) return false;

        var exePath = Path.Combine(gameFolder, "cds_95.exe");
        if (!File.Exists(exePath)) return false;

        try
        {
            using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read);
            if (fs.Length < JobButtonPatchOffset + JobButtonPatchLength) return false;

            fs.Seek(JobButtonPatchOffset, SeekOrigin.Begin);
            var buf = new byte[JobButtonPatchLength];
            fs.Read(buf, 0, JobButtonPatchLength);

            for (int i = 0; i < JobButtonPatchLength; i++)
                if (buf[i] != JobButtonPatchedBytes[i]) return false;
            return true;
        }
        catch { return false; }
    }

    private async Task RunLoop(int[] targets, int delay, int maxAttempts, CancellationToken token)
    {
        // 직업버튼 능력치 갱신 패치 확인
        if (!IsJobButtonPatchApplied())
        {
            Log("⚠ exe패치에서 '직업버튼 능력갱신' 패치가 적용되어 있지 않습니다.");
            Log("  exe패치 탭에서 패치를 먼저 적용해주세요.");
            Stopped?.Invoke();
            return;
        }

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

        if (layout.StatRois.Length < 4)
        {
            Log("지력 박스를 감지할 수 없습니다.");
            return;
        }

        // 0~9 템플릿이 모두 없으면 사냥꾼 클릭 → 자동학습 반복 (최대 20회)
        var missingDigits = Enumerable.Range(0, 10).Where(d => !_digitTemplates.ContainsKey(d)).ToList();
        if (missingDigits.Count > 0)
        {
            Log($"미학습 숫자: {string.Join(", ", missingDigits)} — 자동 학습 시작");
            for (int autoAttempt = 0; autoAttempt < 20 && !token.IsCancellationRequested; autoAttempt++)
            {
                if (IsHotkeyPressed())
                {
                    Log("Ctrl+Alt 감지 — 자동 학습 중지");
                    Stopped?.Invoke();
                    return;
                }

                // 사냥꾼 버튼 클릭 (새 능력치 생성)
                GameWindowHelper.SendClickRelative(hWnd, layout.HunterBtn.X, layout.HunterBtn.Y);
                await Task.Delay(delay > 0 ? delay : 100, token);

                // 자동 학습
                AutoLearnDigits();

                TemplatesChanged?.Invoke();

                missingDigits = Enumerable.Range(0, 10).Where(d => !_digitTemplates.ContainsKey(d)).ToList();
                if (missingDigits.Count == 0)
                {
                    Log($"자동 학습 완료! ({autoAttempt + 1}회 시도) — 0~9 모두 확보");
                    break;
                }
                Log($"[자동학습 {autoAttempt + 1}/20] 미학습: {string.Join(", ", missingDigits)}");
            }

            if (missingDigits.Count > 0)
            {
                Log($"20회 시도 후에도 미학습 숫자 존재: {string.Join(", ", missingDigits)} — 리롤 중단");
                Stopped?.Invoke();
                return;
            }
        }

        string[] names = { "체력", "지력", "무력", "매력", "운", "보너스" };
        var goalParts = new List<string>();
        for (int i = 0; i < Math.Min(targets.Length, 6); i++)
            if (targets[i] > 0) goalParts.Add($"{names[i]}>={targets[i]}");
        Log($"리롤 시작 — 목표: {string.Join(", ", goalParts)}, 최대 {maxAttempts}회 (템플릿 {_digitTemplates.Count}종)");

        int attempts = 0;

        while (!token.IsCancellationRequested && attempts < maxAttempts)
        {
            try
            {
                if (IsHotkeyPressed())
                {
                    Log("Ctrl+Alt 감지 — 리롤 중지");
                    Stopped?.Invoke();
                    return;
                }

                hWnd = GameWindowHelper.FindGameWindow();
                if (hWnd == IntPtr.Zero) { await Task.Delay(2000, token); continue; }

                // 사냥꾼 버튼 클릭
                var (ox, oy) = GameWindowHelper.GetClientOrigin(hWnd);
                var (cw, ch) = GameWindowHelper.GetClientSize(hWnd);
                int screenX = ox + layout.HunterBtn.X;
                int screenY = oy + layout.HunterBtn.Y;
                if (attempts == 0)
                {
                    Log($"클릭좌표: client({layout.HunterBtn.X},{layout.HunterBtn.Y}) origin({ox},{oy}) screen({screenX},{screenY}) clientSize({cw},{ch})");
                }
                GameWindowHelper.SendClickRelative(hWnd, layout.HunterBtn.X, layout.HunterBtn.Y);
                attempts++;

                await Task.Delay(delay, token);

                using var bitmap = GameWindowHelper.CaptureClient(hWnd);
                if (bitmap == null) continue;

                // 모든 능력치 읽기
                int statCount = Math.Min(6, layout.StatRois.Length / 2);
                var readValues = new int[statCount];
                var vals = new List<string>();
                int totalSum = 0;
                for (int i = 0; i < statCount; i++)
                {
                    readValues[i] = ReadTwoDigits(bitmap, layout.StatRois[i * 2], layout.StatRois[i * 2 + 1]);
                    vals.Add($"{names[i]}={readValues[i]}");
                    if (readValues[i] > 0) totalSum += readValues[i];
                }
                vals.Add($"합계={totalSum}");

                Progress?.Invoke(readValues.Length > 1 ? readValues[1] : -1, attempts);
                Log($"[{attempts}회] {string.Join(", ", vals)}");

                // 목표 달성 조건: 지정된 목표(>0) 모두 충족
                // 인식 실패(-1)인 항목은 미충족 처리
                bool hasTarget = false;
                bool allMet = true;
                for (int i = 0; i < Math.Min(targets.Length, statCount); i++)
                {
                    if (targets[i] <= 0) continue;
                    hasTarget = true;
                    if (readValues[i] < targets[i]) { allMet = false; break; }
                }

                if (hasTarget && allMet)
                {
                    Log($"[{attempts}회] 목표 달성!");
                    Completed?.Invoke(readValues.Length > 1 ? readValues[1] : 0, attempts);
                    return;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log($"오류: {ex.Message}");
                await Task.Delay(2000, token);
            }
        }

        if (attempts >= maxAttempts)
        {
            Log($"최대 시도 횟수({maxAttempts}회) 도달 — 중지");
            Stopped?.Invoke();
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
        if (_digitTemplates.Count == 0) return -1;

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

    public void ClearTemplates()
    {
        foreach (var (_, m) in _digitTemplates) m.Dispose();
        _digitTemplates.Clear();

        if (Directory.Exists(_numberTemplateDir))
        {
            foreach (var f in Directory.GetFiles(_numberTemplateDir, "*.png"))
                File.Delete(f);
        }
        Log("템플릿 초기화 완료");
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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt

    private bool IsHotkeyPressed()
        => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0
        && (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private static Mat? CaptureFullScreen()
    {
        try
        {
            int w = GetSystemMetrics(0); // SM_CXSCREEN
            int h = GetSystemMetrics(1); // SM_CYSCREEN
            using var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(0, 0, 0, 0, new System.Drawing.Size(w, h));
            return BitmapConverter.ToMat(bmp);
        }
        catch { return null; }
    }

    /// <summary>가까운 Rect들을 하나로 합친다.</summary>
    private static List<Rect> MergeCloseRects(List<Rect> rects, int gap)
    {
        if (rects.Count == 0) return rects;
        var sorted = rects.OrderBy(r => r.Y).ThenBy(r => r.X).ToList();
        var result = new List<Rect> { sorted[0] };

        for (int i = 1; i < sorted.Count; i++)
        {
            var cur = sorted[i];
            bool merged = false;
            for (int j = 0; j < result.Count; j++)
            {
                var prev = result[j];
                // 두 rect가 gap 이내로 가까우면 합침
                if (cur.X <= prev.X + prev.Width + gap &&
                    cur.X + cur.Width >= prev.X - gap &&
                    cur.Y <= prev.Y + prev.Height + gap &&
                    cur.Y + cur.Height >= prev.Y - gap)
                {
                    int x1 = Math.Min(prev.X, cur.X);
                    int y1 = Math.Min(prev.Y, cur.Y);
                    int x2 = Math.Max(prev.X + prev.Width, cur.X + cur.Width);
                    int y2 = Math.Max(prev.Y + prev.Height, cur.Y + cur.Height);
                    result[j] = new Rect(x1, y1, x2 - x1, y2 - y1);
                    merged = true;
                    break;
                }
            }
            if (!merged) result.Add(cur);
        }
        return result;
    }

    private static Rect ClampRect(Rect r, int maxW, int maxH)
    {
        int x = Math.Clamp(r.X, 0, maxW - 1);
        int y = Math.Clamp(r.Y, 0, maxH - 1);
        int w = Math.Min(r.Width, maxW - x);
        int h = Math.Min(r.Height, maxH - y);
        return new Rect(x, y, Math.Max(0, w), Math.Max(0, h));
    }

    private void Log(string msg) => LogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");

    /// <summary>contour 후보를 원본 위에 표시한 디버그 이미지를 exe 디렉토리에 저장.</summary>
    private void SaveContourDebugImage(Mat gray, Rect dlg, List<Rect> rects, string prefix)
    {
        try
        {
            using var debug = new Mat();
            Cv2.CvtColor(gray, debug, ColorConversionCodes.GRAY2BGR);
            Cv2.Rectangle(debug, dlg, new Scalar(0, 255, 0), 1); // 다이얼로그: 녹색
            foreach (var r in rects)
                Cv2.Rectangle(debug, r, new Scalar(0, 0, 255), 1); // 후보: 빨간색

            var dir = AppDomain.CurrentDomain.BaseDirectory;
            var path = Path.Combine(dir, $"{prefix}_{DateTime.Now:HHmmss}.png");
            bool ok = Cv2.ImWrite(path, debug);
            Log($"디버그 이미지: {path} (저장={ok}, {rects.Count}개 후보)");
        }
        catch (Exception ex)
        {
            Log($"디버그 이미지 저장 실패: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _cts?.Cancel(); _cts?.Dispose();
        foreach (var (_, m) in _digitTemplates) m.Dispose();
        _digitTemplates.Clear();
    }

    #endregion
}
