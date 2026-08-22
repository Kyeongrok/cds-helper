using System.IO;
using System.Text.Json;

namespace CdsHelper.Support.Local.Settings;

public class ViewOption
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public class AppSettingsData
{
    public double MarkerSize { get; set; } = AppSettings.DefaultMarkerSize;
    public string DefaultView { get; set; } = AppSettings.DefaultDefaultView;
    public string? LastSaveFilePath { get; set; }
    public string? TrailDirectory { get; set; }
    public HashSet<int> CheckedDiscoveryIds { get; set; } = new();
    public WorldMapOptions WorldMap { get; set; } = new();
    public bool AutoConfirmDialog { get; set; } = true;

    /// <summary>앱을 켤 때 함대 보기(Direct3D) 창을 바로 띄울지. 기본은 켬.</summary>
    public bool AutoOpenShipMap { get; set; } = true;

    /// <summary>함대 창에서 배경음악을 틀지. 기본은 켬.</summary>
    public bool BgmEnabled { get; set; } = true;

    /// <summary>효과음(닻·거절 따위)을 낼지. 기본은 켬.</summary>
    public bool SfxEnabled { get; set; } = true;

    /// <summary>게임 창 단추의 좌우 여백(점). 띠 마구리가 앉을 자리다.</summary>
    public int BandPad { get; set; } = AppSettings.DefaultBandPad;

    /// <summary>도시 창이 열릴 때 줄 효과. <see cref="CityOpenEffect"/> 의 이름이다.</summary>
    public string CityOpenEffect { get; set; } = "Expand";

    /// <summary>지도 위에 좌표 상자를 겹쳐 보일지. 개발 창에서 켜고 끈다.</summary>
    public bool ShowCoordOverlay { get; set; } = true;

    /// <summary>지도 위의 까만 조작 줄을 보일지. 개발 창에서 켜고 끈다.</summary>
    public bool ShowToolBar { get; set; } = true;

    /// <summary>지도 위에 바람·해류 화살표를 얹을지. 함대 창 커맨드에서 켜고 끈다.</summary>
    public bool ShowFlowArrows { get; set; }
    public RerollOptions Reroll { get; set; } = new();
}

/// <summary>능력치 리롤 옵션.</summary>
public class RerollOptions
{
    /// <summary>true면 자동 감지한 사냥꾼 버튼 대신 사용자가 지정한 좌표를 클릭한다.</summary>
    public bool UseCustomClickPoint { get; set; }

    /// <summary>사용자 지정 클릭 좌표 (게임 클라이언트 기준).</summary>
    public int ClickX { get; set; }

    public int ClickY { get; set; }
}

public class WorldMapOptions
{
    public bool ShowCoast { get; set; } = true;
    public bool ShowWind { get; set; } = false;
    public bool ShowDiscoveries { get; set; } = true;
    public bool ShowCityLabels { get; set; } = false;
    public bool HideFound { get; set; } = false;
    public bool ShowSpeed { get; set; } = false;
    public double Zoom { get; set; } = 2.0;
    /// <summary>
    /// 자동 스크롤 임계 비율 (0.0~1.0). 마커가 뷰포트 중심에서
    /// (viewport_half × 이 비율)만큼 벗어나면 재중앙 정렬한다.
    /// 0.5 = 뷰포트 중앙 50% 안전영역 (기본), 1.0 = 가장자리까지 가야 스크롤.
    /// </summary>
    public double AutoScrollThreshold { get; set; } = 0.5;

    /// <summary>수동 좌표추적 주기 (초). 매 N초마다 화면 캡처 + OCR.</summary>
    public double TrackingIntervalSeconds { get; set; } = 2.0;
}

public static class AppSettings
{
    public const double DefaultMarkerSize = 11.0;

    /// <summary>
    /// 게임 창 단추의 좌우 여백 기본값(점).
    /// </summary>
    /// <remarks>
    /// 띠 마구리는 실제로 16점이지만 그만큼 다 비우면 창이 훨씬 넓어 보인다 — 게임 창은
    /// 글자가 마구리 무늬에 꽤 가깝게 붙는다. 눈으로 맞춰 3 으로 잡았다.
    /// </remarks>
    public const int DefaultBandPad = 3;

