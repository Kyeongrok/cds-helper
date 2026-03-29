using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.RegularExpressions;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using TorchSharp;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using static TorchSharp.torch;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 좌표 인식 서비스: 데이터 수집 → 학습 → 추론 전체 파이프라인.
/// 상단바를 통째로 CNN에 넣어서 모델이 좌표 위치와 값을 직접 학습.
/// </summary>
public class CoordinateOcrService : IDisposable
{
    private CoordinateNet? _model;
    private Device? _device;
    private readonly string _dataDir;
    private readonly string _imageDir;
    private readonly string _labelPath;
    private readonly string _modelPath;

    private CancellationTokenSource? _collectCts;
    private Task? _collectTask;
    private int _collectCount;

    /// <summary>상단바 크롭 높이 (픽셀). 게임 해상도에 따라 조정.</summary>
    public int TopBarHeight { get; set; } = 30;

    /// <summary>모델 로드 완료 여부.</summary>
    public bool IsModelLoaded => _model != null;

    /// <summary>데이터 수집 중 여부.</summary>
    public bool IsCollecting => _collectTask is { IsCompleted: false };

    /// <summary>데이터 폴더 경로.</summary>
    public string DataDirectory => _dataDir;

    public event Action<string>? LogMessage;

    /// <summary>TorchSharp 디바이스를 지연 초기화.</summary>
    private Device GetDevice()
    {
        if (_device != null) return _device;

        try
        {
            _device = cuda.is_available() ? CUDA : CPU;
        }
        catch (Exception ex)
        {
            Log($"CUDA 초기화 실패, CPU 사용: {ex.Message}");
            _device = CPU;
        }

        Log($"TorchSharp 디바이스: {_device}");
        return _device;
    }

    public CoordinateOcrService(string? baseDir = null)
    {
        var root = baseDir
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "git", "ml", "cds-ai", "assets");

        _dataDir = Path.Combine(root, "coordinate_data");
        _imageDir = Path.Combine(_dataDir, "images");
        _labelPath = Path.Combine(_dataDir, "labels.csv");
        _modelPath = Path.Combine(_dataDir, "model", "coordinate_net.pt");

        Directory.CreateDirectory(_imageDir);
        Directory.CreateDirectory(Path.GetDirectoryName(_modelPath)!);

