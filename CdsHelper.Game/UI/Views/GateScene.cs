using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 성문 앞 — 막힌 도시에 다가서면 <b>그림은 그려지고</b> 문지기에게 문에서 막힌다.
/// </summary>
/// <remarks>
/// 들어가겠다고 대답하면 게임은 도시 그림부터 편다. 그 위에서 문지기가
/// <c>0x00551D28</c> "외국인은 들어올 수 없다." 를 말하고, 그제야 공격·잠입·교섭·떠난다가
/// 뜬다(<c>0x004A5210</c> 이 차림표 <c>0x004A56F0</c> 의 앞머리다).
///
/// <b>말을 못 알아들으면 글자가 아니라 ×로 나온다.</b> 그리고 대원 중에도 아는 이가 없으면
/// 한 마디 덧붙는다 — 마을이면 <c>0x00551D48</c>, 항구면 <c>0x00551D90</c> 이라
/// <b>문구가 갈린다</b>. 인자가 0 이 아닐 때만 「마을」이라는 말이 들어가는 이 갈림이
/// 곧 차림표 인자의 뜻을 밝혀 준다(1 이 마을, 0 이 항구).
///
/// 이 창은 그림만 깐다 — 말과 차림표는 이 창을 임자로 삼아 그 위에 뜬다.
/// </remarks>
internal sealed class GateScene : Window
{
    private GateScene(BitmapSource picture, int scale, Rect mapArea)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = Brushes.Black;
        SizeToContent = SizeToContent.Manual;

        double fullW = CityPictures.Width * scale, fullH = CityPictures.Height * scale;
        Width = fullW;
        Height = fullH;
        if (mapArea.Width > 0)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = mapArea.X + (mapArea.Width - fullW) / 2;
            Top = mapArea.Y + (mapArea.Height - fullH) / 2;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        var art = new Image { Source = picture, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(art, BitmapScalingMode.NearestNeighbor);
        Content = art;
    }

    /// <summary>
    /// 그 도시의 그림을 편다. 그림을 못 구하면 null — 그때는 차림표만 뜬다.
    /// </summary>
    public static GateScene? Open(Window owner, Engine.Game game, int cityId, Rect mapArea)
    {
        if (game.CityPics is not { } pictures) return null;
        if (pictures.TryGetBgra(cityId) is not { } bgra) return null;

        var picture = BitmapSource.Create(CityPictures.Width, CityPictures.Height, 96, 96,
                                          PixelFormats.Bgra32, null, bgra,
                                          CityPictures.Width * 4);
        picture.Freeze();

        double areaW = mapArea.Width > 0 ? mapArea.Width : owner.ActualWidth;
        double areaH = mapArea.Height > 0 ? mapArea.Height : owner.ActualHeight;
        int scale = Math.Max(1, Math.Min(4, (int)Math.Min(areaW * 0.6 / CityPictures.Width,
                                                          areaH * 0.7 / CityPictures.Height)));

        var scene = new GateScene(picture, scale, mapArea) { Owner = owner };
        scene.Show();
        return scene;
    }
}
