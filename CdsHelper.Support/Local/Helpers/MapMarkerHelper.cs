using System.Windows.Controls;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.UI.Units;

namespace CdsHelper.Support.Local.Helpers;

public static class MapMarkerHelper
{
    /// <summary>
    /// Canvas에 도시 마커들을 추가합니다.
    /// </summary>
    public static void AddCityMarkers(Canvas canvas, IEnumerable<City> cities, bool showLabels = false, bool showCoordinates = false, double markerSize = 8.0)
    {
        if (canvas == null) return;

        foreach (var city in cities)
        {
            if (!city.PixelX.HasValue || !city.PixelY.HasValue || city.PixelX <= 0 || city.PixelY <= 0)
                continue;

            var x = city.PixelX.Value;
            var y = city.PixelY.Value;

            var marker = new CityMarker(x, y, city.Name, city.Latitude, city.Longitude, markerSize)
            {
                ShowLabel = showLabels,
                ShowCoordinates = showCoordinates
            };
            canvas.Children.Add(marker);
        }
    }

    /// <summary>
    /// 모든 마커의 크기를 변경합니다.
    /// </summary>
    public static void SetMarkerSize(Canvas canvas, double size)
    {
        if (canvas == null) return;

        foreach (var child in canvas.Children)
        {
            if (child is CityMarker marker)
            {
                marker.MarkerSize = size;
            }
        }
    }

    /// <summary>
    /// 도시 라벨 표시 여부를 변경합니다.
    /// </summary>
    public static void SetLabelsVisibility(Canvas canvas, bool visible)
    {
        if (canvas == null) return;

        foreach (var child in canvas.Children)
        {
            if (child is CityMarker marker)
            {
                marker.ShowLabel = visible;
            }
        }
    }

    /// <summary>
    /// 좌표 표시 여부를 변경합니다.
    /// </summary>
    public static void SetCoordinatesVisibility(Canvas canvas, bool visible)
    {
        if (canvas == null) return;

        foreach (var child in canvas.Children)
        {
            if (child is CityMarker marker)
            {
                marker.ShowCoordinates = visible;
            }
        }
    }

    /// <summary>
    /// Canvas의 모든 자식 요소를 제거합니다.
    /// </summary>
    public static void ClearMarkers(Canvas canvas)
    {
        canvas?.Children.Clear();
    }
}
