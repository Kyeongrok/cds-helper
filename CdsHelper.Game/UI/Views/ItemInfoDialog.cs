using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 아이템 하나를 보여 주는 창 — 그림과 설명, 오른쪽 위에 속성과 효과.
/// </summary>
/// <remarks>
/// 게임 화면을 그대로 옮겼다. 다른 창과 달리 <b>남회색 바탕</b>이고 제목 띠가 없다 —
/// 맨 윗줄에 이름·속성·효과·닫기를 한 줄로 늘어놓는다.
/// <code>
///   바스타드소드      속성/병기      효과  48  X
///   ┌────────┐  독일, 스위스 등지에서 발달한
///   │  그림   │  단검. 보통때는 한손으로 사용
///   │120x120 │  하고, …
///   └────────┘                        확인
/// </code>
/// 시장에서 고른 뒤에도 뜨고, 소지품 일람에서도 뜬다. 그래서 어느 쪽에서 왔는지 모르게
/// 아이템 하나만 받는다.
///
/// 그림이 없는 아이템이 99개나 된다(<see cref="ItemTable.Record.HasPic"/>). 그럴 때는
/// 액자만 비워 두고 설명은 그대로 낸다 — 게임도 그림 자리를 비운다.
/// </remarks>
public sealed class ItemInfoDialog : Window
{
    /// <summary>게임 화면에서 뽑은 남회색 바탕.</summary>
    private static readonly Brush Back = Freeze(Color.FromRgb(0x58, 0x60, 0x78));

    /// <summary>글자색 — 바탕이 밝아 검은 글씨다.</summary>
    private static readonly Brush Ink = Freeze(Color.FromRgb(0x10, 0x10, 0x18));

    /// <summary>그림이 없을 때 액자를 채우는 색.</summary>
    private static readonly Brush Empty = Freeze(Color.FromRgb(0x48, 0x50, 0x66));

    private static SolidColorBrush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private ItemInfoDialog(ItemTable.Record item, string description, ItemArt? art)
    {
        Title = item.Name;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Back;

        var head = HeadRow(item);

        var picture = new Border
        {
            Width = ItemArt.Width,
            Height = ItemArt.Height,
            Background = Empty,
            Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var image = item.HasPic ? art?.TryGetImage(item.Pic) : null;
        if (image != null)
            picture.Child = new Image
            {
                Source = image,
                Width = ItemArt.Width,
                Height = ItemArt.Height,
                // 도트를 뭉개지 않는다 — 게임 그림은 원래 크기 그대로 낸다.
                SnapsToDevicePixels = true,
            };

        var text = new TextBlock
        {
            Text = description.Length > 0 ? description : "(설명이 없다)",
            Foreground = Ink,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 24,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };

        var body = new DockPanel { LastChildFill = true, Margin = new Thickness(14, 12, 14, 8) };
        DockPanel.SetDock(picture, Dock.Left);
        body.Children.Add(picture);
        body.Children.Add(text);

        var ok = GameUi.PushButton("확인", Close, 88);
        ok.HorizontalAlignment = HorizontalAlignment.Right;
        ok.Margin = new Thickness(0, 0, 14, 12);

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(body);
        stack.Children.Add(ok);
        Content = stack;

        GameUi.EnableDrag(this, head);
        KeyDown += (_, e) => { if (e.Key is Key.Escape or Key.Enter or Key.Space) Close(); };
    }

    /// <summary>맨 윗줄 — 이름 · 속성 · 효과 · 닫기.</summary>
    private FrameworkElement HeadRow(ItemTable.Record item)
    {
        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(12, 8, 8, 4) };

        var close = new Border
        {
            Background = GameUi.ItemFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(2),
            Padding = new Thickness(5, 0, 5, 0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "닫기",
            Child = new TextBlock { Text = "✕", Foreground = Brushes.Black, FontWeight = FontWeights.Bold },
        };
        close.MouseLeftButtonDown += (_, e) => e.Handled = true;   // 제목 줄 끌기에 안 먹히게
        close.MouseLeftButtonUp += (_, e) => { e.Handled = true; Close(); };
        DockPanel.SetDock(close, Dock.Right);
        row.Children.Add(close);

        var effect = Label($"효과  {item.Effect}");
        effect.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(effect, Dock.Right);
        row.Children.Add(effect);

        var kind = Label($"속성/{item.CategoryName}");
        kind.Margin = new Thickness(0, 0, 24, 0);
        DockPanel.SetDock(kind, Dock.Right);
        row.Children.Add(kind);

        row.Children.Add(Label(item.Name));
        return row;
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = Ink,
        FontSize = 15,
        FontWeight = FontWeights.Bold,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>아이템 하나를 보여 준다. 확인을 누르거나 ESC 로 닫는다.</summary>
    public static void Show(Window owner, ItemTable.Record item, string description, ItemArt? art) =>
        new ItemInfoDialog(item, description, art) { Owner = owner }.ShowDialog();
}
