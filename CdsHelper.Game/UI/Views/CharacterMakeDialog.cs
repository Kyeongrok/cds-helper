using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// NEW GAME 의 첫 걸음 — 성·명·연령·생일·혈액형·국적을 받는다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0045BF80</c> 이다(만들기 본체는 <c>0x0045EBE0</c>).
/// <code>
///   0x00571AAC  명   0x00571AB0 성   0x00571AB8 연령   0x00571AC0 생일
///   0x00571AC8  월   0x00571ACC 일   0x00571AD0 혈액형
///   0x00571B08  "%s·%s"                                  ; 명·성
///   0x00571500  "&lt;&lt;"  "&gt;&gt;"  "일람"  "이름 일람"  "입력 에러"
///   0x005609D8  별자리 열둘 — 목양좌부터
/// </code>
/// <b>자리는 화면을 재어 박았다.</b> 쌓기가 아니라 <see cref="Canvas"/> 로 놓는다 —
/// 행 피치가 일정하지 않고 칸 하나가 어긋나 있어 쌓기로는 그 모양이 안 난다.
/// <code>
///   판 644 x 392 · 속 614 x 362 · 테 8 베벨 + 4 빈칸 + 3 줄
///   가로  13 빈칸 · 초상화 140 · 30 빈칸 · 이름표 42 · 컨트롤 293 · 큰단추 84 · 12 여백
///   세로  13 성 · 42 명 · 42 연령 · 42 생일 · 47 혈액형 · 50 국적 · 70 단추
///   단추  일람·취소·다음 76x32 · 혈액형·화살표 62x32 · 국가 215x32 · 사이는 늘 8
/// </code>
/// <b>어긋난 데 둘을 그대로 뒀다.</b> 생일 줄의 첫 숫자칸이 연령 줄보다 <b>28(글자 한 칸)</b>
/// 오른쪽에 있고, 행 피치가 생일부터 47 · 50 으로 벌어진다. 재어 보니 게임이 그렇다.
///
/// 이름 칸 오른쪽 작은 단추는 <see cref="TextInputDialog"/> 를, 숫자 칸 것은
/// <see cref="NumberPadDialog"/> 를 연다. "일람" 은 미리 갖춰 둔 이름을 늘어놓는다.
///
/// <b>이름 일람은 게임 것이 아니다.</b> 게임은 그 목록을 파일에서 읽어 오는데
/// (<c>0x0045C9DD</c> 가 클래스 <c>0x004FD0D8</c> 을 세운다) 그 파일을 아직 안 짚었다.
/// 그래서 EXE 의 <b>후원자 이름 여든하나</b>(<see cref="SponsorTable"/>)를 가운뎃점에서
/// 갈라 명·성 목록으로 쓴다 — 같은 시대의 진짜 이름들이다.
/// </remarks>
internal sealed class CharacterMakeDialog : Window
{
    // ── 화면에서 잰 자리 ──────────────────────────────────────────────────────

    /// <summary>속 크기. 판은 여기에 테 30 을 두른 644 x 392 가 된다.</summary>
    private const double ContentWidth = 614, ContentHeight = 362;

    /// <summary>테 — 바깥 베벨 · 빈칸 · 안쪽 줄.</summary>
    private const double Bevel = 8, FrameGap = 4, FrameLine = 3;

    /// <summary>가로 자리(속 왼쪽에서).</summary>
    private const double PortraitX = 13, LabelX = 183, ControlX = 225, RightEdge = 602;

    /// <summary>초상화 크기. 얼굴 조각이 80x96 이라 딱 1.75배다.</summary>
    private const double FaceWidth = 140, FaceHeight = 168;

    /// <summary>세로 자리(속 위에서).</summary>
    private const double RowFamily = 13, RowGiven = 55, RowAge = 97, RowBirth = 139,
                         RowBlood = 186, RowNation = 236, RowFooter = 310;

    /// <summary>칸 크기.</summary>
    private const double FieldWidth = 263, FieldHeight = 28, NumWidth = 39, SpinSize = 28;

    /// <summary>단추 크기.</summary>
    private const double SmallWidth = 76, SmallHeight = 32, PickWidth = 62, NationWidth = 215;

    /// <summary>단추 사이. 화면 어디서나 8 이다.</summary>
    private const double Gap = 8;

    /// <summary>칸과 그 옆 작은 단추 사이.</summary>
    private const double SpinGap = 2;

