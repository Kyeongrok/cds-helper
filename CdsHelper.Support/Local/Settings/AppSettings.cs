namespace CdsHelper.Support.Local.Settings;

public static class AppSettings
{
    public const double DefaultMarkerSize = 11.0;

    public static event Action? SettingsChanged;

    private static double _markerSize = DefaultMarkerSize;

    public static double MarkerSize
    {
        get => _markerSize;
        set
        {
            _markerSize = Math.Clamp(value, 4.0, 20.0);
            SettingsChanged?.Invoke();
        }
    }
}
