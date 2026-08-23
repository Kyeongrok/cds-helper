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
///   0x00571B10  "%2d월%2d일생(%2d세)  %-6s  %s형"
///   0x00571500  "&lt;&lt;"  "&gt;&gt;"  "일람"  "이름 일람"  "입력 에러"
///   0x005609D8  별자리 열둘 — 목양좌부터
/// </code>
/// 이름 칸 오른쪽의 작은 단추는 <see cref="TextInputDialog"/> 를 열고, "일람" 은
/// 미리 갖춰 둔 이름을 늘어놓는다. 숫자 칸도 같은 작은 단추로 받는다.
///
/// <b>이름 일람은 게임 것이 아니다.</b> 게임은 그 목록을 파일에서 읽어 오는데
/// (<c>0x0045C9DD</c> 가 클래스 <c>0x004FD0D8</c> 을 세운다) 그 파일을 아직 안 짚었다.
/// 그래서 EXE 의 <b>후원자 이름 여든하나</b>(<see cref="SponsorTable"/>)를 가운뎃점에서
/// 갈라 명·성 목록으로 쓴다 — 같은 시대의 진짜 이름들이다.
///
/// 게임은 이 뒤로 세 걸음이 더 있다(능력치 · 지식·언어 · 마무리). 우리는 아직 이 한
/// 걸음이라 "다음" 이 곧 시작이다. 자세한 것은 볼트
/// <c>39.분석-NEW GAME(주인공 만들기와 은퇴)</c>.
/// </remarks>
internal sealed class CharacterMakeDialog : InfoDialog
{
    /// <summary>판 크기. 게임 화면 비율에 맞춘다.</summary>
    private const double BoardWidth = 600, BoardHeight = 300;

    /// <summary>초상화를 몇 배로 그릴지.</summary>
    private const int FaceScale = 2;

    private readonly Portraits? _faces;
    private readonly IReadOnlyList<string> _givenNames, _familyNames;

    private readonly Image _portrait = new();
    private readonly TextBlock _family = Field(), _given = Field();
    private readonly TextBlock _age = Field(46), _month = Field(34), _day = Field(34);
    private readonly TextBlock _zodiac = new()
    {
        Foreground = Brushes.Black,
        FontWeight = FontWeights.Bold,
        FontSize = 15,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(6, 0, 0, 0),
    };

    private readonly List<Border> _bloods = [], _nations = [];

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

        var rows = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
        rows.Children.Add(NameRow("성", _family, () => _familyNames));
        rows.Children.Add(NameRow("명", _given, () => _givenNames));
        rows.Children.Add(AgeRow());
        rows.Children.Add(BirthRow());
        rows.Children.Add(BloodRow());
        rows.Children.Add(NationRow());

        var left = new StackPanel { Width = Portraits.Width * FaceScale + 4 };
        left.Children.Add(new Border
        {
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(2),
            Child = _portrait,
        });

