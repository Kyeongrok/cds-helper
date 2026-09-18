using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 모드 창 — <b>원본에 없는 편의 기능</b>을 켜고 끈다.
/// </summary>
/// <remarks>
/// 개발 창에 섞여 있던 것 가운데 <b>놀 때 쓰는 것</b>만 따로 뽑아 왔다. 개발 창은 값을
/// 손으로 밀어 넣어 시험하는 데고, 여기는 판을 그대로 두고 보기를 거드는 데다.
///
/// 지금 든 것은 컨디션 막대 · 미니맵 · 발견물 지도 · 여급 수첩 · 기능·언어 쪽지 ·
/// 출입 일수 · 인물 이동 두 줄이다.
/// </remarks>
public sealed class ModDialog : GameWindow
{
    /// <summary>모드 창이 만지는 것들.</summary>
    public sealed class Options
    {
        /// <summary>제독 컨디션(HP) 상자.</summary>
        public Func<bool> ConditionOn { get; init; } = () => false;
        public Action<bool> SetCondition { get; init; } = _ => { };

        /// <summary>미니맵 — 발견물 지도를 작게 잘라 배를 따라간다.</summary>
        public Func<bool> MiniMapOn { get; init; } = () => false;
        public Action<bool> SetMiniMap { get; init; } = _ => { };
    }

