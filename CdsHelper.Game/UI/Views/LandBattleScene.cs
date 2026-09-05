using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Engine.Land;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 육상전 싸움터 — 배치가 끝나면 이 화면으로 넘어간다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0044A870</c> 이 펴는 판이다. 자리를 손으로 짐작하지 않았다 —
/// <c>0x00445258</c>~<c>0x004453B9</c> 가 열두 자리를 못 박는 그 값 그대로다.
/// <code>
///   아군  (112,208) (176,256) (240,304) / (48,256) (112,304) (176,352)
///   적    (432,160) (368,112) (304, 64) / (496,112) (432, 64) (368, 16)
/// </code>
/// 싸움터 그림은 LANDDATA 파트 1~4(640x480), 부대는 파트 8~50 에서 온다.
/// 병사수는 부대배치 화면과 같은 24x24 숫자 조각이다.
///
/// <b>여기까지가 옮긴 데다.</b> 「제 N턴」을 알리고 <b>공격명령</b> 차림표를 내는
/// 데까지고, 명령을 고른 뒤의 셈(<c>0x00449320</c> · 피해 <c>0x00448360</c>)은 아직이다.
/// </remarks>
internal sealed class LandBattleScene : Window
{
    /// <summary>
    /// 부대 열둘이 서는 자리(<c>0x00445258</c>~<c>0x004453B9</c>).
    /// 앞 여섯이 아군, 뒤 여섯이 적이다.
    /// </summary>
    private static readonly (int X, int Y)[] StandAt =
    [
        (112, 208), (176, 256), (240, 304),
        (48, 256), (112, 304), (176, 352),
        (432, 160), (368, 112), (304, 64),
        (496, 112), (432, 64), (368, 16),
    ];

    /// <summary>숫자 한 자의 한 변과, 넉 자리 폭에 가운데로 모는 셈(<c>0x0049FD14</c>).</summary>
    private const int Digit = LandArt.DigitSide, DigitSlots = 4;

    private readonly LandArt? _art;
    private readonly LandBattle _battle;
    private readonly Canvas _board = new()
    {
        Width = LandArt.FieldWidth,
        Height = LandArt.FieldHeight,
    };

    private LandBattleScene(Engine.Game game, LandBattle battle, int scale)
    {
        _battle = battle;
        _art = game.Directory.Length > 0 ? LandArt.Open(game.Directory) : null;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Black;
        Width = LandArt.FieldWidth * scale;
        Height = LandArt.FieldHeight * scale;

        if (Field() is { } field) _board.Children.Add(At(field, 0, 0));
        else _board.Background = new SolidColorBrush(Color.FromRgb(0x35, 0x3B, 0x30));

        Stand();

        _board.RenderTransform = new ScaleTransform(scale, scale);
        Content = new Canvas
        {
            Width = LandArt.FieldWidth * scale,
            Height = LandArt.FieldHeight * scale,
            Children = { _board },
        };
    }

    /// <summary>
    /// 판을 펴고 첫 턴을 돌린다. 「퇴각」을 골랐거나 턴이 다하면 돌아온다.
    /// </summary>
    public static void Run(Window? owner, Engine.Game game, LandBattle battle)
    {
        double areaW = owner?.ActualWidth ?? LandArt.FieldWidth;
        double areaH = owner?.ActualHeight ?? LandArt.FieldHeight;
        int scale = Math.Max(1, Math.Min(3, (int)Math.Min(areaW * 0.95 / LandArt.FieldWidth,
                                                          areaH * 0.95 / LandArt.FieldHeight)));

        var scene = new LandBattleScene(game, battle, scale);
        if (owner != null) scene.Owner = owner;
        scene.Show();
        try { scene.Fight(); }
        finally { scene.Close(); }
    }

    /// <summary>
    /// 턴을 돈다 — 「제 N턴」을 알리고 공격명령을 묻는다(<c>0x00449C80</c>).
    /// </summary>
    /// <remarks>
    /// 명령을 고른 뒤의 셈이 아직이라 한 턴이 그대로 지나간다. 「퇴각」만 제 몫을 한다.
    /// </remarks>
    private void Fight()
    {
        // 첫 턴에만 일기토를 걸 수 있다(0x0044A604 가 +0x54 를 4 로 두고, 한 번
        // 싸우고 나면 0x00449BC5 어름이 끈다).
        bool first = true;

        while (true)
        {
            NoticeDialog.Show(this, _battle.TurnWord, "");

            int order = ChoiceDialog.Pick(this, $" {LandBattle.OrderTitle} ",
                                          _battle.OrderRows(canDuel: first, canRuse: true));
            if (order == LandBattle.Retreat)
            {
                if (ConfirmDialog.Ask(this, "퇴각해도 좋습니까?")) return;
                continue;
            }
            if (order < 0) continue;                 // 물러도 차림표가 다시 뜬다

            // 「애니메이션」은 켜고 끄는 것이라 턴이 안 간다(0x00449190).
            if (order == LandBattle.Animate) continue;

            first = false;
            NoticeDialog.Show(this,
                "…(부대는 명령을 받았지만 치고받는 셈은 아직 옮기지 못했다.)", "");
            if (!_battle.NextTurn()) return;
        }
    }

    // ── 그리기 ─────────────────────────────────────────────────────────────────

    /// <summary>싸움터 한 장. 못 읽으면 null.</summary>
    private Image? Field()
    {
        if (_art?.TryGetField(_battle.Terrain) is not { } bgra) return null;
        return Picture(bgra, LandArt.FieldWidth, LandArt.FieldHeight);
    }

    /// <summary>부대 열둘을 세운다.</summary>
    private void Stand()
    {
        for (int i = 0; i < LandBattle.Slots && i < StandAt.Length; i++)
        {
            var unit = _battle.Units[i];
            if (!unit.Standing) continue;

            var (x, y) = StandAt[i];
            if (Sprite(unit.Kind, friend: i < LandBattle.FirstFoe) is { } art)
                _board.Children.Add(At(art, x, y));

            // 병사수는 칸 위쪽에 넉 자리 폭으로 가운데를 맞춰 찍는다.
            string men = unit.Men.ToString();
            int left = x + (DigitSlots - men.Length) * Digit / 2;
            foreach (char c in men)
            {
                if (Number(c - '0') is { } glyph) _board.Children.Add(At(glyph, left, y));
                left += Digit;
            }
        }
    }

    /// <summary>그 병종의 첫 몸짓. 못 구하면 null.</summary>
    private Image? Sprite(int kind, bool friend)
    {
        if (_art == null) return null;

        var bgra = _art.TryGetUnit(kind, friend, _battle.Culture, frame: 0, out int w, out int h);
        return bgra == null ? null : Picture(bgra, w, h);
    }

    /// <summary>숫자 한 자. 조각을 못 구하면 게임 글꼴로 물러선다.</summary>
    private FrameworkElement? Number(int digit)
    {
        if (_art?.TryGetDigit(digit) is { } bgra) return Picture(bgra, Digit, Digit);
        return GameUi.GameFontLabel(digit.ToString(), GameFont.ButtonColor, 1,
                                    GameUi.ItemTextHeight);
    }

    private static Image Picture(uint[] bgra, int w, int h)
    {
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        bmp.Freeze();

        var image = new Image { Source = bmp, Width = w, Height = h, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
        return image;
    }

    private static FrameworkElement At(FrameworkElement what, int x, int y)
    {
        Canvas.SetLeft(what, x);
        Canvas.SetTop(what, y);
        return what;
    }
}