        Log("CoordinateOcrService 초기화 완료");
    }

    #region 상단바 크롭

    /// <summary>게임 화면에서 상단바를 크롭. 좌표는 항상 상단바에 있으므로 고정 높이로 자름.</summary>
    public Bitmap? CropTopBar(Bitmap screenshot)
    {
        var h = Math.Min(TopBarHeight, screenshot.Height);
        if (h <= 0) return null;
        return screenshot.Clone(new Rectangle(0, 0, screenshot.Width, h), screenshot.PixelFormat);
    }

    /// <summary>전체 스크린샷 + 상단바 미리보기 저장.</summary>
    public string? CapturePreview()
    {
        var hWnd = GameWindowHelper.FindGameWindow();
        if (hWnd == IntPtr.Zero)
        {
            Log("게임 윈도우를 찾을 수 없습니다.");
            return null;
        }

        GameWindowHelper.BringToFront(hWnd);
        Thread.Sleep(300);

        using var bitmap = GameWindowHelper.CaptureClient(hWnd);
        if (bitmap == null) return null;

        var (cw, ch) = GameWindowHelper.GetClientSize(hWnd);
        Log($"클라이언트: {cw}x{ch}, 캡처: {bitmap.Width}x{bitmap.Height}");

        // 전체 스크린샷 저장
        var fullPath = Path.Combine(_dataDir, "full_screenshot.png");
        bitmap.Save(fullPath, ImageFormat.Png);

        // 상단바 크롭 저장
        var topBar = CropTopBar(bitmap);
        if (topBar != null)
        {
            var topBarPath = Path.Combine(_dataDir, "topbar_preview.png");
            topBar.Save(topBarPath, ImageFormat.Png);
            topBar.Dispose();
            Log($"상단바 미리보기 저장 (높이 {TopBarHeight}px): {topBarPath}");
        }

        Log($"전체 스크린샷 저장: {fullPath}");
        return fullPath;
    }

    #endregion

    #region 데이터 수집

    /// <summary>데이터 수집 시작. 1초 간격으로 상단바 크롭 저장.</summary>
    public void StartCollecting()
    {
        if (IsCollecting) return;

        _collectCts = new CancellationTokenSource();
        _collectCount = Directory.GetFiles(_imageDir, "*.png").Length;
        _collectTask = Task.Run(() => CollectLoop(_collectCts.Token));
        Log("데이터 수집 시작");
    }

    /// <summary>데이터 수집 중지.</summary>
    public void StopCollecting()
    {
        _collectCts?.Cancel();
        Log($"데이터 수집 중지 (총 {_collectCount}장)");
    }

    private async Task CollectLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var hWnd = GameWindowHelper.FindGameWindow();
                if (hWnd == IntPtr.Zero)
                {
                    await Task.Delay(2000, token);
                    continue;
                }

                using var bitmap = GameWindowHelper.CaptureClient(hWnd);
                if (bitmap == null)
                {
                    await Task.Delay(1000, token);
                    continue;
                }

                var topBar = CropTopBar(bitmap);
                if (topBar != null)
                {
                    var filename = $"coord_{_collectCount:D5}.png";
                    var path = Path.Combine(_imageDir, filename);
                    topBar.Save(path, ImageFormat.Png);
                    topBar.Dispose();
                    _collectCount++;

                    if (_collectCount % 10 == 0)
                        Log($"수집 중... {_collectCount}장");
                }

                await Task.Delay(1000, token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Log($"수집 오류: {ex.Message}"); }
        }
    }

    #endregion

    #region 라벨링

    /// <summary>수동 라벨 추가.</summary>
    public void AddLabel(string imageFilename, int latDir, int latVal, int lonDir, int lonVal)
    {
        var header = "filename,lat_dir,lat_val,lon_dir,lon_val";
        if (!File.Exists(_labelPath))
            File.WriteAllText(_labelPath, header + Environment.NewLine);

        var line = $"{imageFilename},{latDir},{latVal},{lonDir},{lonVal}";
        File.AppendAllText(_labelPath, line + Environment.NewLine);
        Log($"라벨 추가: {line}");
    }

    /// <summary>
    /// Windows OCR로 수집된 이미지를 자동 라벨링.
    /// 상단바에서 "북위 38 서경 10" 같은 텍스트를 읽어서 labels.csv에 저장.
    /// </summary>
    public async Task<int> AutoLabelAsync()
    {
        var ocrEngine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("ko"));
        if (ocrEngine == null)
        {
            Log("Windows OCR 한국어 엔진을 생성할 수 없습니다. 한국어 언어팩을 설치하세요.");
            return 0;
        }

        var images = Directory.GetFiles(_imageDir, "*.png");
        var existing = LoadExistingLabels();
        var labeled = 0;
        var failed = 0;

        foreach (var imgPath in images)
        {
            var filename = Path.GetFileName(imgPath);
            if (existing.Contains(filename)) continue;

            var result = await OcrFromFileAsync(ocrEngine, imgPath);
            if (result == null)
            {
                failed++;
                continue;
            }

            AddLabel(filename, result.Value.latDir, result.Value.latVal, result.Value.lonDir, result.Value.lonVal);
            labeled++;
        }

        Log($"자동 라벨링 완료: 성공 {labeled}장, 실패 {failed}장");
        return labeled;
    }

    private async Task<(int latDir, int latVal, int lonDir, int lonVal)?> OcrFromFileAsync(
        OcrEngine engine, string imagePath)
    {
        try
        {
            // 이미지가 작으면 OCR 인식이 안 되므로 3배 확대
            using var mat = Cv2.ImRead(imagePath, ImreadModes.Color);
            if (mat.Empty()) return null;

            using var scaled = new Mat();
            Cv2.Resize(mat, scaled, new OpenCvSharp.Size(mat.Cols * 3, mat.Rows * 3), interpolation: InterpolationFlags.Cubic);

            var scaledPath = Path.Combine(_dataDir, "_ocr_temp.png");
            Cv2.ImWrite(scaledPath, scaled);

            using var stream = File.OpenRead(scaledPath);
            using var ras = stream.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(ras);
            var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

            // Windows OCR은 Bgra8 + Premultiplied 포맷만 지원
            if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8
                || softwareBitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
            {
                softwareBitmap = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            }

            var ocrResult = await engine.RecognizeAsync(softwareBitmap);
            softwareBitmap.Dispose();
            var text = ocrResult.Text;

            var parsed = ParseCoordinateText(text);
            if (parsed == null)
                Log($"  OCR 파싱 실패: \"{text}\" ← {Path.GetFileName(imagePath)}");

            return parsed;
        }
        catch (Exception ex)
        {
            Log($"  OCR 오류: {ex.Message} ← {Path.GetFileName(imagePath)}");
            return null;
        }
    }

    /// <summary>
    /// OCR 텍스트에서 좌표를 파싱.
    /// 예: "북위 38 서경 10" → (0, 38, 1, 10)
    /// </summary>
    private static (int latDir, int latVal, int lonDir, int lonVal)? ParseCoordinateText(string text)
    {
        // "북위" 또는 "남위" + 숫자
        var latMatch = Regex.Match(text, @"(북위|남위)\s*(\d+)");
        if (!latMatch.Success) return null;

        // "동경" 또는 "서경" + 숫자
        var lonMatch = Regex.Match(text, @"(동경|서경)\s*(\d+)");
        if (!lonMatch.Success) return null;

        var latDir = latMatch.Groups[1].Value == "남위" ? 1 : 0;
        var latVal = int.Parse(latMatch.Groups[2].Value);
        var lonDir = lonMatch.Groups[1].Value == "서경" ? 1 : 0;
        var lonVal = int.Parse(lonMatch.Groups[2].Value);

        if (latVal > 90 || lonVal > 180) return null;

        return (latDir, latVal, lonDir, lonVal);
    }

    private HashSet<string> LoadExistingLabels()
    {
        var set = new HashSet<string>();
        if (!File.Exists(_labelPath)) return set;

        foreach (var line in File.ReadLines(_labelPath).Skip(1))
        {
            var parts = line.Split(',');
            if (parts.Length >= 1)
                set.Add(parts[0]);
        }

        return set;
    }

    #endregion

    #region 학습

    /// <summary>라벨링된 데이터로 모델 학습.</summary>
    public void Train(int epochs = 50, int batchSize = 16, double learningRate = 0.001)
    {
        var labels = LoadLabels();
        if (labels.Count == 0)
        {
            Log("학습 데이터가 없습니다. 먼저 라벨링을 수행하세요.");
            return;
        }

        var device = GetDevice();
        Log($"학습 시작: {labels.Count}개 데이터, {epochs} 에포크, 디바이스: {device}");

        var model = new CoordinateNet(device);
        model.train();

        var optimizer = optim.Adam(model.parameters(), lr: learningRate);
        var ceLoss = nn.CrossEntropyLoss();

        for (var epoch = 0; epoch < epochs; epoch++)
        {
            var shuffled = labels.OrderBy(_ => Random.Shared.Next()).ToList();
            var totalLoss = 0.0;
            var correct = 0;
            var total = 0;

            for (var i = 0; i < shuffled.Count; i += batchSize)
            {
                var batch = shuffled.Skip(i).Take(batchSize).ToList();
                if (batch.Count == 0) break;

                using var scope = NewDisposeScope();

                var images = new List<Tensor>();
                var latDirTargets = new List<long>();
                var latValTargets = new List<long>();
                var lonDirTargets = new List<long>();
                var lonValTargets = new List<long>();

                foreach (var label in batch)
                {
                    var tensor = LoadImageAsTensor(label.ImagePath);
                    if (tensor is null) continue;

                    images.Add(tensor!);
                    latDirTargets.Add(label.LatDir);
                    latValTargets.Add(label.LatValue);
                    lonDirTargets.Add(label.LonDir);
                    lonValTargets.Add(label.LonValue);
                }

                if (images.Count == 0) continue;

                var xBatch = cat(images.Select(t => t).ToArray(), dim: 0).to(device);
                var yLatDir = tensor(latDirTargets.ToArray(), dtype: int64).to(device);
                var yLatVal = tensor(latValTargets.ToArray(), dtype: int64).to(device);
                var yLonDir = tensor(lonDirTargets.ToArray(), dtype: int64).to(device);
                var yLonVal = tensor(lonValTargets.ToArray(), dtype: int64).to(device);

                var output = model.forward(xBatch);
                var (predLd, predLv, predOd, predOv) = CoordinateNet.SplitOutput(output);

                var loss = ceLoss.forward(predLd, yLatDir)
                         + ceLoss.forward(predLv, yLatVal)
                         + ceLoss.forward(predOd, yLonDir)
                         + ceLoss.forward(predOv, yLonVal);

                optimizer.zero_grad();
                loss.backward();
                optimizer.step();

                totalLoss += loss.item<float>() * batch.Count;

                var latValPred = predLv.argmax(1);
                var lonValPred = predOv.argmax(1);
                correct += (int)(latValPred.eq(yLatVal).sum().item<long>()
                              + lonValPred.eq(yLonVal).sum().item<long>());
                total += images.Count * 2;
            }

            var avgLoss = totalLoss / labels.Count;
            var accuracy = total > 0 ? (double)correct / total * 100 : 0;

            if ((epoch + 1) % 5 == 0 || epoch == 0)
                Log($"에포크 {epoch + 1}/{epochs} — 손실: {avgLoss:F4}, 정확도: {accuracy:F1}%");
        }

        model.save(_modelPath);
        _model = model;
        Log($"모델 저장 완료: {_modelPath}");
    }

    /// <summary>저장된 모델 로드.</summary>
    public bool LoadModel()
    {
        if (!File.Exists(_modelPath))
            return false;

        try
        {
            var device = GetDevice();
            _model = new CoordinateNet(device);
            _model.load(_modelPath);
            _model.eval();
            Log($"모델 로드 완료: {_modelPath} (디바이스: {device})");
            return true;
        }
        catch (Exception ex)
        {
            Log($"모델 로드 실패: {ex.Message}");
            return false;
        }
    }

    private List<CoordinateLabel> LoadLabels()
    {
        var labels = new List<CoordinateLabel>();
        if (!File.Exists(_labelPath)) return labels;

        foreach (var line in File.ReadLines(_labelPath).Skip(1))
        {
            var parts = line.Trim().Split(',');
            if (parts.Length < 5) continue;

            var imgPath = Path.Combine(_imageDir, parts[0]);
            if (!File.Exists(imgPath)) continue;

            labels.Add(new CoordinateLabel(
                imgPath,
                int.Parse(parts[1]),
                int.Parse(parts[2]),
                int.Parse(parts[3]),
                int.Parse(parts[4])
            ));
        }

        return labels;
    }

    private Tensor? LoadImageAsTensor(string imagePath)
    {
        using var mat = Cv2.ImRead(imagePath, ImreadModes.Grayscale);
        if (mat.Empty()) return null;
        return MatToTensor(mat);
    }

    /// <summary>OpenCV Mat → TorchSharp Tensor [1, 1, H, W] (0~1 정규화).</summary>
    private Tensor MatToTensor(Mat grayMat)
    {
        using var resized = new Mat();
        Cv2.Resize(grayMat, resized, new OpenCvSharp.Size(CoordinateNet.InputWidth, CoordinateNet.InputHeight));

        var h = resized.Rows;
        var w = resized.Cols;
        var data = new float[h * w];

        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            data[y * w + x] = resized.At<byte>(y, x) / 255f;

        return tensor(data, [1, 1, h, w]);
    }

    #endregion

    #region 추론

    private OcrEngine? _ocrEngine;

    /// <summary>Windows OCR 엔진 초기화.</summary>
    private OcrEngine? GetOcrEngine()
    {
        if (_ocrEngine != null) return _ocrEngine;
        _ocrEngine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("ko"));
        if (_ocrEngine == null)
            Log("Windows OCR 한국어 엔진 생성 실패");
        return _ocrEngine;
    }

    /// <summary>게임 화면 Bitmap에서 Windows OCR로 좌표를 인식.</summary>
    public async Task<CoordinatePrediction?> PredictOcrAsync(Bitmap screenshot)
    {
        var engine = GetOcrEngine();
        if (engine == null) return null;

        var topBar = CropTopBar(screenshot);
        if (topBar == null) return null;

        try
        {
            // 3배 확대 (OCR 정확도 향상)
            using var mat = BitmapConverter.ToMat(topBar);
            topBar.Dispose();
            using var scaled = new Mat();
            Cv2.Resize(mat, scaled, new OpenCvSharp.Size(mat.Cols * 3, mat.Rows * 3),
                interpolation: InterpolationFlags.Cubic);

            // Mat → SoftwareBitmap
            using var scaledBmp = BitmapConverter.ToBitmap(scaled);
            using var ms = new MemoryStream();
            scaledBmp.Save(ms, ImageFormat.Png);
            ms.Position = 0;

            using var ras = ms.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(ras);
            var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

            if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8
                || softwareBitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
            {
                softwareBitmap = SoftwareBitmap.Convert(softwareBitmap,
                    BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            }

            var ocrResult = await engine.RecognizeAsync(softwareBitmap);
            softwareBitmap.Dispose();

            var parsed = ParseCoordinateText(ocrResult.Text);
            if (parsed == null) return null;

            return new CoordinatePrediction(
                IsNorth: parsed.Value.latDir == 0,
                LatValue: parsed.Value.latVal,
                IsEast: parsed.Value.lonDir == 0,
                LonValue: parsed.Value.lonVal
            );
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region 유틸

    /// <summary>현재 수집된 이미지 수.</summary>
    public int GetCollectedCount() => Directory.Exists(_imageDir) ? Directory.GetFiles(_imageDir, "*.png").Length : 0;

    /// <summary>현재 라벨링된 이미지 수.</summary>
    public int GetLabeledCount() => File.Exists(_labelPath) ? File.ReadLines(_labelPath).Count() - 1 : 0;

    private void Log(string message)
    {
        var timestamped = $"[{DateTime.Now:HH:mm:ss}] [좌표OCR] {message}";
        LogMessage?.Invoke(timestamped);
    }

    public void Dispose()
    {
        _collectCts?.Cancel();
        _collectCts?.Dispose();
        _model?.Dispose();
    }

    #endregion
}
