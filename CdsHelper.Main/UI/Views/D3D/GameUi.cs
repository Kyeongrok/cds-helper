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
            close.MouseLeftButtonDown += (_, e) => e.Handled = true;   // 제목 줄 끌기에 먹히지 않게
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
            BorderThickness = new Thickness(1),
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
        if (run != null)
        {
            // 누름도 여기서 삼킨다 — 창 끌기(DragMove)가 먼저 걸리면 마우스를 잡아 버려
            // 뗌이 오지 않는다. 그러면 눌러도 아무 일이 없어 멈춘 것처럼 보인다.
            item.MouseLeftButtonDown += (_, e) => e.Handled = true;
            item.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
        }
        return item;
    }

    /// <summary>제목 한 줄과 항목들을 세로로 쌓은 명령 창.</summary>
    public static Border CommandBox(string title, params (string Text, Action? Run)[] items) =>
        CommandBox(title, null, items);

    /// <summary>제목 없이 줄만 쌓은 창. 기능 창처럼 제목이 없는 것에 쓴다.</summary>
    public static Border MenuBox(params (string Text, Action? Run)[] items)
    {
        var stack = new StackPanel();
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

    /// <summary>
    /// 제목 줄에 닫기(X)까지 두는 명령 창. <paramref name="onClose"/> 가 null 이면 제목만 낸다.
    /// </summary>
    public static Border CommandBox(string title, Action? onClose,
                                    params (string Text, Action? Run)[] items)
    {
        var stack = new StackPanel();
        stack.Children.Add(onClose != null
            ? TitleBar(title, onClose)
            : new Border
            {
                Background = MenuBack,
                BorderBrush = Edge,
                BorderThickness = new Thickness(2),
                Padding = new Thickness(18, 2, 18, 2),
                Child = new TextBlock
                {
                    Text = title,
                    Foreground = Text,
                    FontWeight = FontWeights.Bold,
                    FontSize = 15,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            });
        ((Border)stack.Children[0]).Margin = new Thickness(0, 0, 0, 6);
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
        if (run != null)
        {
            // 명령 창 줄과 같은 까닭으로 누름도 삼킨다(창 끌기에 먹히지 않게).
            b.MouseLeftButtonDown += (_, e) => e.Handled = true;
            b.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
        }
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

    /// <summary>
    /// 이 창을 옮기면 딸린 창(<see cref="Window.OwnedWindows"/>)도 같은 만큼 따라 옮긴다.
    /// </summary>
    /// <remarks>
    /// 게임에서는 도시 그림도 커맨드 창도 지도 안에 그려진 것이라 지도가 움직이면 함께
    /// 움직인다. 우리는 D3D 자식 창 위에 제대로 띄우려고 창(HWND)을 따로 쓰므로
    /// (<see cref="CityPicDialog"/> 참고) 그 값을 손으로 붙여 준다.
    ///
    /// 딸린 창에도 이것을 걸어 두면 사슬로 이어진다 — 함대 창을 옮기면 도시 그림이 따라오고,
    /// 그 그림이 옮겨지면 다시 그 옆의 커맨드 창이 따라온다.
    ///
    /// 최대화·최소화로 바뀌는 자리까지 따라가면 딸린 창이 엉뚱한 데로 튄다. 보통 상태일
    /// 때만 옮기고 그 밖에는 기준만 다시 잡는다.
    /// </remarks>
    public static void CarryOwnedWindows(Window window)
    {
        // 아직 안 뜬 창은 Left/Top 이 NaN 이다. 첫 자리를 잡을 때 기준이 채워진다.
        double lastLeft = window.Left, lastTop = window.Top;

        window.LocationChanged += (_, _) =>
        {
            double left = window.Left, top = window.Top;
            double dx = left - lastLeft, dy = top - lastTop;
            lastLeft = left;
            lastTop = top;

            if (window.WindowState != WindowState.Normal) return;
            if (double.IsNaN(dx) || double.IsNaN(dy) || (dx == 0 && dy == 0)) return;

            // 옮기는 사이에 목록이 바뀔 수 있다(창이 닫히는 따위) — 베껴 두고 돈다.
            foreach (var owned in window.OwnedWindows.Cast<Window>().ToArray())
            {
                if (double.IsNaN(owned.Left) || double.IsNaN(owned.Top)) continue;
                owned.Left += dx;
                owned.Top += dy;
            }
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
