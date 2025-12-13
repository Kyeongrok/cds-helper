namespace CdsHelper.Support.Local.Settings;

public class ViewOption
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public static class AppSettings
{
    public const double DefaultMarkerSize = 11.0;
    public const string DefaultDefaultView = "PlayerContent";

    public static event Action? SettingsChanged;

    private static double _markerSize = DefaultMarkerSize;
    private static string _defaultView = DefaultDefaultView;

    public static double MarkerSize
    {
        get => _markerSize;
        set
        {
            _markerSize = Math.Clamp(value, 4.0, 20.0);
            SettingsChanged?.Invoke();
        }
    }

    public static string DefaultView
    {
        get => _defaultView;
        set
        {
            _defaultView = value;
            SettingsChanged?.Invoke();
        }
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
        new() { Name = "MapContent", DisplayName = "지도" }
    };
}
