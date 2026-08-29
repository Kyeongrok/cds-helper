using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// NEW GAME 둘째 걸음 — 굴린 능력치를 보너스 포인트로 손보고 직업을 고른다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0045D6C0</c> 이고, 굴리는 것은 <c>0x0045D450</c> 이다.
/// <code>
///   0x00560A88  능력치 여섯 — 체력 지력 무력 매력 운 (신앙심은 안 보인다)
///   0x00560AA8  직업 여덟   — 화면에는 탐험가 발굴자 사냥꾼 정복자 넷만
///   0x0051ACA0  직업마다 32바이트 — 능력치 보정
///   0x005472C0  생일마다 32바이트 — 이레만 값이 있다
///   45d568      값 = 생일보정 + rand(나이) + 직업보정 + 나이보정 + 50, 20~100 으로 자른다
///   45d5d5      보너스 = 합으로 갈린다 — 잘 굴렸을수록 덜 준다
/// </code>
/// <b>직업을 바꿔도 다시 안 굴린다.</b> 굴리는 <c>0x0045D450</c> 을 부르는 데는
/// <c>0x0045D022</c> 한 곳뿐인데, 그 자리는 <b>앞 걸음(신상)의 끝</b>이다 — 이 화면에
/// 들어오기 전에 이미 굴려 놓는다는 뜻이다. 그래서 직업 보정표는 새 놀이에서는
/// 늘 0번 줄(탐험가, 값이 다 0)로 걸리고, 표의 나머지 줄은 부하·NPC 쪽에서만 쓰인다.
/// 직업 단추는 <b>기본 기술</b>만 정한다(다음 걸음).
///
/// 다시 굴리고 싶으면 게임처럼 "취소" 로 신상 걸음까지 물러났다가 다시 오면 된다.
/// </remarks>
internal sealed class AbilityMakeDialog : InfoDialog
{
    /// <summary>
    /// 판 크기(그림 점). 잰 값이 <b>1.75배로 늘어난 화면</b>에서 나온 것이라 도로 나눴다 —
    /// 띠 단추만 제 크기(24)로 그려져 있어 혼자 작아 보였다.
    /// </summary>
    private const double BoardWidth = 296, BoardHeight = 178;

    /// <summary>능력치 줄의 이름 칸과 값 칸.</summary>
    private const double NameWidth = 48, ValueWidth = 28;

    /// <summary>값 옆 화살표 단추의 폭. 직업 단추는 마구리 둘에 가운데 여섯 칸이다.</summary>
    private const double ArrowWidth = 13, JobWidth = 80;

    private readonly int _age;

    private readonly GameUi.GameLabel[] _values = new GameUi.GameLabel[Ability.Shown];
    private readonly GameUi.GameLabel _bonus = new(GameFont.WhiteColor)
    {
        Bold = true,
        FallbackBrush = Ink,
        HorizontalAlignment = HorizontalAlignment.Right,
    };
    private readonly List<GameButton> _jobs = [];

    private int[] _stats;
    private int _left, _job;
    private bool _ok;

    private AbilityMakeDialog(Player player, Random rng)
    {
        _age = player.Age;
        _job = player.JobIndex;
        _stats = Ability.Roll(Job.Of(_job), _age, player.BirthMonth, player.BirthDay, rng);
        _left = Ability.BonusFor(_stats, rng);

        var left = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
        for (int i = 0; i < Ability.Shown; i++)
        {
            int which = i;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new GameUi.GameLabel(GameFont.WhiteColor)
            {
                Text = Ability.Names[i],
                Bold = true,
                FallbackBrush = Ink,
                Width = NameWidth,
                HorizontalAlignment = HorizontalAlignment.Left,
            });
            // 값은 오른쪽에 맞춘다 — 한 자리와 두 자리가 섞여도 화살표 자리가 안 흔들린다.
            _values[i] = new GameUi.GameLabel(GameFont.WhiteColor)
            {
                Bold = true,
                FallbackBrush = Ink,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            row.Children.Add(new Grid { Width = ValueWidth, Children = { _values[i] } });
            // 게임은 화살표 둘을 세로로 쌓지 않고 나란히 놓는다.
            row.Children.Add(Arrow(up: true, () => Move(which, +1)));
            row.Children.Add(Arrow(up: false, () => Move(which, -1)));
            left.Children.Add(row);
        }

        left.Children.Add(new Border
        {
            BorderBrush = Ink,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 7, 5, 0),
            Padding = new Thickness(3, 1, 3, 1),
            Child = Bonus(),
        });

        var right = new StackPanel { Margin = new Thickness(14, 1, 0, 0) };
        for (int i = 0; i < Job.Choosable; i++)
        {
            int pick = i;
            var cell = new GameButton(Job.All[i].Name, () => ChooseJob(pick), BandStyle.Button, JobWidth);
            cell.Margin = new Thickness(0, 2, 0, 2);
            _jobs.Add(cell);
            right.Children.Add(cell);
        }

