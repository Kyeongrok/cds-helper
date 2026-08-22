using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 자택 보관 창 — 지닌 것과 맡겨 둔 것을 주고받는다.
/// </summary>
/// <remarks>
/// 자택 명령 창의 "보관" 으로 연다. 게임의 <c>0x004608B0</c> 을 옮겼다.
/// <code>
///   소지 = 16칸을 읽는다 (0x004B09D0 → 0x0047CDD0, 플레이어 +0x118)
///   보관 = 99칸을 읽는다 (0x004AB750 → 0x0047CE70, 플레이어 +0x158)
///   0x004B1D30(16, 소지, "소지 아이템", 99, 보관, "보관 아이템")   ← 주고받는 창
///   끝나면 두 칸을 그대로 되쓴다 (0x0047CE50 · 0x0047CDB0)
/// </code>
///
/// <b>소지품은 열여섯 칸, 보관은 아흔아홉 칸이다.</b> 소지품이 꽉 차면 시장에서도
/// "이 이상 가질 수 없습니다!" 로 물리므로, 보관이 그 자리를 비우는 길이다.
///
/// 창 모양은 소지품 정보 창(<see cref="BelongingsDialog"/>)과 같은 결이다 — 양피지 칸 둘에
/// 제목 띠를 얹었다. 한 줄을 고르고 "결정" 을 누르면 반대쪽으로 옮긴다.
/// </remarks>
public sealed class StorageDialog : Window
{
    /// <summary>고른 줄에 씌우는 남색. 소지품 창과 같은 색이다.</summary>
    private static readonly Brush Picked = Freeze(Color.FromRgb(0x3A, 0x5A, 0x9A));

    /// <summary>글꼴을 못 읽었을 때 물러설 글씨색.</summary>
    private static readonly Brush Ink = Freeze(Color.FromRgb(0x20, 0x18, 0x10));

    private static SolidColorBrush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private readonly Player _player;
    private readonly ItemTable? _items;
    private readonly StackPanel _bagRows;
    private readonly StackPanel _boxRows;
    private readonly GameUi.GameLabel _count;
    private readonly GameButton _decide;

    /// <summary>고른 자리 — 소지품 쪽이면 <c>Bag</c>, 보관 쪽이면 <c>Box</c>.</summary>
    private enum Side { None, Bag, Box }

    private Side _side = Side.None;
    private int _at = -1;

    private StorageDialog(Player player, ItemTable? items)
    {
        _player = player;
        _items = items;

        Title = "보관";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Width = 820;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var bands = new Grid();
        bands.ColumnDefinitions.Add(new ColumnDefinition());
        bands.ColumnDefinitions.Add(new ColumnDefinition());

        var left = Band("소지 아이템");
        Grid.SetColumn(left, 0);
        bands.Children.Add(left);

        var right = Band("보관 아이템");
        Grid.SetColumn(right, 1);
        bands.Children.Add(right);

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition());

        var bag = ListColumn();
        Grid.SetColumn(bag.Host, 0);
        columns.Children.Add(bag.Host);
        _bagRows = bag.Items;

        var box = ListColumn();
        Grid.SetColumn(box.Host, 1);
        columns.Children.Add(box.Host);
        _boxRows = box.Items;

        _count = new GameUi.GameLabel(GameFont.WhiteColor) { FallbackBrush = GameUi.Text };
        _decide = new GameButton("결정", Move, width: 130) { On = false };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 12),
        };
        buttons.Children.Add(_count);
        buttons.Children.Add(_decide);
        buttons.Children.Add(new GameButton("중단", Close, width: 130));

        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(bands, Dock.Top);
        root.Children.Add(bands);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(columns);

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = root,
        };

        GameUi.EnableDrag(this, bands);
        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
        Fill();
    }

    /// <summary>두 칸을 다시 채운다. 옮길 때마다 통째로 다시 짓는다 — 줄이 몇 안 된다.</summary>
    private void Fill()
    {
        _bagRows.Children.Clear();
        _boxRows.Children.Clear();

        for (int i = 0; i < _player.Items.Count; i++)
            _bagRows.Children.Add(Row(Side.Bag, i, _player.Items[i]));
        for (int i = 0; i < _player.Stored.Count; i++)
            _boxRows.Children.Add(Row(Side.Box, i, _player.Stored[i]));

        if (_player.Items.Count == 0) _bagRows.Children.Add(Empty("지닌 것이 없다."));
        if (_player.Stored.Count == 0) _boxRows.Children.Add(Empty("맡겨 둔 것이 없다."));

        _count.Text = $"소지 {_player.Items.Count}/{Player.MaxItems}   " +
                      $"보관 {_player.Stored.Count}/{Player.MaxStored}      ";
        _decide.On = _at >= 0;
    }

    /// <summary>고른 것을 반대쪽으로 옮긴다.</summary>
    /// <remarks>
    /// 옮길 자리가 없으면 그 까닭을 알린다 — 게임도 소지품이 꽉 차면 "이 이상 가질 수
    /// 없습니다!" 로 물린다.
    /// </remarks>
    private void Move()
    {
        if (_at < 0) return;

        bool moved = _side switch
        {
            Side.Bag => _player.Store(_at),
            Side.Box => _player.Fetch(_at),
            _ => false,
        };

        if (!moved)
        {
            GameDialog.Show(this, _side == Side.Bag
                ? "이 이상 맡길 수 없습니다!"
                : "이 이상 가질 수 없습니다!");
            return;
        }

        _side = Side.None;
        _at = -1;
        Fill();
    }

    /// <summary>줄 하나. 누르면 골라지고, 반대쪽에서 고른 것은 풀린다.</summary>
    private FrameworkElement Row(Side side, int index, int itemId)
    {
        bool on = side == _side && index == _at;
        var row = new Border
        {
            Background = on ? Picked : Brushes.Transparent,
            Padding = new Thickness(0, 2, 0, 2),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = Label(_items?.Find(itemId)?.Name ?? $"아이템 {itemId}", on),
        };
        row.MouseLeftButtonDown += (_, e) => e.Handled = true;
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _side = side;
            _at = index;
            Fill();
        };
        return row;
    }

    private static FrameworkElement Empty(string text) => new TextBlock
    {
        Text = "  " + text,
        Foreground = Ink,
        FontWeight = FontWeights.Bold,
        FontSize = 15,
        Margin = new Thickness(6, 8, 6, 6),
    };

    /// <summary>줄 글씨. 고른 줄은 남색 위라 흰빛으로 뒤집는다.</summary>
    private static FrameworkElement Label(string name, bool picked)
    {
        var label = new GameUi.GameLabel(picked ? GameFont.WhiteColor : GameFont.ButtonColor)
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(6, 0, 6, 0),
            Text = name,
        };
        label.FallbackBrush = picked ? Brushes.White : Ink;
        return label;
    }

    /// <summary>제목 띠 하나. 원본 조각을 못 읽었으면 띠 대신 글자만 낸다.</summary>
    private static FrameworkElement Band(string title) =>
        GameUi.TitleFrame(GameUi.Sprites, title) ?? new Border
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

    /// <summary>줄이 쌓이는 양피지 칸.</summary>
    private static (Border Host, StackPanel Items) ListColumn()
    {
        var items = new StackPanel { Margin = new Thickness(6, 4, 4, 4) };
        var host = new Border
        {
            Background = GameUi.PageFill,
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = items,
            },
        };
        return (host, items);
    }

    /// <summary>보관 창을 연다.</summary>
    public static void Show(Window owner, Player player, ItemTable? items) =>
        new StorageDialog(player, items) { Owner = owner }.ShowDialog();
}
