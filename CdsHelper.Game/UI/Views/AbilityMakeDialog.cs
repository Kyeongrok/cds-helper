using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
///   45d548      값 = 밑값 + rand(나이) + 직업보정 + 나이보정 + 50, 20~100 으로 자른다
///   45d5d5      보너스 = 합으로 갈린다 — 잘 굴렸을수록 덜 준다
/// </code>
/// <b>직업을 바꾸면 능력치를 다시 굴린다</b> — 보정이 직업마다 다르기 때문이다.
/// </remarks>
internal sealed class AbilityMakeDialog : InfoDialog
{
    private const double BoardWidth = 520, BoardHeight = 280;

    private readonly Random _rng;
    private readonly int _age;

    private readonly TextBlock[] _values = new TextBlock[Ability.Shown];
    private readonly TextBlock _bonus = new()
    {
        Foreground = Brushes.Black,
        FontWeight = FontWeights.Bold,
        FontSize = 16,
        HorizontalAlignment = HorizontalAlignment.Right,
        Margin = new Thickness(0, 0, 8, 0),
    };
    private readonly List<Border> _jobs = [];

    private int[] _stats;
    private int _left, _job;
    private bool _ok;

    private AbilityMakeDialog(Player player, Random rng)
    {
        _rng = rng;
        _age = player.Age;
        _job = player.JobIndex;
        _stats = Ability.Roll(Job.Of(_job), _age, rng);
        _left = Ability.BonusFor(_stats, rng);

        var left = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        for (int i = 0; i < Ability.Shown; i++)
        {
            int which = i;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            row.Children.Add(new TextBlock
            {
                Text = Ability.Names[i],
                Foreground = Ink,
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                Width = 54,
                VerticalAlignment = VerticalAlignment.Center,
            });
            _values[i] = new TextBlock
            {
                Foreground = Ink,
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                Width = 40,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            row.Children.Add(_values[i]);
            row.Children.Add(Arrow("↑", () => Move(which, +1)));
            row.Children.Add(Arrow("↓", () => Move(which, -1)));
            left.Children.Add(row);
        }

        left.Children.Add(new Border
        {
            BorderBrush = Ink,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(0, 12, 8, 0),
            Padding = new Thickness(6, 2, 6, 2),
            Child = Bonus(),
        });

        var right = new StackPanel { Margin = new Thickness(24, 2, 0, 0) };
        for (int i = 0; i < Job.Choosable; i++)
        {
            int pick = i;
            var cell = new GameButton(Job.All[i].Name, () => ChooseJob(pick), BandStyle.Button, 140);
            cell.Margin = new Thickness(0, 4, 0, 4);
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
        stack.Children.Add(new TextBlock
        {
            Text = "보너스",
            Foreground = Ink,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
        });
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new TextBlock
        {
            Text = "  포인트:",
            Foreground = Ink,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        });
        _bonus.MinWidth = 40;
        line.Children.Add(_bonus);
        stack.Children.Add(line);
        return stack;
    }

    private Border Arrow(string glyph, Action run)
    {
        var box = new Border
        {
            Background = GameUi.ItemFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(2),
            Width = 22,
            Margin = new Thickness(2, 0, 0, 0),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = glyph,
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
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
    /// 직업을 바꾼다 — 보정이 다르므로 능력치를 다시 굴린다.
    /// </summary>
    private void ChooseJob(int pick)
    {
        _job = pick;
        _stats = Ability.Roll(Job.Of(_job), _age, _rng);
        _left = Ability.BonusFor(_stats, _rng);
        Sync();
    }

    private void Sync()
    {
        for (int i = 0; i < Ability.Shown; i++) _values[i].Text = $"{_stats[i]}";
        _bonus.Text = $"{_left}";
        for (int i = 0; i < _jobs.Count; i++)
            _jobs[i].BorderBrush = i == _job ? GameUi.PageFill : GameUi.ItemEdge;
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
        return dialog._left;
    }
}