    private ModDialog(Options options)
    {
        Title = "모드";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var rows = new StackPanel { Margin = new Thickness(12, 10, 12, 4) };

        // 컨디션 — 제독 HP(0x005B60D8)를 지도 왼쪽 아래에 막대로 띄운다. 300·100 문턱도 같이 그린다.
        rows.Children.Add(Toggle("컨디션", options.ConditionOn(), options.SetCondition,
            "제독 컨디션(HP, 0~2000)을 지도 왼쪽 아래에 띄웁니다. 300·100 아래면 부관이 쉬라고 하고, 0 이면 쓰러집니다"));

        // 미니맵 — D 로 여는 발견물 지도를 항해·뭍 이동 중에 오른쪽 아래에 작게 띄운다.
        rows.Children.Add(Toggle("미니맵", options.MiniMapOn(), options.SetMiniMap,
            "항해·뭍 이동 중에 발견물 지도를 지도 오른쪽 아래에 작게 띄웁니다. 배를 가운데 두고 따라갑니다(빨강 찾음 · 회색 아직 · 파랑 내 자리)"));

        // 발견물 지도 — 햄버거 줄과 단축키를 함께 여닫는다. 원본 항해지도는 표식을 안 찍는다.
        rows.Children.Add(Toggle("발견물 지도", GameSettings.ShowDiscoveryMapMenu,
            on => GameSettings.ShowDiscoveryMapMenu = on,
            "햄버거에 「발견물 지도」 줄을 냅니다. 어디에 무엇이 있는지 표식으로 찍어 보여 줍니다"
            + " — 끄면 줄도 단축키도 안 먹습니다"));

        // 여급 수첩 — 낯을 튼 여급과 궁합을 모아 본다. 원본에는 없는 창이다.
        rows.Children.Add(Toggle("여급 수첩", GameSettings.ShowBarmaidBookMenu,
            on => GameSettings.ShowBarmaidBookMenu = on,
            "햄버거에 「여급 수첩」 줄을 냅니다. 낯을 튼 여급의 친밀도와 궁합을 모아 봅니다"));

        // 기능·언어 — 켜 두면 도시에 들어갈 때 도시 그림 왼쪽에 쪽지로 뜬다.
        rows.Children.Add(Toggle("기능·언어", GameSettings.ShowSkillOverlay,
            on => GameSettings.ShowSkillOverlay = on,
            "도시에 들어가면 제독과 부하 넷의 기능·언어를 도시 그림 왼쪽에 띄웁니다. 끌어 옮기면 그 자리를 기억합니다"));

        // 마을·항구에 들고 날 때 보내는 날수. 원본은 열흘씩이라 오가는 시험이 더디다.
        rows.Children.Add(Select("출입 일수",
            [.. Enumerable.Range(GameSettings.MinPortDays,
                                 GameSettings.MaxPortDays - GameSettings.MinPortDays + 1)
                          .Select(n => n == GameSettings.DefaultPortDays ? $"{n}일 (원본)" : $"{n}일")],
            GameSettings.PortDays - GameSettings.MinPortDays,
            i => GameSettings.PortDays = i + GameSettings.MinPortDays,
            $"항구·마을에 들어가고 나올 때 각각 지나는 날수. 원본 기본값 {GameSettings.DefaultPortDays}일입니다."
            + " 바꾼 값은 다음 출입부터 곧바로 듭니다."));

        // 인물 이동 — 떠날지 굴리는 때와 확률. 원본은 매월 1일 5분의 1이다.
        // 첫 줄(0)이 원본 「매월 1일」이고, 그 뒤 줄 번호가 곧 날수다.
        rows.Children.Add(Select("이동 주기",
            ["매월 1일 (원본)", .. Enumerable.Range(1, GameSettings.MaxPersonRollDays).Select(n => $"{n}일마다")],
            GameSettings.PersonRollDays,
            i => GameSettings.PersonRollDays = i,
            "인물(14~200번)이 떠날지 굴리는 때. 원본은 매월 1일입니다. N일마다는 1480년 1월 1일부터 셉니다."
            + " 역사 항해자 대본은 늘 매월 1일입니다."));
        rows.Children.Add(Select("떠날 확률",
            [.. Enumerable.Range(GameSettings.MinPersonMoveOdds,
                                 GameSettings.MaxPersonMoveOdds - GameSettings.MinPersonMoveOdds + 1)
                          .Select(n => n == 1 ? "1분의 1 (반드시)" : $"{n}분의 1")],
            GameSettings.PersonMoveOdds - GameSettings.MinPersonMoveOdds,
            i => GameSettings.PersonMoveOdds = i + GameSettings.MinPersonMoveOdds,
            $"굴릴 때마다 떠날 확률. 원본은 {GameSettings.DefaultPersonMoveOdds}분의 1입니다."));

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 12),
        };
        buttons.Children.Add(GameUi.PushButton("닫기", Close, 96));

        var title = GameUi.TitleBar("모드", Close);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(rows);
        stack.Children.Add(buttons);

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = stack,
        };

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
    }

    /// <summary>켜고 끄는 줄 하나.</summary>
    private static CheckBox Toggle(string label, bool on, Action<bool> set, string tip)
    {
        var box = new CheckBox
        {
            Content = label,
            IsChecked = on,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            Margin = new Thickness(0, 8, 0, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = tip,
        };
        box.Checked += (_, _) => set(true);
        box.Unchecked += (_, _) => set(false);
        return box;
    }

    /// <summary>고르는 줄 하나 — 이름과 펼침 상자. 고르면 곧바로 설정에 남긴다.</summary>
    private static UIElement Select(string label, IReadOnlyList<string> items, int selected,
                                    Action<int> set, string tip)
    {
        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 2),
            ToolTip = tip,
        };
        line.Children.Add(new TextBlock
        {
            Text = label,
            Width = 64,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var box = new ComboBox
        {
            Width = 160,
            Margin = new Thickness(6, 0, 6, 0),
            Padding = new Thickness(6, 3, 6, 3),
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        foreach (string item in items) box.Items.Add(item);
        box.SelectedIndex = Math.Clamp(selected, 0, items.Count - 1);
        box.SelectionChanged += (_, _) => { if (box.SelectedIndex >= 0) set(box.SelectedIndex); };

        line.Children.Add(box);
        return line;
    }

    public static void Show(Window owner, Options options) =>
        new ModDialog(options) { Owner = owner }.ShowDialog();
}
