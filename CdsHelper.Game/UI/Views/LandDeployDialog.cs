using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Engine.Land;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 육상전 <b>부대배치</b> 화면 — 여섯 자리에 부대를 놓고 「결정」을 누른다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0049E000</c>~<c>0x004A0500</c> 이다(볼트 <c>65.분석-육상전</c> 4절).
/// 자리와 크기를 손으로 짐작하지 않았다 — 게임이 그림을 어디에 얹는지 그대로 옮겼다.
/// <code>
///   0049ec00  자리 여섯   (16,16) (128,16) (240,16) (16,160) (128,160) (240,160)
///   0049ecd1  고르는 넉 칸 (368,16) (480,16) (368,160) (480,160)
///   0049fefb  판 그림      0x004B5CB9(0, 0, 592, 320, 파트 6)
///   0049ff37  부대 한 칸   96 x 96
///   0049f8b5  「결정」 (536,292) 48x24 · 0049f947 「전회」 (478,292) 48x24
/// </code>
///
/// <b>낼 수 있는 병종은 넷</b>이다 — 대장 · 근접 · 사격 · 포. 무엇이 되는지는 제 기능이
/// 정하고, 갈래마다 몇 부대까지인지도 기능이 정한다. 셈은 <see cref="LandRoster"/> 에 있다.
///
/// <b>「전회」는 물러서는 단추가 아니라 지난번 배치를 다시 펴는 단추</b>다
/// (<c>0x0049F540</c> 이 <c>0x0056EAB8</c> 여섯 칸을 그대로 되놓는다). 새 놀이에서 그 여섯이
/// 죄다 −1 인 것도 그래서다 — 아직 한 번도 안 싸운 것이다.
/// </remarks>
internal sealed class LandDeployDialog : Window
{
    /// <summary>판 위 자리 여섯(<c>0x0049EC00</c>). 앞 셋이 윗줄, 뒤 셋이 아랫줄이다.</summary>
    private static readonly (int X, int Y)[] SlotAt =
    [
        (16, 16), (128, 16), (240, 16),
        (16, 160), (128, 160), (240, 160),
    ];

    /// <summary>고르는 넉 칸(<c>0x0049ECD1</c>) — 대장 · 근접 · 사격 · 포 차례다.</summary>
    private static readonly (int X, int Y)[] PickAt =
    [
        (368, 16), (480, 16),
        (368, 160), (480, 160),
    ];

    /// <summary>부대 한 칸의 한 변.</summary>
    private const int Tile = LandArt.DeploySide;

    /// <summary>숫자 한 자의 한 변.</summary>
    private const int Digit = LandArt.DigitSide;

    /// <summary>병사수를 넉 자리로 보고 가운데로 몬다(<c>0x0049FD14</c>).</summary>
    private const int DigitSlots = 4;

    /// <summary>병종 이름을 얹는 자리 — 칸 밑 <c>0x70</c> 이다(<c>0x0049FDDE</c>).</summary>
    private const int NameDrop = 0x70;

    /// <summary>「×」와 남은 수를 얹는 자리(<c>0x0049FE21</c> · <c>0x0049FE7E</c>).</summary>
    private const int CrossX = 0x20, CrossY = 0x70, CountX = 0x30, CountY = 0x68;

    /// <summary>단추 둘(<c>0x0049F8B5</c> · <c>0x0049F947</c>).</summary>
    private const int ButtonY = 292, ButtonW = 48, ButtonH = 24,
                      BackX = 478, DecideX = 536;

    /// <summary>글자 한 자가 차지하는 폭의 절반 — 이름을 가운데로 몰 때 쓴다.</summary>
    private const int HalfCell = 4, NameCells = 12;

    /// <summary>
    /// 지난번 배치 — 게임의 <c>0x0056EAB8</c> 여섯 칸이다.
    /// </summary>
    /// <remarks>
    /// 게임도 정적 자리에 들고 있어 한 판 안에서만 살아 있다. 병종 번호가 아니라
    /// <b>고르는 넉 칸의 번호</b>로 적어 둔다 — 기능이 오르면 같은 자리라도 병종이
    /// 달라지는데, 게임의 <c>0x0049F370</c> 이 되놓을 때 바로 그 옮김을 한다.
    /// </remarks>
    private static readonly int[] LastPicked = [-1, -1, -1, -1, -1, -1];