    /// <summary>생일 줄이 연령 줄보다 밀려 있는 만큼 — 글자 한 칸이다.</summary>
    private const double BirthShift = 28;

    /// <summary>이름 한 칸에 들어갈 수 있는 길이.</summary>
    private const int NameLimit = 16;

    /// <summary>새 놀이에서 고를 수 있는 초상화 수 — <b>앞의 열여섯</b>이다.</summary>
    private const int FaceChoices = 16;

    // ── 색 ────────────────────────────────────────────────────────────────────

    private static readonly Brush Back = Frozen(Color.FromRgb(0x31, 0x18, 0x18));
    private static readonly Brush Line = Frozen(Color.FromRgb(0x11, 0x09, 0x09));
    private static readonly Brush Ink = Frozen(Color.FromRgb(0xCB, 0xC5, 0xC5));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private readonly Portraits? _faces;
    private readonly IReadOnlyList<string> _givenNames, _familyNames;
    private readonly Canvas _board = new() { Width = ContentWidth, Height = ContentHeight };

    private readonly Image _portrait = new();
    private readonly TextBlock _family = Field(), _given = Field();
    private readonly TextBlock _age = Field(), _month = Field(), _day = Field();
    private readonly TextBlock _zodiac = Glyph("");

    private readonly List<GameButton> _bloods = [], _nations = [];

    private int _face, _blood, _nation;
    private bool _ok;

    private CharacterMakeDialog(Player player, Portraits? faces, IReadOnlyList<string> given,
                                IReadOnlyList<string> family)
    {
        _faces = faces;
        _givenNames = given;
        _familyNames = family;

        _face = player.Face;
        _blood = player.Blood;
        _nation = player.Nation;
        _family.Text = player.Family;
        _given.Text = player.Given;
        _age.Text = $"{player.Age}";
        _month.Text = $"{player.BirthMonth}";
        _day.Text = $"{player.BirthDay}";

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Back;

        Portrait();
        NameRow(RowFamily, "성", _family, () => _familyNames);
        NameRow(RowGiven, "명", _given, () => _givenNames);
        AgeRow();
        BirthRow();
        BloodRow();
        NationRow();
        Footer();

        Content = Framed(_board);
        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
        MouseRightButtonUp += (_, _) => Close();

        ShowFace();
        Mark();
    }

    /// <summary>바깥 베벨 · 빈칸 · 안쪽 줄 세 겹.</summary>
    private UIElement Framed(UIElement content)
    {
        var inner = new Border
        {
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(FrameLine),
            Child = content,
        };
        var middle = new Border
        {
            Background = Back,
            Padding = new Thickness(FrameGap),
            Child = inner,
        };
        var outer = new Border
        {
            Background = Back,
            BorderBrush = Line,
            BorderThickness = new Thickness(Bevel),
            Child = middle,
        };
        GameUi.EnableDrag(this, outer);
        return outer;
    }

    // ── 조각 ──────────────────────────────────────────────────────────────────

    private static TextBlock Field() => new()
    {
        Foreground = Brushes.Black,
        FontWeight = FontWeights.Bold,
        FontSize = 16,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(6, 0, 6, 0),
    };

    private static TextBlock Glyph(string text) => new()
    {
        Text = text,
        Foreground = Ink,
        FontWeight = FontWeights.Bold,
        FontSize = 16,
    };

    /// <summary>속에 무엇 하나를 그 자리에 놓는다.</summary>
    private T Put<T>(T what, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(what, x);
        Canvas.SetTop(what, y);
        _board.Children.Add(what);
        return what;
    }

    /// <summary>이름표 한 자리.</summary>
    private void Label(string text, double y) => Put(Glyph(text), LabelX, y + 3);

    /// <summary>글이나 숫자를 적는 칸.</summary>
    private void Boxed(UIElement child, double x, double y, double width) =>
        Put(new Border
        {
            Background = GameUi.PageFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(2),
            Width = width,
            Height = FieldHeight,
            Child = child,
        }, x, y);

