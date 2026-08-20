using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 소지품 정보 창 — 왼쪽에 소지품, 오른쪽에 발견물을 나란히 늘어놓는다.
/// </summary>
/// <remarks>
/// 도시 커맨드 창의 "소지품 정보" 로 연다. 게임 화면을 그대로 옮겼다 — 양피지 바탕에
/// 제목 띠 둘이 나란히 서고, 밑에 결정·중단이 있다.
/// <code>
///   ┌ 소지품일람 ─┬ 발견물일람 ─┐
///   │ 육분의      │            │
///   │ 사해사본     │            │
///   │ …          │            │
///   └─────────┴──────────┘
///          [결정]  [중단]
/// </code>
/// <b>발견물 쪽은 비워 둔다.</b> 발견물을 놀이 안에서 얻는 길(상륙해서 찾기·발표)을 아직
/// 흉내내지 않아서, 채울 것이 없는데 칸만 채우면 없는 놀이를 있는 것처럼 보이게 한다.
/// 자리는 게임처럼 잡아 두었으니 나중에 <see cref="Discoveries"/> 에 넣기만 하면 된다.
///
/// 줄을 고르고 결정을 누르면 그 아이템 창이 뜬다(<see cref="ItemInfoDialog"/>) —
/// 시장에서 고른 뒤에 뜨는 것과 같은 창이다.
/// </remarks>
public sealed class BelongingsDialog : Window
{
    /// <summary>고른 줄에 씌우는 남색. 시장 목록과 같은 색이다.</summary>
    private static readonly Brush Picked = Freeze(Color.FromRgb(0x3A, 0x5A, 0x9A));

    /// <summary>글꼴을 못 읽었을 때 물러설 글씨색.</summary>
    private static readonly Brush Ink = Freeze(Color.FromRgb(0x20, 0x18, 0x10));

    private static SolidColorBrush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private readonly ItemTable? _items;
    private readonly ItemDescriptions? _descriptions;
    private readonly ItemArt? _art;

    private readonly List<(int ItemId, Border Row, string Name)> _rows = [];
    private readonly GameUi.BandButton _decide;
    private int _at = -1;

    private BelongingsDialog(Player player, ItemTable? items,
                             ItemDescriptions? descriptions, ItemArt? art)
    {
        _items = items;
        _descriptions = descriptions;
        _art = art;

        Title = "소지품 정보";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Width = 820;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.PageFill;

        var columns = new Grid { Margin = new Thickness(2, 2, 2, 2) };
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition());

        var left = Column("소지품일람", out var leftList);
        Grid.SetColumn(left, 0);
        columns.Children.Add(left);

        var right = Column("발견물일람", out var rightList);
        Grid.SetColumn(right, 1);
        columns.Children.Add(right);
        Discoveries = rightList;

        foreach (int id in player.Items)
        {
            var row = Row(id);
            _rows.Add(row);
            leftList.Children.Add(row.Row);
        }
        if (_rows.Count == 0)
            leftList.Children.Add(new TextBlock
            {
                Text = "  지닌 것이 없다.",
                Foreground = Ink,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                Margin = new Thickness(6, 8, 6, 6),
            });

        _decide = new GameUi.BandButton("결정", Decide, 130) { On = false };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 12),
        };
        buttons.Children.Add(_decide);
        buttons.Children.Add(new GameUi.BandButton("중단", Close, 130));

        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(columns);

        Content = new Border
        {
            Background = GameUi.PageFill,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = root,
        };

        GameUi.EnableDrag(this, columns);
        KeyDown += OnKey;
    }

    /// <summary>
    /// 발견물 칸. 지금은 비어 있다 — 발견물을 얻는 길이 생기면 여기에 줄을 넣으면 된다.
    /// </summary>
    public StackPanel Discoveries { get; }

    /// <summary>제목 띠 하나와 그 밑의 줄 칸.</summary>
    private static FrameworkElement Column(string title, out StackPanel list)
    {
        list = new StackPanel { Margin = new Thickness(4, 4, 4, 2) };

        // 원본 조각을 못 읽었으면 띠 대신 글자만 낸다.
        FrameworkElement head = GameUi.TitleFrame(GameUi.Sprites, title) ?? new Border
        {
            Background = GameUi.MenuBack,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = title,
                Foreground = GameUi.Text,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 3),
            },
        };

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = list,
        };

        var panel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(head, Dock.Top);
        panel.Children.Add(head);
        panel.Children.Add(scroll);
        return panel;
    }

    /// <summary>줄 하나 — 아이템 이름.</summary>
    private (int ItemId, Border Row, string Name) Row(int itemId)
    {
        string name = _items?.Find(itemId)?.Name ?? $"아이템 {itemId}";
        var row = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(0, 2, 0, 2),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = Label(name, picked: false),
        };
        row.MouseLeftButtonDown += (_, e) => e.Handled = true;
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Pick(_rows.FindIndex(r => ReferenceEquals(r.Row, row)));
        };
        return (itemId, row, name);
    }

    /// <summary>
    /// 줄 글씨. 게임 비트맵 글꼴로 찍는다 — 윈도 글꼴은 같은 자리에서 더 크고 결이 다르다.
    /// </summary>
    /// <remarks>
    /// 고른 줄은 남색 위라 글씨를 흰빛으로 뒤집는다. 색이 생길 때 정해지므로 고를 때마다
    /// 새로 짓는다 — 한 번 고를 때 바뀌는 줄은 둘(놓은 줄과 잡은 줄)뿐이라 값싸다.
    /// </remarks>
    private static FrameworkElement Label(string name, bool picked)
    {
        var label = new GameUi.GameLabel(picked ? GameFont.WhiteColor : GameFont.ButtonColor)
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(6, 0, 6, 0),
            Text = name,
        };
        // 글꼴을 못 읽었으면 GameLabel 이 윈도 글꼴로 물러선다. 그때 색을 맞춰 준다.
        label.FallbackBrush = picked ? Brushes.White : Ink;
        return label;
    }

    /// <summary>그 줄을 고른다.</summary>
    private void Pick(int index)
    {
        if (index < 0 || index >= _rows.Count) return;
        _at = index;
        for (int i = 0; i < _rows.Count; i++)
        {
            bool on = i == index;
            _rows[i].Row.Background = on ? Picked : Brushes.Transparent;
            _rows[i].Row.Child = Label(_rows[i].Name, on);
        }
        _decide.On = true;      // 게임도 아무것도 안 고른 동안은 이 단추가 흐리다
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Up:
                Pick(_at <= 0 ? _rows.Count - 1 : _at - 1);
                e.Handled = true;
                break;
            case Key.Down:
                Pick(_at < 0 || _at >= _rows.Count - 1 ? 0 : _at + 1);
                e.Handled = true;
                break;
            case Key.Enter or Key.Space when _at >= 0:
                Decide();
                e.Handled = true;
                break;
        }
    }

    /// <summary>고른 것을 들여다본다 — 시장에서 고른 뒤에 뜨는 것과 같은 창이다.</summary>
    private void Decide()
    {
        if (_at < 0 || _at >= _rows.Count) return;
        if (_items?.Find(_rows[_at].ItemId) is not { } item) return;

        ItemInfoDialog.Show(this, item, _descriptions?.Of(item.Id) ?? "", _art);
    }

    /// <summary>소지품 정보 창을 연다.</summary>
    public static void Show(Window owner, Player player, ItemTable? items,
                            ItemDescriptions? descriptions, ItemArt? art) =>
        new BelongingsDialog(player, items, descriptions, art) { Owner = owner }.ShowDialog();
}