    private readonly LandArt? _art;
    private readonly LandRoster _roster;
    private readonly SoundBank? _sfx;

    /// <summary>자리마다 놓인 것 — 고르는 넉 칸의 번호, 비었으면 −1.</summary>
    private readonly int[] _picked = [-1, -1, -1, -1, -1, -1];

    /// <summary>자리마다 나뉜 병사수.</summary>
    private readonly int[] _men = new int[LandRoster.SlotCount];

    private readonly Canvas _board = new()
    {
        Width = LandArt.BoardWidth,
        Height = LandArt.BoardHeight,
    };

    private readonly Canvas _layer = new()
    {
        Width = LandArt.BoardWidth,
        Height = LandArt.BoardHeight,
    };

    private LandDeployDialog(Engine.Game game, string cityName, LandRoster roster, int scale)
    {
        _roster = roster;
        _art = game.Directory.Length > 0 ? LandArt.Open(game.Directory) : null;
        _sfx = game.Sfx;

        Title = $"{cityName} — 부대배치";
        Width = LandArt.BoardWidth * scale;
        Height = LandArt.BoardHeight * scale;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Black;

        Content = Build(scale);
        Redraw();
    }

    /// <summary>
    /// 창을 띄운다. 「결정」을 눌렀으면 참.
    /// </summary>
    /// <remarks>
    /// 문화권을 안 받는다 — 낼 수 있는 여덟 병종은 <see cref="LandUnitArt.PartOf"/> 에서
    /// 죄다 아군·적으로만 갈리고 문화권으로는 안 갈린다.
    /// </remarks>
    public static bool Show(Window? owner, Engine.Game game, string cityName)
    {
        var roster = LandRoster.For(game.Player, Aide(game));

        double areaW = owner?.ActualWidth ?? LandArt.BoardWidth;
        double areaH = owner?.ActualHeight ?? LandArt.BoardHeight;
        int scale = Math.Max(1, Math.Min(3, (int)Math.Min(areaW * 0.95 / LandArt.BoardWidth,
                                                          areaH * 0.95 / LandArt.BoardHeight)));

        var window = new LandDeployDialog(game, cityName, roster, scale);
        if (owner != null) window.Owner = owner;
        return window.ShowDialog() == true;
    }

    /// <summary>부관 신상. 없으면 null — 그때는 제독의 기능만 본다.</summary>
    private static Support.Local.Models.Player.MateInfo? Aide(Engine.Game game)
    {
        var mates = game.Player.Mates;
        if (mates.Count == 0) return null;

        string name = mates[0];
        return name.Length > 0 ? game.Player.MateInfoOf(name) : null;
    }

    // ── 화면 ───────────────────────────────────────────────────────────────────

