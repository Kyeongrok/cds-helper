using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 항해지도 — 지금까지 <b>밝힌 바다</b>만 드러나는 세계 지도.
/// </summary>
/// <remarks>
/// 게임의 <c>0x00416A00</c> 이다. 점 하나가 칸 4x4 라 <b>625 x 313</b> 이고, 안 밝힌
/// 자리는 양피지색 그대로 남는다 — 처음 나가면 리스본 언저리만 동그랗게 뚫려 있다.
///
/// 그림은 지도 쪽(<see cref="ShipMapHost.Chart"/>)이 짓는다. 밝힘은 주인공이 든다
/// (<see cref="Support.Local.Models.ExploredMap"/>) — 세이브에 함께 적힌다.
/// </remarks>
public sealed class SeaChartDialog : Window
{
    /// <summary>몇 배로 키워 낼지. 625x313 을 그대로 내면 너무 작다.</summary>
    private const int Scale = 2;

    private SeaChartDialog(BitmapSource chart)
    {
        Title = "항해지도";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var image = new Image
        {
            Source = chart,
            Width = chart.PixelWidth * Scale,
            Height = chart.PixelHeight * Scale,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);

        var bar = GameUi.TitleBar("항해지도", Close);
        var stack = new StackPanel();
        stack.Children.Add(bar);
        stack.Children.Add(new Border
        {
            // 지도는 액자 안에 앉는다 — 게임도 갈색 테를 두 겹 두른다.
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            Child = image,
        });
        Content = stack;

        KeyDown += (_, e) => { if (e.Key is System.Windows.Input.Key.Escape) Close(); };
        GameUi.EnableDrag(this, bar);
    }

    /// <summary>
    /// 항해지도를 편다. 지도를 못 읽었거나 아직 한 번도 안 나갔으면 그 까닭을 알린다.
    /// </summary>
    public static void Show(Window owner, ShipMapHost host,
                            Support.Local.Models.ExploredMap seen)
    {
        var bgra = host.Chart(seen, out int w, out int h);
        if (bgra == null)
        {
            ConfirmDialog.Tell(owner, "지도를 읽지 못했습니다");
            return;
        }

        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        bmp.Freeze();
        new SeaChartDialog(bmp) { Owner = owner }.ShowDialog();
    }
}