        var arrows = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };
        arrows.Children.Add(Small("<<", () => Turn(-1), 40));
        arrows.Children.Add(Small(">>", () => Turn(+1), 40));
        left.Children.Add(arrows);

        var body = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(left, Dock.Left);
        body.Children.Add(left);
        body.Children.Add(rows);

        Build("", body, BoardWidth, BoardHeight,
              new GameButton("취소", Close), new GameButton("다음", Next));

        ShowFace();
        Mark();
    }

    private static TextBlock Field(double width = 250) => new()
    {
        Foreground = Brushes.Black,
        FontWeight = FontWeights.Bold,
        FontSize = 15,
        MinWidth = width,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(6, 2, 6, 2),
    };

    /// <summary>이름을 적는 칸 한 줄 — 이름표 · 글칸 · 글자판 단추 · 일람.</summary>
    private UIElement NameRow(string label, TextBlock box, Func<IReadOnlyList<string>> list)
    {
        var row = Row(label);
        row.Children.Add(Boxed(box));
        row.Children.Add(Small("田", () =>
        {
            if (TextInputDialog.Ask(this, box.Text, NameLimit) is { } typed) box.Text = typed;
        }));
        row.Children.Add(Small("일람", () =>
        {
            var names = list();
            int at = HintListDialog.Pick(this, names, "이름 일람", "이름이 없다.");
            if (at >= 0 && at < names.Count) box.Text = names[at];
        }, 52));
        return row;
    }

    private UIElement AgeRow()
    {
        var row = Row("연령");
        row.Children.Add(Boxed(_age));
        row.Children.Add(Small("田", () =>
        {
            int n = CountDialog.Ask(this, "연령", "연령", "세", Player.MaxAge);
            if (n >= Player.MinAge) _age.Text = $"{n}";
        }));
        return row;
    }

    private UIElement BirthRow()
    {
        var row = Row("생일");
        row.Children.Add(Boxed(_month));
        row.Children.Add(Small("田", () =>
        {
            int n = CountDialog.Ask(this, "생일", "달", "월", 12);
            if (n >= 1) { _month.Text = $"{n}"; Mark(); }
        }));
        row.Children.Add(Text("월"));
        row.Children.Add(Boxed(_day));
        row.Children.Add(Small("田", () =>
        {
            int n = CountDialog.Ask(this, "생일", "날", "일", 31);
            if (n >= 1) { _day.Text = $"{n}"; Mark(); }
        }));
        row.Children.Add(Text("일"));
        row.Children.Add(_zodiac);
        return row;
    }

    private UIElement BloodRow()
    {
        var row = Row("혈액형");
        for (int i = 0; i < Player.BloodTypes.Length; i++)
        {
            int pick = i;
            var cell = Pick(Player.BloodTypes[i], 40, () => { _blood = pick; Mark(); });
            _bloods.Add(cell);
            row.Children.Add(cell);
        }
        return row;
    }

    private UIElement NationRow()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        };
        for (int i = 0; i < Player.Nations.Length; i++)
        {
            int pick = i;
            var cell = Pick(Player.Nations[i], 130, () => { _nation = pick; Mark(); });
            _nations.Add(cell);
            row.Children.Add(cell);
        }
        return row;
    }

    private static StackPanel Row(string label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Ink,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            Width = 54,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    private static UIElement Text(string text) => new TextBlock
    {
        Text = text,
        Foreground = Ink,
        FontWeight = FontWeights.Bold,
        FontSize = 15,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(2, 0, 4, 0),
    };

    private static UIElement Boxed(UIElement child) => new Border
    {
        Background = GameUi.PageFill,
        BorderBrush = GameUi.ItemEdge,
        BorderThickness = new Thickness(2),
        Margin = new Thickness(4, 0, 2, 0),
        Child = child,
    };

    /// <summary>작은 네모 단추(글자판·숫자판·화살표).</summary>
    private static Border Small(string text, Action run, double width = 26)
    {
        var box = new Border
        {
            Background = GameUi.ItemFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(2),
            Width = width,
            Margin = new Thickness(1, 0, 1, 0),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 1),
            },
        };
        box.MouseLeftButtonDown += (_, e) => e.Handled = true;
        box.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
        return box;
    }

    /// <summary>고르는 칸(혈액형·국적). 고른 것만 도드라진다.</summary>
    private static Border Pick(string text, double width, Action run)
    {
        var box = Small(text, run, width);
        box.Margin = new Thickness(3, 0, 3, 0);
        return box;
    }

    /// <summary>고른 것을 도드라지게 칠하고 별자리를 다시 적는다.</summary>
    private void Mark()
    {
        for (int i = 0; i < _bloods.Count; i++)
            _bloods[i].Background = i == _blood ? GameUi.PageFill : GameUi.ItemFill;
        for (int i = 0; i < _nations.Count; i++)
            _nations[i].Background = i == _nation ? GameUi.PageFill : GameUi.ItemFill;

        _zodiac.Text = Player.ZodiacOf(Number(_month, 1), Number(_day, 1));
    }

    /// <summary>초상화를 하나 옆으로 넘긴다.</summary>
    private void Turn(int by)
    {
        int count = _faces?.MaleCount ?? 0;
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
        _portrait.Width = Portraits.Width * FaceScale;
        _portrait.Height = Portraits.Height * FaceScale;
        RenderOptions.SetBitmapScalingMode(_portrait, BitmapScalingMode.NearestNeighbor);
    }

    private static int Number(TextBlock box, int fallback) =>
        int.TryParse(box.Text, out int n) ? n : fallback;

    /// <summary>이름 한 칸에 들어갈 수 있는 길이.</summary>
    private const int NameLimit = 16;

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
