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
}

public static class AppSettings
{
    public const double DefaultMarkerSize = 11.0;
    public const string DefaultDefaultView = "PlayerContent";

    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CdsHelper",
        "settings.json");

    public static event Action? SettingsChanged;

    private static double _markerSize = DefaultMarkerSize;
    private static string _defaultView = DefaultDefaultView;
    private static string? _lastSaveFilePath;
    private static HashSet<int> _checkedDiscoveryIds = new();
    private static string _trailDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "trails");
    private static WorldMapOptions _worldMap = new();

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
                WorldMap = _worldMap
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