        var body = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(left, Dock.Left);
        body.Children.Add(left);
        DockPanel.SetDock(right, Dock.Left);
        body.Children.Add(right);

        Build("", body, BoardWidth, BoardHeight,
              new GameButton("취소", Close), new GameButton("다음", Next));

        Sync();
    }

    /// <summary>보너스 포인트 상자 — 게임처럼 두 줄로 적는다.</summary>
    private UIElement Bonus()
    {
        var stack = new StackPanel();
        stack.Children.Add(new GameUi.GameLabel(GameFont.WhiteColor)
        {
            Text = "보너스",
            Bold = true,
            FallbackBrush = Ink,
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new GameUi.GameLabel(GameFont.WhiteColor)
        {
            Text = "  포인트:",
            Bold = true,
            FallbackBrush = Ink,
        });
        _bonus.MinWidth = ValueWidth;
        line.Children.Add(_bonus);
        stack.Children.Add(line);
        return stack;
    }

    /// <summary>
    /// 위·아래 화살표. 게임 조각(<c>MISC.CDS</c> 파트 3)을 그대로 건다 —
    /// 원본은 16x8 짜리 작은 칸이고, 못 누를 때 쓰는 X 칸도 같은 줄에 있다.
    /// </summary>
    private UIElement Arrow(bool up, Action run)
    {
        if (GameUi.Sprites?.Arrow(up ? UiSprites.ArrowUp : UiSprites.ArrowDown, pressed: false)
            is { } px)
        {
            var bmp = BitmapSource.Create(UiSprites.ArrowWidth, UiSprites.ArrowHeight, 96, 96,
                                          PixelFormats.Bgra32, null, px, UiSprites.ArrowWidth * 4);
            bmp.Freeze();

            var art = new Image
            {
                Source = bmp,
                Width = UiSprites.ArrowWidth,
                Height = UiSprites.ArrowHeight,
                Margin = new Thickness(1, 0, 0, 0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
            };
            RenderOptions.SetBitmapScalingMode(art, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(art, EdgeMode.Aliased);
            art.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
            return art;
        }

        // 조각을 못 읽었으면 글자 화살표로 물러선다.
        var box = new Border
        {
            Background = GameUi.ItemFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(1),
            Width = ArrowWidth,
            Margin = new Thickness(1, 0, 0, 0),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                // 화살표는 게임 비트맵 글꼴에 없는 글자라 윈도 글꼴로 찍는다.
                Text = up ? "↑" : "↓",
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Bold,
                FontSize = 9,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        box.MouseLeftButtonDown += (_, e) => e.Handled = true;
        box.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
        return box;
    }

    /// <summary>보너스 포인트를 능력치에 넣거나 도로 뺀다.</summary>
    private void Move(int which, int by)
    {
        if (by > 0)
        {
            if (_left <= 0 || _stats[which] >= Ability.Max) return;
            _stats[which]++;
            _left--;
        }
        else
        {
            if (_stats[which] <= Ability.Min) return;
            _stats[which]--;
            _left++;
        }
        Sync();
    }

    /// <summary>
    /// 직업을 고른다. 게임이 그렇듯 <b>능력치는 다시 안 굴린다</b> — 이 고름은 다음
    /// 걸음의 기본 기술에만 걸린다.
    /// </summary>
    private void ChooseJob(int pick)
    {
        _job = pick;
        Sync();
    }

    private void Sync()
    {
        for (int i = 0; i < Ability.Shown; i++) _values[i].Text = $"{_stats[i]}";
        _bonus.Text = $"{_left}";

        // 고른 직업은 <b>띠 무늬를 갈아</b> 알린다. 게임은 <b>고른 것이 밝은 베이지</b>고
        // 안 고른 것이 어두운 쪽이다 — 우리가 거꾸로 걸고 있었다.
        for (int i = 0; i < _jobs.Count; i++)
            _jobs[i].Band = i == _job ? BandStyle.Button : BandStyle.Alt;
    }

    /// <summary>"다음" — 보너스를 다 안 썼으면 게임처럼 한 번 묻는다.</summary>
    private void Next()
    {
        if (_left > 0 && !ConfirmDialog.Ask(this,
                $"보너스 포인트가 {_left} 남아 있습니다만{Environment.NewLine}" +
                "다음 설정으로 이동해도 괜찮습니까?"))
            return;

        _ok = true;
        Close();
    }

    /// <summary>
    /// 능력치 화면을 띄운다. "다음" 을 누르면 <paramref name="player"/> 에 적고, 남은
    /// 보너스 포인트를 낸다(무른 것이면 -1).
    /// </summary>
    public static int Show(Window owner, Player player, Random rng)
    {
        var dialog = new AbilityMakeDialog(player, rng) { Owner = owner };
        dialog.ShowDialog();
        if (!dialog._ok) return -1;

        player.JobIndex = dialog._job;
        player.SetAbilities(dialog._stats);
        player.SetGold(Ability.GoldFor(dialog._stats[Ability.Body]));
        return dialog._left;
    }
}
