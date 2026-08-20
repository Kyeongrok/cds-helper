using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 조합 → 수련 에서 뜨는 "습득가능 기술" 창. 한 줄을 고르고 결정을 누르면 조합장이 값과
/// 걸리는 달을 이르고, 그래도 좋다면 배운다.
/// </summary>
/// <remarks>
/// 한 자리 올리는 데 <see cref="Skill.Price"/> 닢이 들고, 걸리는 달은 자리마다 다르다
/// (0→1 석 달 · →2 여섯 달 · →3 열두 달). 배우고 나면 그만큼 날이 간다.
/// </remarks>
public sealed class SkillLearnDialog : Window
{
    private readonly Player _player;
    private readonly IReadOnlyList<string> _skills;
    private readonly Border _decide;
    private readonly Dictionary<string, Border> _rows = [];
    private string? _picked;

    /// <summary>이 창에서 하나라도 배웠는지. 조합장이 나가는 말을 고르는 데 쓴다.</summary>
    private bool _learned;

    private SkillLearnDialog(Player player, IReadOnlyList<string> skills)
    {
        _player = player;
        _skills = skills;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        _decide = GameUi.PushButton("결정", Decide);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 8),
        };
        buttons.Children.Add(_decide);
        buttons.Children.Add(GameUi.PushButton("종료", Close));

        var list = new StackPanel();
        foreach (var name in _skills) list.Children.Add(MakeRow(name));

        var title = GameUi.TitleBar("습득가능 기술", Close);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(new Border
        {
            Background = GameUi.PageFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4, 4, 4, 0),
            Padding = new Thickness(6, 4, 6, 4),
            // 게임처럼 열 줄쯤 보이고 나머지는 굴려서 본다.
            Child = new ScrollViewer
            {
                Height = 260,
                Width = 300,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = list,
            },
        });
        stack.Children.Add(buttons);

        Content = new Border
        {
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = stack,
        };

        SetDecide(enabled: false);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        MouseRightButtonUp += (_, _) => Close();
    }

    /// <summary>기술 한 줄. "이름 ( LVn )" 을 왼쪽에 붙여 낸다(게임은 오른쪽이다).</summary>
    private Border MakeRow(string name)
    {
        var text = new TextBlock
        {
            Text = $"{name} ( LV{_player.LevelOf(name)} )",
            Foreground = Brushes.Black,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(10, 1, 6, 1),
        };
        var row = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = text };
        row.MouseLeftButtonUp += (_, e) => { e.Handled = true; Pick(name); };
        _rows[name] = row;
        return row;
    }

    private void Pick(string name)
    {
        _picked = name;
        foreach (var (key, row) in _rows)
        {
            bool on = key == name;
            row.Background = on ? GameUi.MenuBack : Brushes.Transparent;
            ((TextBlock)row.Child).Foreground = on ? GameUi.Text : Brushes.Black;
        }
        SetDecide(enabled: true);
    }

    private void SetDecide(bool enabled)
    {
        ((TextBlock)_decide.Child).Foreground = enabled ? Brushes.Black : Brushes.Gray;
        _decide.Cursor = enabled ? Cursors.Hand : Cursors.Arrow;
    }

    private void Decide()
    {
        if (_picked is not { } skill) return;

        int level = _player.LevelOf(skill);
        if (level >= Skill.MaxLevel)
        {
            NoticeDialog.Show(this, $"{skill}은(는) 더 배울 것이 없네.");
            return;
        }

        int months = Skill.MonthsFor(level + 1);
        if (!ConfirmDialog.Ask(this,
                $"배우고 싶다면 금화 {Skill.Price}닢 필요하네. " +
                $"습득하는데는, {months}개월 정도 필요하네. 그래도 좋다면 가르쳐 주지. 괜찮은가?"))
            return;

        switch (_player.Learn(skill))
        {
            case LearnResult.Ok:
                _learned = true;
                ((TextBlock)_rows[skill].Child).Text = $"{skill} ( LV{_player.LevelOf(skill)} )";
                NoticeDialog.Show(this, $"{skill}을 습득했다!");
                break;
            case LearnResult.NotEnoughGold:
                NoticeDialog.Show(this, "금화가 모자라는군. 그것으로는 안 되네.");
                break;
            case LearnResult.Mastered:
                NoticeDialog.Show(this, $"{skill}은(는) 더 배울 것이 없네.");
                break;
        }
    }

    /// <summary>
    /// 습득가능 기술 창을 띄운다. <paramref name="skills"/> 는 그 건물이 가르치는 것이다 —
    /// 게임은 건물 표의 비트마스크로 도시마다 다르게 준다.
    /// </summary>
    /// <returns>하나라도 배웠으면 true. 부르는 쪽이 나가는 말을 고르는 데 쓴다.</returns>
    public static bool Show(Window owner, Player player, IReadOnlyList<string> skills)
    {
        if (skills.Count == 0)
        {
            NoticeDialog.Show(owner, "여기서 가르치는 것은 없네.");
            return false;
        }

        var dlg = new SkillLearnDialog(player, skills) { Owner = owner };
        dlg.ShowDialog();
        return dlg._learned;
    }
}
