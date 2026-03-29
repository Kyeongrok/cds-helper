using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 게임 화면에서 바다/육지/배를 색상 기반으로 분류.
/// 출력: 2차원 int 배열 (0=육지, 1=바다, 2=배)
/// </summary>
public class SeaMapService : IDisposable
{
    private Mat? _shipTemplate;
    private readonly string _templateDir;

    /// <summary>출력 그리드 크기. 게임 화면을 이 크기로 나눔.</summary>
    public int GridCols { get; set; } = 128;
    public int GridRows { get; set; } = 96;

    /// <summary>상단바 비율 (화면 높이 대비). DPI 스케일링 무관.</summary>
    public double TopBarRatio { get; set; } = 0.05;

    /// <summary>하단바 비율 (화면 높이 대비).</summary>
    public double BottomBarRatio { get; set; } = 0.055;

    /// <summary>BGR에서 B-R 차이가 이 값 이상이면 바다 픽셀.</summary>
    public int SeaBRDiffThreshold { get; set; } = 10;

    /// <summary>바다로 판정할 셀 내 바다 픽셀 비율 임계값.</summary>
    public double SeaThreshold { get; set; } = 0.5;

    public event Action<string>? LogMessage;

    public SeaMapService(string? templateBaseDir = null)
    {
        _templateDir = templateBaseDir
            ?? System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "git", "ml", "cds-ai", "assets", "templates");

