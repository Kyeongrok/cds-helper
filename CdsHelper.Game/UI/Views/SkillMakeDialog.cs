using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// NEW GAME 셋째 걸음 — 기술 열셋과 언어 열넷에 보너스 포인트를 찍는다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0045DE20</c> 이다.
/// <code>
///   0x00560A10  기술 열셋 — 항해술 운용술 검술 포술 사격술 의학 웅변
///                           측량 역사학 회계 조선기술 신학 과학
///   0x00560A48  언어 열넷 — 스페인어 포르투갈어 로망스어 게르만어 슬라브·그리스어 아랍어
///                           페르시아어 중국어 힌두어 위굴어 …토착어 넷
///   45dfe4      열셋을 다 더한다
///   45dff6      eax = 지력
///   45e002      eax = eax * 3 / 5
///   45e008      넘으면 "더 이상 지식을 습득할 수 없습니다"   0x005718E0
/// </code>
/// <b>지력이 배울 수 있는 총량을 정한다</b> — 자리를 다 더한 값이 <c>지력 x 3 / 5</c> 를
/// 못 넘는다. 언어에도 같은 꼴의 검사가 따로 있다("더 이상 언어를 습득할 수 없습니다").
///
/// 괄호 안의 수는 <b>처음부터 들고 있던 자리</b>다 — 직업이 준 기술과 국적이 준 언어라
/// 그 밑으로는 못 내린다. 페르시아어부터는 화면에서 흐리다(놀이 안에서 배워야 한다).
/// </remarks>
internal sealed class SkillMakeDialog : InfoDialog
{
    private const double BoardWidth = 700, BoardHeight = 400;

    private readonly int[] _skills = new int[Skill.Names.Length];
    private readonly int[] _tongues = new int[Skill.Languages.Length];
    private readonly int[] _skillFloor = new int[Skill.Names.Length];
    private readonly int[] _tongueFloor = new int[Skill.Languages.Length];

    private readonly TextBlock[] _skillText = new TextBlock[Skill.Names.Length];
    private readonly TextBlock[] _tongueText = new TextBlock[Skill.Languages.Length];
    private readonly TextBlock _bonus = new()
    {
        Foreground = Ink,
        FontWeight = FontWeights.Bold,
        FontSize = 15,
        MinWidth = 40,
        TextAlignment = TextAlignment.Right,
    };

    private readonly int _cap;
    private int _left;
    private bool _ok;

    private SkillMakeDialog(Player player)
    {
        // 보너스는 앞 걸음에서 남겨 온 것이 아니라 여기서 새로 센다(0x0045DDD9).
        _left = Skill.BonusFor(player.Age, player.AbilityOf(Ability.Mind),
                               player.Work.Bias[Ability.Mind]);
        _cap = Skill.CapFor(player.AbilityOf(Ability.Mind));

        foreach (var (skill, level) in player.Work.Skills)
            _skills[skill] = _skillFloor[skill] = level;
        foreach (var (tongue, level) in Skill.TongueOf(player.Nation))
            _tongues[tongue] = _tongueFloor[tongue] = level;
        // 직업이 주는 언어는 국적 것과 따로다 — 탐험가는 로망스어, 발굴자는 슬라브·그리스어다.
        foreach (var (tongue, level) in player.Work.Tongues)
            _tongues[tongue] = _tongueFloor[tongue] = level;

        var left = new StackPanel();
        for (int i = 0; i < Skill.Names.Length; i++)
            left.Children.Add(SkillLine(Skill.Names[i], i, _skillText, _skills, _skillFloor, true, true));

        var right = new StackPanel { Margin = new Thickness(24, 0, 0, 0) };
        for (int i = 0; i < Skill.Languages.Length; i++)
            right.Children.Add(SkillLine(Skill.Languages[i], i, _tongueText, _tongues, _tongueFloor,
                                    i < Skill.LanguagesAtStart, false));

        var lists = new StackPanel { Orientation = Orientation.Horizontal };
        lists.Children.Add(Framed(left, 210));
        lists.Children.Add(Framed(right, 250));

        var box = new StackPanel();
        box.Children.Add(lists);
        box.Children.Add(new Border
        {
            BorderBrush = Ink,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(6, 2, 6, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = BonusBox(),
        });

        Build("", box, BoardWidth, BoardHeight,
              new GameButton("취소", Close), new GameButton("다음", Next));

        Sync();
    }

    private static UIElement Framed(UIElement child, double width) => new Border
    {
        Background = GameUi.PageFill,
        BorderBrush = GameUi.ItemEdge,
        BorderThickness = new Thickness(2),
        Width = width,
        Padding = new Thickness(4, 2, 4, 2),
        Child = child,
    };

    private UIElement BonusBox()
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
        line.Children.Add(_bonus);
        stack.Children.Add(line);
        return stack;
    }

