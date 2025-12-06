namespace CdsHelper.Support.Local.Settings;

public static class AppSettings
{
    public static event Action? SettingsChanged;

    private static double _markerSize = 8.0;

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