    /// <summary>칸 옆의 작은 계산기 단추.</summary>
    private void Spinner(double x, double y, Action run)
    {
        var box = new Border
        {
            Background = GameUi.ItemFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(2),
            Width = SpinSize,
            Height = SpinSize,
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = "田",
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        box.MouseLeftButtonDown += (_, e) => e.Handled = true;
        box.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
        Put(box, x, y);
    }

    /// <summary>띠 단추 하나.</summary>
    private GameButton Band(string text, double x, double y, double width, Action run)
    {
        var button = new GameButton(text, run, BandStyle.Button, width);
        button.Height = SmallHeight;
        button.Margin = default;
        return Put(button, x, y);
    }

    // ── 줄 ────────────────────────────────────────────────────────────────────

    private void Portrait()
    {
        Put(new Border
        {
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(2),
            Width = FaceWidth,
            Height = FaceHeight,
            Child = _portrait,
        }, PortraitX, RowFamily);

        // 화살표 둘은 초상화 한가운데에 맞춘다(62 + 8 + 62 = 132).
        double x = PortraitX + (FaceWidth - (PickWidth * 2 + Gap)) / 2;
        double y = RowFamily + FaceHeight + Gap;
        Band("<<", x, y, PickWidth, () => Turn(-1));
        Band(">>", x + PickWidth + Gap, y, PickWidth, () => Turn(+1));
    }

    private void NameRow(double y, string label, TextBlock box, Func<IReadOnlyList<string>> list)
    {
        Label(label, y);
        Boxed(box, ControlX, y, FieldWidth);
        Spinner(ControlX + FieldWidth + SpinGap, y, () =>
        {
            if (TextInputDialog.Ask(this, box.Text, NameLimit) is { } typed) box.Text = typed;
        });
        Band("일람", RightEdge - SmallWidth, y - 2, SmallWidth, () =>
        {
            var names = list();
            int at = MapPointDialog.Ask(this, names);
            if (at >= 0 && at < names.Count) box.Text = names[at];
        });
    }

    private void AgeRow()
    {
        Label("연령", RowAge);
        Boxed(_age, ControlX, RowAge, NumWidth);
        Spinner(ControlX + NumWidth + SpinGap, RowAge, () =>
        {
            if (NumberPadDialog.Ask(this, Number(_age, 25), Player.MinAge, Player.MaxAge) is { } n)
                _age.Text = $"{n}";
        });
    }

    /// <summary>
    /// 생일 줄. 첫 숫자칸이 연령 줄보다 <b>글자 한 칸(28)</b> 오른쪽이다 — 게임이 그렇다.
    /// </summary>
    private void BirthRow()
    {
        double x = ControlX + BirthShift;
        double after = NumWidth + SpinGap + SpinSize;

        Label("생일", RowBirth);
        Boxed(_month, x, RowBirth, NumWidth);
        Spinner(x + NumWidth + SpinGap, RowBirth, () =>
        {
            if (NumberPadDialog.Ask(this, Number(_month, 1), 1, 12) is { } n)
            { _month.Text = $"{n}"; Mark(); }
        });
        Put(Glyph("월"), x + after + 4, RowBirth + 3);

        double x2 = x + after + BirthShift;
        Boxed(_day, x2, RowBirth, NumWidth);
        Spinner(x2 + NumWidth + SpinGap, RowBirth, () =>
        {
            if (NumberPadDialog.Ask(this, Number(_day, 1), 1, 31) is { } n)
            { _day.Text = $"{n}"; Mark(); }
        });
        Put(Glyph("일"), x2 + after + 4, RowBirth + 3);
        Put(_zodiac, x2 + after + BirthShift + 4, RowBirth + 3);
    }

    private void BloodRow()
    {
        Label("혈액형", RowBlood);
        for (int i = 0; i < Player.BloodTypes.Length; i++)
        {
            int pick = i;
            _bloods.Add(Band(Player.BloodTypes[i], ControlX + 12 + i * (PickWidth + Gap), RowBlood,
                             PickWidth, () => { _blood = pick; Mark(); }));
        }
    }

    private void NationRow()
    {
        // 두 단추 묶음(215 + 8 + 215)을 속 한가운데에 놓는다.
        double x = (ContentWidth - (NationWidth * 2 + Gap)) / 2;
        for (int i = 0; i < Player.Nations.Length; i++)
        {
            int pick = i;
            _nations.Add(Band(Player.Nations[i], x + i * (NationWidth + Gap), RowNation,
                              NationWidth, () => { _nation = pick; Mark(); }));
        }
    }

    private void Footer()
    {
        Band("다음", RightEdge - SmallWidth, RowFooter, SmallWidth, Next);
        Band("취소", RightEdge - SmallWidth * 2 - Gap, RowFooter, SmallWidth, Close);
    }

    // ── 손 ────────────────────────────────────────────────────────────────────

    /// <summary>고른 것을 도드라지게 하고 별자리를 다시 적는다.</summary>
    private void Mark()
    {
        // 고른 것은 회녹색 띠로 갈아 낸다 — 게임도 고른 국가를 눌린 모양으로 낸다.
        for (int i = 0; i < _bloods.Count; i++)
            _bloods[i].Band = i == _blood ? BandStyle.Alt : BandStyle.Button;
        for (int i = 0; i < _nations.Count; i++)
            _nations[i].Band = i == _nation ? BandStyle.Alt : BandStyle.Button;
        _zodiac.Text = Player.ZodiacOf(Number(_month, 1), Number(_day, 1));
    }

    /// <summary>초상화를 하나 옆으로 넘긴다. 고를 수 있는 것은 앞의 열여섯뿐이다.</summary>
    private void Turn(int by)
    {
        int count = Math.Min(FaceChoices, _faces?.MaleCount ?? 0);
        if (count <= 0) return;
        _face = (_face + by % count + count) % count;
        ShowFace();
    }

    private void ShowFace()
    {
        var px = _faces?.TryGetBgra(_face, female: false);
        if (px == null) { _portrait.Source = null; return; }

        var bmp = BitmapSource.Create(Portraits.Width, Portraits.Height, 96, 96,
                                      PixelFormats.Bgra32, null, px, Portraits.Width * 4);
        bmp.Freeze();
        _portrait.Source = bmp;
        _portrait.Stretch = Stretch.Fill;
        RenderOptions.SetBitmapScalingMode(_portrait, BitmapScalingMode.NearestNeighbor);
    }

    private static int Number(TextBlock box, int fallback) =>
        int.TryParse(box.Text, out int n) ? n : fallback;

    /// <summary>"다음" — 게임처럼 빈 칸을 먼저 따진다.</summary>
    private void Next()
    {
        if (_given.Text.Trim().Length == 0)
        {
            NoticeDialog.Show(this, "이름을 정확히 입력해 주십시오");
            return;
        }
        int age = Number(_age, 0);
        if (age < Player.MinAge || age > Player.MaxAge)
        {
            NoticeDialog.Show(this, "연령을 정확히 입력해 주십시오");
            return;
        }
        int month = Number(_month, 0), day = Number(_day, 0);
        if (month is < 1 or > 12 || day is < 1 or > 31)
        {
            NoticeDialog.Show(this, "생일을 정확히 입력해 주십시오");
            return;
        }

        _ok = true;
        Close();
    }

    /// <summary>
    /// 신상 화면을 띄운다. "다음" 을 누르면 <paramref name="player"/> 에 적고 true.
    /// </summary>
    public static bool Show(Window owner, Player player, string gameDirectory)
    {
        var faces = gameDirectory.Length == 0 ? null : Portraits.Open(gameDirectory);
        var (given, family) = NamePool(gameDirectory);

        var dialog = new CharacterMakeDialog(player, faces, given, family) { Owner = owner };
        dialog.ShowDialog();
        if (!dialog._ok) return false;

        player.SetProfile(dialog._family.Text, dialog._given.Text,
                          Number(dialog._age, 25), Number(dialog._month, 1), Number(dialog._day, 1),
                          dialog._blood, dialog._nation, dialog._face);
        return true;
    }

    /// <summary>
    /// 고를 수 있는 명·성. 후원자 여든하나의 이름을 가운뎃점에서 가른 것이다.
    /// </summary>
    private static (List<string> Given, List<string> Family) NamePool(string gameDirectory)
    {
        var given = new List<string> { "라몬", "에밀리오", "에르네스토" };
        var family = new List<string> { "데·마르시아스", "알발레스" };

        if (gameDirectory.Length > 0 && SponsorTable.Open(gameDirectory) is { } table)
            foreach (var row in table.Sponsors)
            {
                int at = row.Name.IndexOf('·');
                if (at <= 0) { Add(given, row.Name); continue; }
                Add(given, row.Name[..at]);
                Add(family, row.Name[(at + 1)..]);
            }

        given.Sort(StringComparer.Ordinal);
        family.Sort(StringComparer.Ordinal);
        return (given, family);

        static void Add(List<string> to, string name)
        {
            name = name.Trim();
            if (name.Length > 0 && !to.Contains(name)) to.Add(name);
        }
    }
}