    /// <summary>줄 하나 — 이름 · <c>자리(밑자리)</c> · 올리고 내리는 화살표.</summary>
    private UIElement SkillLine(string name, int at, TextBlock[] texts, int[] values, int[] floors,
                           bool on, bool skill)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 1) };
        var faint = new SolidColorBrush(Color.FromRgb(0xA0, 0x98, 0x88));

        row.Children.Add(new TextBlock
        {
            Text = name,
            Foreground = on ? Brushes.Black : faint,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Width = skill ? 92 : 130,
            VerticalAlignment = VerticalAlignment.Center,
        });

        texts[at] = new TextBlock
        {
            Foreground = on ? Brushes.Black : faint,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Width = 46,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(texts[at]);

        if (on)
        {
            row.Children.Add(Arrow("↑", () => Move(values, floors, at, +1)));
            row.Children.Add(Arrow("↓", () => Move(values, floors, at, -1)));
        }
        return row;
    }

    private Border Arrow(string glyph, Action run)
    {
        var box = new Border
        {
            Background = GameUi.ItemFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(1),
            Width = 18,
            Margin = new Thickness(1, 0, 0, 0),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = glyph,
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };
        box.MouseLeftButtonDown += (_, e) => e.Handled = true;
        box.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
        return box;
    }

    /// <summary>한 자리 올리거나 내린다. 밑자리 밑으로는 못 내리고 상한을 못 넘는다.</summary>
    private void Move(int[] values, int[] floors, int at, int by)
    {
        if (by > 0)
        {
            if (_left <= 0 || values[at] >= Skill.MaxLevel) return;
            if (Total() >= _cap)
            {
                NoticeDialog.Show(this, ReferenceEquals(values, _skills)
                    ? "더 이상 지식을 습득할 수 없습니다"
                    : "더 이상 언어를 습득할 수 없습니다");
                return;
            }
            values[at]++;
            _left--;
        }
        else
        {
            if (values[at] <= floors[at]) return;
            values[at]--;
            _left++;
        }
        Sync();
    }

    /// <summary>기술과 언어의 자리를 다 더한 값.</summary>
    private int Total() => _skills.Sum() + _tongues.Sum();

    private void Sync()
    {
        for (int i = 0; i < _skills.Length; i++)
            _skillText[i].Text = $"{_skills[i]}({_skillFloor[i]})";
        for (int i = 0; i < _tongues.Length; i++)
            _tongueText[i].Text = $"{_tongues[i]}({_tongueFloor[i]})";
        _bonus.Text = $"{_left}";
    }

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
    /// 기술 화면을 띄운다. "다음" 을 누르면 <paramref name="player"/> 에 적고 true.
    /// </summary>
    /// <remarks>
    /// 보너스 포인트는 여기서 <b>새로 센다</b> — 나이와 지력과 직업 보정으로 나온다
    /// (<see cref="Skill.BonusFor"/>).
    /// </remarks>
    public static bool Show(Window owner, Player player)
    {
        var dialog = new SkillMakeDialog(player) { Owner = owner };
        dialog.ShowDialog();
        if (!dialog._ok) return false;

        for (int i = 0; i < Skill.Names.Length; i++)
            player.SetSkill(Skill.Names[i], dialog._skills[i]);
        for (int i = 0; i < Skill.Languages.Length; i++)
            player.SetTongue(Skill.Languages[i], dialog._tongues[i]);
        return true;
    }
}
