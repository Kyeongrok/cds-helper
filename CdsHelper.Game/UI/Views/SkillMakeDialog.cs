using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
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
    /// <summary>게임 갈무리에 이 화면들에는 닫기(X)가 없다.</summary>
    protected override bool ShowClose => false;

    /// <summary>판과 단추 줄의 여백. 게임 것이 훨씬 촘촘하다.</summary>
    protected override Thickness BoardPad => new(8, 6, 8, 2);

    protected override Thickness ButtonPad => new(0, 2, 8, 6);

    /// <summary>아래 단추의 폭과 사이. 게임 것은 마구리 둘에 가운데 두 칸(48)이다.</summary>
    private const double FootWidth = 48;

    private static readonly Thickness FootGap = new(3, 0, 0, 0);

    /// <summary>
    /// 판 크기(그림 점). 잰 값이 <b>1.75배로 늘어난 화면</b>에서 나온 것이라 도로 나눴다 —
    /// 띠 단추만 제 크기로 그려져 있어 혼자 작아 보였다.
    /// </summary>
    private const double BoardWidth = 400, BoardHeight = 232;

    /// <summary>줄 속 칸 폭 — 이름 · 자리. 언어 이름이 여덟 자(128)까지 온다.</summary>
    private const double SkillNameWidth = 76, TongueNameWidth = 136, ValueWidth = 40;

    private readonly int[] _skills = new int[Skill.Names.Length];
    private readonly int[] _tongues = new int[Skill.Languages.Length];
    private readonly int[] _skillFloor = new int[Skill.Names.Length];
    private readonly int[] _tongueFloor = new int[Skill.Languages.Length];

    private readonly GameUi.GameLabel[] _skillText = new GameUi.GameLabel[Skill.Names.Length];
    private readonly GameUi.GameLabel[] _tongueText = new GameUi.GameLabel[Skill.Languages.Length];
    private readonly GameUi.GameLabel _bonus = new(GameFont.WhiteColor)
    {
        FallbackBrush = Ink,
        MinWidth = ValueWidth,
        HorizontalAlignment = HorizontalAlignment.Right,
    };

    private readonly int _cap;
    private int _left;
    private bool _ok;

    private SkillMakeDialog(Player player)
    {
        // 보너스는 앞 걸음에서 남겨 온 것이 아니라 여기서 새로 센다(0x0045DDD9).
        _left = Skill.BonusFor(player.Age, player.AbilityOf(Ability.Mind));
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

        var right = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
        for (int i = 0; i < Skill.Languages.Length; i++)
            right.Children.Add(SkillLine(Skill.Languages[i], i, _tongueText, _tongues, _tongueFloor,
                                    i < Skill.LanguagesAtStart, false));

        var lists = new StackPanel { Orientation = Orientation.Horizontal };
        lists.Children.Add(Framed(left, SkillNameWidth + ValueWidth + 44));
        lists.Children.Add(Framed(right, TongueNameWidth + ValueWidth + 44));

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
              new GameButton("취소", Close, width: FootWidth) { Margin = FootGap }, new GameButton("다음", Next, width: FootWidth) { Margin = FootGap });

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
        stack.Children.Add(new GameUi.GameLabel(GameFont.WhiteColor)
        {
            Text = "보너스",
            FallbackBrush = Ink,
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new GameUi.GameLabel(GameFont.WhiteColor)
        {
            Text = "  포인트:",
            FallbackBrush = Ink,
        });
        line.Children.Add(_bonus);
        stack.Children.Add(line);
        return stack;
    }

    /// <summary>줄 하나 — 이름 · <c>자리(밑자리)</c> · 올리고 내리는 화살표.</summary>
    private UIElement SkillLine(string name, int at, GameUi.GameLabel[] texts, int[] values,
                                int[] floors, bool on, bool skill)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };

        row.Children.Add(new GameUi.GameLabel(on ? GameFont.BlackColor : GameFont.ButtonColor)
        {
            Text = name,
            FallbackBrush = on ? Brushes.Black : Faint,
            Width = skill ? SkillNameWidth : TongueNameWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
        });

        texts[at] = new GameUi.GameLabel(on ? GameFont.BlackColor : GameFont.ButtonColor)
        {
            FallbackBrush = on ? Brushes.Black : Faint,
            Width = ValueWidth,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        row.Children.Add(texts[at]);

        if (on)
        {
            row.Children.Add(Arrow(UiSprites.IconUp, () => Move(values, floors, at, +1)));
            row.Children.Add(Arrow(UiSprites.IconDown, () => Move(values, floors, at, -1)));
        }
        return row;
    }

    /// <summary>못 올리는 줄의 글씨색(글꼴을 못 읽었을 때).</summary>
    private static readonly Brush Faint = Frozen(Color.FromRgb(0xA0, 0x98, 0x88));

    /// <summary>올리고 내리는 화살표. 게임 조각(<c>MISC.CDS</c> 파트 3)을 그대로 건다.</summary>
    private FrameworkElement Arrow(int icon, Action run)
    {
        FrameworkElement box = GameUi.GameIcon(icon) ?? (FrameworkElement)new Border
        {
            Background = GameUi.ItemFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(1),
            Width = UiSprites.IconWidth,
            Height = UiSprites.IconHeight,
            Child = new TextBlock
            {
                Text = icon == UiSprites.IconUp ? "↑" : "↓",
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };
        box.Margin = new Thickness(1, 0, 0, 0);
        box.Cursor = Cursors.Hand;
        box.VerticalAlignment = VerticalAlignment.Center;
        box.MouseLeftButtonDown += (_, e) => e.Handled = true;
        box.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
        return box;
    }

    /// <summary>
    /// <b>한 자리 올리는 값은 그 자리 값만큼이다</b> — 0→1 은 1 점, 1→2 는 2 점,
    /// 2→3 은 3 점이다. 내릴 때도 같은 값으로 되돌려 준다.
    /// </summary>
    /// <remarks>
    /// <code>
    /// 45dfc2  ecx = 밑자리[i] + 올린만큼[i]          ; 지금 자리
    /// 45dfd3  ecx >= 3 이면 못 올린다
    /// 45e021  ecx++                                  ; 드는 점수 = 지금 자리 + 1
    /// 45e028  남은 보너스보다 크면 못 올린다
    /// 45e031  남은 보너스 -= ecx
    ///
    /// 45e060  edx = 밑자리[i] + 올린만큼[i]          ; 내릴 때는 그 값을 그대로 돌려준다
    /// 45e068  남은 보너스 += edx
    /// </code>
    /// 그래서 한 기술을 3 까지 올리려면 <b>여섯 점</b>이 든다. 보너스가 세 점이면
    /// 세 자리를 하나씩 올리거나 한 자리를 2 까지만 올릴 수 있다.
    /// </remarks>
    private static int CostOf(int level) => level + 1;

    /// <summary>한 자리 올리거나 내린다. 밑자리 밑으로는 못 내리고 상한을 못 넘는다.</summary>
    private void Move(int[] values, int[] floors, int at, int by)
    {
        if (by > 0)
        {
            if (values[at] >= Skill.MaxLevel || _left < CostOf(values[at])) return;
            if (Total() >= _cap)
            {
                NoticeDialog.Show(this, ReferenceEquals(values, _skills)
                    ? "더 이상 지식을 습득할 수 없습니다"
                    : "더 이상 언어를 습득할 수 없습니다");
                return;
            }
            _left -= CostOf(values[at]);
            values[at]++;
        }
        else
        {
            if (values[at] <= floors[at]) return;
            values[at]--;
            _left += CostOf(values[at]);
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