    private UIElement Build(int scale)
    {
        if (Picture() is { } board) _board.Children.Add(Place(board, 0, 0));
        else _board.Background = new SolidColorBrush(Color.FromRgb(0x6B, 0x5E, 0x55));

        // 판 위에서 자리를 누르는 것이 배치다. 칸에 그림이 없어도 누를 수 있어야 하므로
        // 자리마다 눈에 안 띄는 네모를 하나씩 깔아 둔다.
        for (int i = 0; i < LandRoster.SlotCount; i++)
        {
            int slot = i;
            var spot = new Border
            {
                Width = Tile,
                Height = Tile,
                Background = Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            spot.MouseLeftButtonUp += (_, _) => Choose(slot);
            _board.Children.Add(Place(spot, SlotAt[i].X, SlotAt[i].Y));
        }

        _board.Children.Add(_layer);
        Canvas.SetLeft(_layer, 0);
        Canvas.SetTop(_layer, 0);

        _board.Children.Add(Place(Button("전회", Previous), BackX, ButtonY));
        _board.Children.Add(Place(Button("결정", Decide), DecideX, ButtonY));

        var box = new Canvas
        {
            Width = LandArt.BoardWidth * scale,
            Height = LandArt.BoardHeight * scale,
            Children = { _board },
        };
        _board.RenderTransform = new ScaleTransform(scale, scale);
        return box;
    }

    private static GameButton Button(string text, Action run) =>
        new(text, run, BandStyle.Button, ButtonW)
        {
            Margin = default,
            Height = ButtonH,
        };

    private static FrameworkElement Place(FrameworkElement what, int x, int y)
    {
        Canvas.SetLeft(what, x);
        Canvas.SetTop(what, y);
        return what;
    }

    /// <summary>배치 판 그림. 못 읽으면 null.</summary>
    private Image? Picture()
    {
        if (_art?.TryGetBoard() is not { } bgra) return null;
        return Put(bgra, LandArt.BoardWidth, LandArt.BoardHeight);
    }

    private static Image Put(uint[] bgra, int w, int h)
    {
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        bmp.Freeze();

        var image = new Image { Source = bmp, Width = w, Height = h, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
        return image;
    }

    // ── 다시 그리기 ────────────────────────────────────────────────────────────

    private void Redraw()
    {
        Split();
        _layer.Children.Clear();

        for (int i = 0; i < LandRoster.SlotCount; i++)
        {
            if (_picked[i] < 0) continue;
            var (x, y) = SlotAt[i];

            if (Unit(_roster.KindAt(_picked[i])) is { } art) _layer.Children.Add(Place(art, x, y));

            // 병사수는 넉 자리 폭에 가운데로 몬다(0x0049FD14).
            string men = _men[i].ToString();
            int left = x + (DigitSlots - men.Length) * Digit / 2;
            foreach (char c in men)
            {
                if (Number(c - '0') is { } glyph) _layer.Children.Add(Place(glyph, left, y));
                left += Digit;
            }

            // 병종 이름은 칸 밑에 붙는다(0x0049FDDE).
            string name = _roster.NameAt(_picked[i]);
            if (Word(name) is { } label)
                _layer.Children.Add(Place(label, x + (NameCells - Bytes(name)) * HalfCell,
                                          y + NameDrop));
        }

        for (int choice = 0; choice < LandRoster.ChoiceCount; choice++)
        {
            var (x, y) = PickAt[choice];
            if (Unit(_roster.KindAt(choice)) is { } art) _layer.Children.Add(Place(art, x, y));

            int left = Remaining(choice);
            if (Word("×") is { } cross) _layer.Children.Add(Place(cross, x + CrossX, y + CrossY));
            if (Number(Math.Min(left, 9)) is { } glyph)
                _layer.Children.Add(Place(glyph, x + CountX, y + CountY));
        }
    }

    /// <summary>그 병종의 배치 화면 그림. 못 구하면 null.</summary>
    private Image? Unit(int kind)
    {
        if (_art == null) return null;

        var bgra = _art.TryGetDeployUnit(kind, out int w, out int h);
        return bgra == null ? null : Put(bgra, w, h);
    }

    /// <summary>숫자 한 자. 못 구하면 게임 글꼴로 물러선다.</summary>
    private FrameworkElement? Number(int digit)
    {
        if (_art?.TryGetDigit(digit) is { } bgra) return Put(bgra, Digit, Digit);
        return Word(digit.ToString());
    }

    private static FrameworkElement? Word(string text) =>
        GameUi.GameFontLabel(text, GameFont.ButtonColor, 1, GameUi.ItemTextHeight);

    /// <summary>게임이 이름 길이를 재는 방식 — 바이트 수다(<c>0x0049FDC3</c>).</summary>
    private static int Bytes(string text) =>
        System.Text.Encoding.GetEncoding(949).GetByteCount(text);

    // ── 셈 ─────────────────────────────────────────────────────────────────────

    /// <summary>판에 선 부대 수.</summary>
    private int Standing() => _picked.Count(p => p >= 0);

    /// <summary>그 갈래를 몇 부대 더 낼 수 있는지(<c>0x0049F6E0</c>).</summary>
    private int Remaining(int choice)
    {
        int placed = _picked.Count(p => p == choice);
        return Math.Max(0, _roster.CapAt(choice) - placed);
    }

    /// <summary>
    /// 인원을 다시 나눈다 — <b>고르게 나누고 나머지는 대장 부대</b>가 갖는다.
    /// </summary>
    /// <remarks><c>0x0049F640</c> 이 부대를 놓거나 걷을 때마다 이것을 다시 돌린다.</remarks>
    private void Split()
    {
        Array.Clear(_men);
        int units = Standing();
        if (units == 0) return;

        int each = _roster.Men / units, over = _roster.Men % units;
        for (int i = 0; i < LandRoster.SlotCount; i++)
        {
            if (_picked[i] < 0) continue;
            _men[i] = each + (_picked[i] == LandRoster.Leader ? over : 0);
        }
    }

    // ── 누르기 ─────────────────────────────────────────────────────────────────

    /// <summary>자리를 누르면 「선택」 차림표가 뜬다(<c>0x0049F010</c>).</summary>
    private void Choose(int slot)
    {
        // 빈 자리에 하나 더 세우는 것이면 사람이 남았는지부터 본다(0x004A00A0).
        if (_picked[slot] < 0 && Standing() >= _roster.Men)
        {
            NoticeDialog.Show(this, LandRoster.NoMoreWord, LandRoster.WarnTitle);
            return;
        }

        var rows = new (string, bool)[LandRoster.ChoiceCount + 1];
        for (int i = 0; i < LandRoster.ChoiceCount; i++)
            rows[i] = (_roster.NameAt(i), Remaining(i) > 0);
        rows[LandRoster.None] = (LandRoster.NoneWord, true);

        int pick = ChoiceDialog.Pick(this, LandRoster.PickTitle, rows);
        if (pick < 0) return;

        // 놓는 소리와 걷는 소리가 다르다(0x0049F23E · 0x0049F205).
        bool clearing = pick == LandRoster.None;
        _sfx?.Play(clearing ? SoundBank.DeployLiftPart : SoundBank.DeployPlacePart);

        _picked[slot] = clearing ? -1 : pick;
        Redraw();
    }

    /// <summary>
    /// 「전회」 — 지난번 배치를 그대로 되편다(<c>0x0049F4F0</c>).
    /// </summary>
    /// <remarks>
    /// 되펴기 전에 두 가지를 본다(<c>0x0049F3E0</c>). 갈래마다 지금 낼 수 있는 수를 넘으면
    /// 「현재의 부대배치가능수 보다 많아…」고, 사람보다 부대가 많으면 「현재 인원수로는
    /// 나눌 수 없습니다」다. 지난번이 없으면(죄다 −1) 아무 일도 안 일어난다.
    /// </remarks>
    private void Previous()
    {
        var counts = new int[LandRoster.ChoiceCount];
        foreach (int choice in LastPicked)
            if (choice >= 0 && choice < LandRoster.ChoiceCount) counts[choice]++;

        for (int i = 0; i < LandRoster.ChoiceCount; i++)
            if (counts[i] > _roster.CapAt(i))
            {
                NoticeDialog.Show(this, LandRoster.TooManyWord, LandRoster.WarnTitle);
                return;
            }

        // 게임이 세는 것은 대장을 뺀 셋뿐이다 — 갈래 셈에서 대장 부대를 빼고(0x0049F49F)
        // 다시 하나를 더하기 때문에(0x0049F4B5) 대장 한 자리가 셈에서 상쇄된다.
        int units = counts[LandRoster.Melee] + counts[LandRoster.Shot] + counts[LandRoster.Cannon];
        if (units > _roster.Men)
        {
            NoticeDialog.Show(this, LandRoster.CannotSplitWord, LandRoster.WarnTitle);
            return;
        }

        Array.Copy(LastPicked, _picked, LandRoster.SlotCount);
        Redraw();
    }

    /// <summary>
    /// 「결정」 — 대장 부대가 판에 없으면 물린다(<c>0x004473B0</c>).
    /// </summary>
    private void Decide()
    {
        if (!_picked.Contains(LandRoster.Leader))
        {
            NoticeDialog.Show(this, LandRoster.NeedLeaderWord, "");
            return;
        }

        Array.Copy(_picked, LastPicked, LandRoster.SlotCount);
        DialogResult = true;
        Close();
    }
}