        LoadShipTemplate();
    }

    private void LoadShipTemplate()
    {
        var path = System.IO.Path.Combine(_templateDir, "ship", "ship.png");
        if (!System.IO.File.Exists(path)) return;

        _shipTemplate = Cv2.ImRead(path, ImreadModes.Color);
        if (_shipTemplate.Empty())
        {
            _shipTemplate.Dispose();
            _shipTemplate = null;
        }
        else
        {
            Log($"배 템플릿 로드: {_shipTemplate.Cols}x{_shipTemplate.Rows}");
        }
    }

    /// <summary>
    /// 게임 화면을 분석하여 바다/육지/배 그리드를 생성.
    /// </summary>
    /// <returns>int[행,열] — 0=육지, 1=바다, 2=배</returns>
    public int[,] Analyze(Bitmap screenshot)
    {
        using var full = BitmapConverter.ToMat(screenshot);
        return AnalyzeMat(full);
    }

    /// <summary>Mat 기반 분석.</summary>
    public int[,] AnalyzeMat(Mat full)
    {
        var grid = new int[GridRows, GridCols];

        // 게임 영역 (상단바, 하단바 제외)
        var gameTop = (int)(full.Rows * TopBarRatio);
        var gameBottom = (int)(full.Rows * (1.0 - BottomBarRatio));
        var gameHeight = gameBottom - gameTop;

        if (gameHeight <= 0 || full.Cols <= 0)
            return grid;

        using var gameArea = new Mat(full, new Rect(0, gameTop, full.Cols, gameHeight));

        // BGR 채널 분리 → B - R 차이로 바다 판정
        using var seaMask = new Mat(gameArea.Rows, gameArea.Cols, MatType.CV_8UC1);
        var channels = Cv2.Split(gameArea); // B, G, R
        using var b = channels[0];
        using var g = channels[1];
        using var r = channels[2];

        // B - R > threshold → 바다
        // byte 연산이므로 float 변환
        using var bFloat = new Mat();
        using var rFloat = new Mat();
        b.ConvertTo(bFloat, MatType.CV_32F);
        r.ConvertTo(rFloat, MatType.CV_32F);

        using var diff = new Mat();
        Cv2.Subtract(bFloat, rFloat, diff);

        // threshold 적용
        Cv2.Threshold(diff, seaMask, SeaBRDiffThreshold, 255, ThresholdTypes.Binary);
        seaMask.ConvertTo(seaMask, MatType.CV_8UC1);

        // 셀 크기
        var cellW = (double)gameArea.Cols / GridCols;
        var cellH = (double)gameArea.Rows / GridRows;

        // 각 셀의 바다 비율로 분류
        for (var row = 0; row < GridRows; row++)
        for (var col = 0; col < GridCols; col++)
        {
            var x = (int)(col * cellW);
            var y = (int)(row * cellH);
            var w = (int)Math.Min(cellW, gameArea.Cols - x);
            var h = (int)Math.Min(cellH, gameArea.Rows - y);

            if (w <= 0 || h <= 0) continue;

            using var cellMask = new Mat(seaMask, new Rect(x, y, w, h));
            var seaPixels = Cv2.CountNonZero(cellMask);
            var totalPixels = w * h;
            var seaRatio = (double)seaPixels / totalPixels;

            grid[row, col] = seaRatio >= SeaThreshold ? 1 : 0;
        }

        // 배 위치 찾기 (템플릿 매칭)
        var shipPos = FindShip(gameArea);
        if (shipPos.HasValue)
        {
            var shipCol = (int)(shipPos.Value.x / cellW);
            var shipRow = (int)(shipPos.Value.y / cellH);

            if (shipRow >= 0 && shipRow < GridRows && shipCol >= 0 && shipCol < GridCols)
                grid[shipRow, shipCol] = 2;
        }

        return grid;
    }

    /// <summary>배 위치를 찾아 게임 영역 내 좌표 반환.</summary>
    public (int x, int y)? FindShip(Mat gameArea)
    {
        if (_shipTemplate == null) return null;
        if (_shipTemplate.Rows > gameArea.Rows || _shipTemplate.Cols > gameArea.Cols)
            return null;

        using var result = new Mat();
        Cv2.MatchTemplate(gameArea, _shipTemplate, result, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);

        if (maxVal < 0.6) return null;

        // 템플릿 중심 좌표
        var cx = maxLoc.X + _shipTemplate.Cols / 2;
        var cy = maxLoc.Y + _shipTemplate.Rows / 2;
        return (cx, cy);
    }

    /// <summary>배의 화면 절대 좌표 (상단바 포함).</summary>
    public (int x, int y)? FindShipScreen(Bitmap screenshot)
    {
        using var full = BitmapConverter.ToMat(screenshot);
        var gameTop = (int)(full.Rows * TopBarRatio);
        var gameBottom = (int)(full.Rows * (1.0 - BottomBarRatio));
        var gameHeight = gameBottom - gameTop;
        if (gameHeight <= 0) return null;

        using var gameArea = new Mat(full, new Rect(0, gameTop, full.Cols, gameHeight));
        var pos = FindShip(gameArea);
        if (pos == null) return null;

        return (pos.Value.x, pos.Value.y + gameTop);
    }

    /// <summary>디버그용: 그리드를 시각화한 Mat 생성.</summary>
    public Mat VisualizeGrid(int[,] grid)
    {
        var rows = grid.GetLength(0);
        var cols = grid.GetLength(1);
        var cellSize = 6;
        var vis = new Mat(rows * cellSize, cols * cellSize, MatType.CV_8UC3);

        for (var r = 0; r < rows; r++)
        for (var c = 0; c < cols; c++)
        {
            var color = grid[r, c] switch
            {
                0 => new Scalar(60, 130, 180),   // 육지 (갈색 BGR)
                1 => new Scalar(200, 120, 50),    // 바다 (파란 BGR)
                2 => new Scalar(0, 255, 255),     // 배 (노란 BGR)
                _ => new Scalar(0, 0, 0)
            };

            Cv2.Rectangle(vis,
                new Rect(c * cellSize, r * cellSize, cellSize, cellSize),
                color, -1);
        }

        return vis;
    }

    /// <summary>디버그용: 분석 결과를 파일로 저장.</summary>
    public void SaveDebugImage(Bitmap screenshot, string path)
    {
        var grid = Analyze(screenshot);
        using var vis = VisualizeGrid(grid);
        Cv2.ImWrite(path, vis);
    }

    private void Log(string message)
    {
        LogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] [해도] {message}");
    }

    public void Dispose()
    {
        _shipTemplate?.Dispose();
    }
}