    /// <summary>단추 여백을 이 사이로만 잡는다.</summary>
    public const int MinBandPad = 0, MaxBandPad = 32;
    public const string DefaultDefaultView = "PlayerContent";

    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CdsHelper",
        "settings.json");

    /// <summary>
    /// 세계지도 색상 팔레트 JSON 파일 경로 (%APPDATA%\CdsHelper\map_palette.json).
    /// 사용자가 직접 수정 후 세계지도에서 "팔레트 다시 불러오기" 버튼으로 반영.
    /// </summary>
    public static string MapPaletteFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CdsHelper",
        "map_palette.json");

    /// <summary>
    /// 발견물 마스터 JSON 파일 경로 (%APPDATA%\CdsHelper\발견물.json).
    /// CSV를 대체하는 source of truth. 좌표/이름 편집이 즉시 저장되며 앱 업데이트로 install 폴더가 갈려도 보존된다.
    /// </summary>
    public static string DiscoveryFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CdsHelper",
        "발견물.json");

    public static event Action? SettingsChanged;

    private static double _markerSize = DefaultMarkerSize;
    private static string _defaultView = DefaultDefaultView;
    private static string? _lastSaveFilePath;
    private static HashSet<int> _checkedDiscoveryIds = new();
    private static string _trailDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "trails");
    private static WorldMapOptions _worldMap = new();
    private static bool _autoConfirmDialog = true;
    private static bool _autoOpenShipMap = true;
    private static bool _bgmEnabled = true;
    private static CityOpenEffect _cityOpenEffect = Settings.CityOpenEffect.Expand;
    private static bool _showCoordOverlay = true;
    private static bool _showToolBar = true;
    private static bool _showFlowArrows;
    private static bool _sfxEnabled = true;
    private static int _bandPad = DefaultBandPad;
    private static RerollOptions _reroll = new();

    static AppSettings()
    {
        LoadSettings();
    }

    public static double MarkerSize
    {
        get => _markerSize;
        set
        {
            _markerSize = Math.Clamp(value, 4.0, 20.0);
            SaveSettings();
            SettingsChanged?.Invoke();
        }
    }

    public static string DefaultView
    {
        get => _defaultView;
        set
        {
            _defaultView = value;
            SaveSettings();
            SettingsChanged?.Invoke();
        }
    }

    public static string? LastSaveFilePath
    {
        get => _lastSaveFilePath;
        set
        {
            _lastSaveFilePath = value;
            SaveSettings();
        }
    }

    public static string TrailDirectory
    {
        get => _trailDirectory;
        set
        {
            _trailDirectory = value;
            SaveSettings();
        }
    }

    public static HashSet<int> CheckedDiscoveryIds => _checkedDiscoveryIds;

    public static void SetDiscoveryChecked(int discoveryId, bool isChecked)
    {
        if (isChecked)
        {
            _checkedDiscoveryIds.Add(discoveryId);
        }
        else
        {
            _checkedDiscoveryIds.Remove(discoveryId);
        }
        SaveSettings();
    }

    public static bool IsDiscoveryChecked(int discoveryId)
    {
        return _checkedDiscoveryIds.Contains(discoveryId);
    }

    public static WorldMapOptions WorldMap => _worldMap;

    public static void SaveWorldMapOptions()
    {
        SaveSettings();
    }

    public static RerollOptions Reroll => _reroll;

    public static void SaveRerollOptions()
    {
        SaveSettings();
    }

    /// <summary>
    /// 자동 항해 중 대화창 "확인" 버튼을 OCR/템플릿 매칭으로 자동 클릭할지 여부.
    /// 끄면 다이얼로그가 떠도 클릭하지 않고 사용자가 수동으로 처리하게 둔다.
    /// </summary>
    public static bool AutoConfirmDialog
    {
        get => _autoConfirmDialog;
        set
        {
            _autoConfirmDialog = value;
            SaveSettings();
            SettingsChanged?.Invoke();
        }
    }

    /// <summary>
    /// 앱을 켤 때 함대 보기(Direct3D) 창을 바로 띄울지. 기본은 <b>켬</b>이다 — 이 앱이
    /// 하는 일이 곧 그 창이라, 켤 때마다 메뉴에서 한 번 더 누르게 할 까닭이 없다.
    /// 이미 설정을 적어 둔 사람은 적힌 값을 그대로 따른다(없는 칸만 이 기본값이 든다).
    /// </summary>
    public static bool AutoOpenShipMap
    {
        get => _autoOpenShipMap;
        set
        {
            _autoOpenShipMap = value;
            SaveSettings();
            SettingsChanged?.Invoke();
        }
    }

    /// <summary>함대 창의 배경음악을 틀지. 설정 창에서 켜고 끈다.</summary>
    public static bool BgmEnabled
    {
        get => _bgmEnabled;
        set
        {
            _bgmEnabled = value;
            SaveSettings();
            SettingsChanged?.Invoke();
        }
    }

    /// <summary>효과음을 낼지. 설정 창에서 켜고 끈다. 배경음악과 따로 논다.</summary>
    public static bool SfxEnabled
    {
        get => _sfxEnabled;
        set
        {
            _sfxEnabled = value;
            SaveSettings();
            SettingsChanged?.Invoke();
        }
    }

    /// <summary>
    /// 게임 창 단추의 좌우 여백(점). 개발 창에서 맞춘다.
    /// </summary>
    /// <remarks>
    /// 띠는 왼끝·가운데·오른끝 셋으로 짓고 양 끝(마구리)이 16점씩이다. 이 값만큼을 글자
    /// 바깥에 비워 두므로, 16 이면 마구리가 통째로 글자 밖에 서고 그보다 작으면 글자가
    /// 마구리 위로 조금씩 올라앉는다. 바꾼 값은 <b>다음에 여는 창</b>부터 든다.
    /// </remarks>
    public static int BandPad
    {
        get => _bandPad;
        set
        {
            _bandPad = Math.Clamp(value, MinBandPad, MaxBandPad);
            SaveSettings();
            SettingsChanged?.Invoke();
        }
    }

    /// <summary>도시 창이 열릴 때 줄 효과. 개발 창에서 고른다.</summary>
    public static CityOpenEffect CityOpenEffect
    {
        get => _cityOpenEffect;
        set
        {
            _cityOpenEffect = value;
            SaveSettings();
            SettingsChanged?.Invoke();
        }
    }

    /// <summary>지도 위 좌표 상자를 켜 둘지. 개발 창에서 바꾸면 다음에 켤 때도 그대로다.</summary>
    public static bool ShowCoordOverlay
    {
        get => _showCoordOverlay;
        set
        {
            _showCoordOverlay = value;
            SaveSettings();
            SettingsChanged?.Invoke();
        }
    }

    /// <summary>지도 위 까만 조작 줄을 켜 둘지.</summary>
    public static bool ShowToolBar
    {
        get => _showToolBar;
        set
        {
            _showToolBar = value;
            SaveSettings();
            SettingsChanged?.Invoke();
        }
    }

    /// <summary>
    /// 지도 위 바람·해류 화살표를 켜 둘지. 게임에는 없는 것이라 기본은 끔이다 —
    /// 원본은 화살표 대신 물결이 흐르는 것으로 해류를 보인다.
    /// </summary>
    public static bool ShowFlowArrows
    {
        get => _showFlowArrows;
        set
        {
            _showFlowArrows = value;
            SaveSettings();
            SettingsChanged?.Invoke();
        }
    }

    public static readonly List<ViewOption> AvailableViews = new()
    {
        new() { Name = "PlayerContent", DisplayName = "플레이어" },
        new() { Name = "CharacterContent", DisplayName = "항해사" },
        new() { Name = "BookContent", DisplayName = "도서" },
        new() { Name = "CityContent", DisplayName = "도시" },
        new() { Name = "PatronContent", DisplayName = "후원자" },
        new() { Name = "FigureheadContent", DisplayName = "선수상" },
        new() { Name = "ItemContent", DisplayName = "아이템" },
        new() { Name = "MapContent", DisplayName = "지도" },
        new() { Name = "WorldMapContent", DisplayName = "세계지도" },
        new() { Name = "SphinxCalculatorContent", DisplayName = "스핑크스" }
    };

    private static void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var data = JsonSerializer.Deserialize<AppSettingsData>(json);
                if (data != null)
                {
                    _markerSize = data.MarkerSize;
                    _defaultView = data.DefaultView;
                    _lastSaveFilePath = data.LastSaveFilePath;
                    if (!string.IsNullOrEmpty(data.TrailDirectory))
                        _trailDirectory = data.TrailDirectory;
                    _checkedDiscoveryIds = data.CheckedDiscoveryIds ?? new();
                    _worldMap = data.WorldMap ?? new();
                    _autoConfirmDialog = data.AutoConfirmDialog;
                    _autoOpenShipMap = data.AutoOpenShipMap;
                    _bgmEnabled = data.BgmEnabled;
                    _sfxEnabled = data.SfxEnabled;
                    _bandPad = Math.Clamp(data.BandPad, MinBandPad, MaxBandPad);
                    _cityOpenEffect = Enum.TryParse<CityOpenEffect>(data.CityOpenEffect, out var eff)
                        ? eff : Settings.CityOpenEffect.Expand;
                    _showCoordOverlay = data.ShowCoordOverlay;
                    _showToolBar = data.ShowToolBar;
                    _showFlowArrows = data.ShowFlowArrows;
                    _reroll = data.Reroll ?? new();
                }
            }
        }
        catch
        {
            // 설정 로드 실패 시 기본값 사용
        }
    }

    private static void SaveSettings()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var data = new AppSettingsData
            {
                MarkerSize = _markerSize,
                DefaultView = _defaultView,
                LastSaveFilePath = _lastSaveFilePath,
                TrailDirectory = _trailDirectory,
                CheckedDiscoveryIds = _checkedDiscoveryIds,
                WorldMap = _worldMap,
                AutoConfirmDialog = _autoConfirmDialog,
                AutoOpenShipMap = _autoOpenShipMap,
                BgmEnabled = _bgmEnabled,
                SfxEnabled = _sfxEnabled,
                BandPad = _bandPad,
                CityOpenEffect = _cityOpenEffect.ToString(),
                ShowCoordOverlay = _showCoordOverlay,
                ShowToolBar = _showToolBar,
                ShowFlowArrows = _showFlowArrows,
                Reroll = _reroll
            };

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // 설정 저장 실패 시 무시
        }
    }
}
