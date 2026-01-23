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
    public HashSet<int> CheckedDiscoveryIds { get; set; } = new();
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

    public static readonly List<ViewOption> AvailableViews = new()
    {
        new() { Name = "PlayerContent", DisplayName = "플레이어" },
        new() { Name = "CharacterContent", DisplayName = "캐릭터" },
        new() { Name = "BookContent", DisplayName = "도서" },
        new() { Name = "CityContent", DisplayName = "도시" },
        new() { Name = "PatronContent", DisplayName = "후원자" },
        new() { Name = "FigureheadContent", DisplayName = "선수상" },
        new() { Name = "ItemContent", DisplayName = "아이템" },
        new() { Name = "MapContent", DisplayName = "지도" },
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
                    _checkedDiscoveryIds = data.CheckedDiscoveryIds ?? new();
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
                CheckedDiscoveryIds = _checkedDiscoveryIds
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
