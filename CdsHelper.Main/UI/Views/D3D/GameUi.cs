using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CdsHelper.Main.UI.Views.D3D;

/// <summary>
/// 함대 창 쪽 물음창·명령창이 함께 쓰는 게임풍 조각. 색은 게임 화면에서 뽑았다 —
/// 짙은 밤색 바탕에 밝은 테를 두르고, 누를 수 있는 것만 양피지에 검은 글씨다.
/// </summary>
internal static class GameUi
{
    public static readonly Brush Back = new SolidColorBrush(Color.FromRgb(0x3A, 0x24, 0x1E));
    public static readonly Brush Edge = new SolidColorBrush(Color.FromRgb(0xC8, 0xB4, 0x90));
    public static readonly Brush Text = new SolidColorBrush(Color.FromRgb(0xF2, 0xEA, 0xD6));
    public static readonly Brush MenuBack = new SolidColorBrush(Color.FromRgb(0x4A, 0x2A, 0x22));
    public static readonly Brush ItemFill = new SolidColorBrush(Color.FromRgb(0xD2, 0xCA, 0xAD));
    public static readonly Brush ItemEdge = new SolidColorBrush(Color.FromRgb(0x4A, 0x40, 0x30));
    public static readonly Brush PageFill = new SolidColorBrush(Color.FromRgb(0xF2, 0xE4, 0xC8));

    /// <summary>도시에 들어가 있는 동안 지도를 덮는 남색. 게임 화면에서 뽑았다.</summary>
    public static readonly Brush MapCover = new SolidColorBrush(Color.FromRgb(0x24, 0x37, 0x5B));

    /// <summary>
    /// 제목 줄. 오른쪽 끝에 닫기(X) 단추를 둔다 — 게임 창들도 그 자리에 있다.
    /// <paramref name="onClose"/> 가 null 이면 단추 없이 제목만 낸다.
    /// </summary>
    public static Border TitleBar(string title, Action? onClose)
    {
        var bar = new DockPanel { LastChildFill = true };

        if (onClose != null)
        {
            var close = new Border
            {
                Background = ItemFill,
                BorderBrush = ItemEdge,
                BorderThickness = new Thickness(2),
                Padding = new Thickness(6, 0, 6, 0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 2, 4, 2),
                ToolTip = "닫기",
                Child = new TextBlock
                {
                    Text = "✕",
                    Foreground = Brushes.Black,
                    FontWeight = FontWeights.Bold,
                    FontSize = 13,
                },
            };
            close.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClose(); };
            DockPanel.SetDock(close, Dock.Right);
            bar.Children.Add(close);
        }

        bar.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Text,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 3, 10, 3),
        });

        return new Border
        {
            Background = MenuBack,
            BorderBrush = Edge,
            BorderThickness = new Thickness(2),
            Child = bar,
        };
    }

    /// <summary>명령 창의 한 줄. <paramref name="run"/> 이 null 이면 흐려 두고 안 먹는다.</summary>
    public static Border MenuItem(string text, Action? run)
    {
        var item = new Border
        {
            Background = ItemFill,
            BorderBrush = ItemEdge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(0, 0, 0, 3),
            Padding = new Thickness(24, 2, 24, 2),
            Cursor = run != null ? Cursors.Hand : Cursors.Arrow,
            Child = new TextBlock
            {
                Text = text,
                Foreground = run != null ? Brushes.Black : Brushes.Gray,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };
        if (run != null) item.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
        return item;
    }

    /// <summary>제목 한 줄과 항목들을 세로로 쌓은 명령 창.</summary>
    public static Border CommandBox(string title, params (string Text, Action? Run)[] items)
    {
        var stack = new StackPanel();
        stack.Children.Add(new Border
        {
            Background = MenuBack,
            BorderBrush = Edge,
            BorderThickness = new Thickness(2),
            Padding = new Thickness(18, 2, 18, 2),
            Margin = new Thickness(0, 0, 0, 6),
            Child = new TextBlock
            {
                Text = title,
                Foreground = Text,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        });
        foreach (var (text, run) in items) stack.Children.Add(MenuItem(text, run));

        return new Border
        {
            Background = MenuBack,
            BorderBrush = Edge,
            BorderThickness = new Thickness(3),
            Padding = new Thickness(6),
            Child = stack,
        };
    }

    /// <summary>창 아래쪽에 두는 단추(결정·중단 따위).</summary>
    public static Border PushButton(string text, Action? run, double width = 110)
    {
        var b = new Border
        {
            Width = width,
            Background = ItemFill,
            BorderBrush = ItemEdge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(10, 0, 10, 0),
            Padding = new Thickness(0, 2, 0, 2),
            Cursor = run != null ? Cursors.Hand : Cursors.Arrow,
            Child = new TextBlock
            {
                Text = text,
                Foreground = run != null ? Brushes.Black : Brushes.Gray,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };
        if (run != null) b.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
        return b;
    }

    /// <summary>
    /// 제목 줄을 잡아 창을 옮길 수 있게 한다. 제목 줄이 없는 창(<c>WindowStyle.None</c>)이라
    /// 이렇게 붙여 줘야 옮길 데가 생긴다.
    /// </summary>
    public static void EnableDrag(Window window, UIElement handle)
    {
        handle.MouseLeftButtonDown += (_, _) =>
        {
            // 누르자마자 뗀 경우 DragMove 가 터진다. 아직 눌려 있을 때만 부른다.
            if (Mouse.LeftButton == MouseButtonState.Pressed) window.DragMove();
        };
    }

    /// <summary>건물 위에 커서를 올렸을 때 밑에 붙는 이름표.</summary>
    public static Border NameTag(string text) => new()
    {
        Background = ItemFill,
        BorderBrush = ItemEdge,
        BorderThickness = new Thickness(2),
        Padding = new Thickness(8, 0, 8, 0),
        Visibility = Visibility.Collapsed,
        Child = new TextBlock
        {
            Text = text,
            Foreground = Brushes.Black,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
        },
    };
}
