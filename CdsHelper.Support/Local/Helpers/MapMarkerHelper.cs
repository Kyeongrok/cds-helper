using System.Windows.Controls;
using System.Windows.Media;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.UI.Units;

namespace CdsHelper.Support.Local.Helpers;

public static class MapMarkerHelper
{
    private const string LabelTag = "CityLabel";

    /// <summary>
    /// Canvas에 도시 마커들을 추가합니다.
    /// </summary>
    public static void AddCityMarkers(Canvas canvas, IEnumerable<City> cities, bool showLabels = false)
    {
        if (canvas == null) return;

        foreach (var city in cities)
        {
            if (!city.PixelX.HasValue || !city.PixelY.HasValue || city.PixelX <= 0 || city.PixelY <= 0)
                continue;

            var x = city.PixelX.Value;
            var y = city.PixelY.Value;

            // 도시 마커 (파란 원)
            var marker = new Marker(x, y);
            canvas.Children.Add(marker);

            // 도시 이름 라벨
            var label = new TextBlock
            {
                Text = city.Name,
                FontSize = 10,
                Foreground = Brushes.Black,
                Background = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                Tag = LabelTag,
                Visibility = showLabels ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed
            };
            Canvas.SetLeft(label, x + 5);
            Canvas.SetTop(label, y - 8);
            canvas.Children.Add(label);
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
            if (child is TextBlock label && label.Tag?.ToString() == LabelTag)
            {
                label.Visibility = visible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
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
