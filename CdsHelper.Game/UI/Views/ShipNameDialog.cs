using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 「선명입력」 — 배 이름을 정하는 창.
/// </summary>
/// <remarks>
/// 게임 화면 그대로다. 맨 위에 지금 이름이 적힌 줄이 있고 그 오른쪽 끝에 작은 단추가 하나
/// 있다(계산기처럼 생겼다). 그 단추를 누르면 <see cref="TextInputDialog"/> 가 떠서 글자를
/// 하나씩 찍어 지을 수 있고, 밑의 목록에서 <b>미리 갖춰 둔 이름</b>(<see cref="ShipNames.All"/>)
/// 을 골라도 된다.
///
/// 게임은 이 창을 <c>0x00454D30(창, 버퍼, 0x24, 목록수, 목록, "선명입력")</c> 하나로 내고
/// 배를 살 때도 같은 창을 쓴다. 우리는 아직 개조의 "선명변경" 에서만 쓴다.
/// <code>
///   0x0053C178  이름 포인터 표 스물하나 (문자열은 0x00531350 부터)
///   0x00531468  "선명입력"
///   0x00531478  "배의 이름을 정해 주십시오"
/// </code>
/// </remarks>
public sealed class ShipNameDialog : Window
{
    private readonly TextBlock _name;
    private readonly List<Border> _rows = [];
    private string? _result;

    private ShipNameDialog(string current)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        _name = new TextBlock
        {
            Text = current,
            Foreground = Brushes.Black,
            FontWeight = FontWeights.Bold,
            FontSize = 17,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 3, 8, 3),
        };

        // 오른쪽 끝의 작은 단추. 게임 것은 계산기 그림인데 우리는 글자로 갈음한다.
        var pad = new Border
        {
            Background = GameUi.ItemFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5, 1, 5, 1),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = "田",
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
            },
        };
        pad.MouseLeftButtonUp += (_, e) => { e.Handled = true; TypeIt(); };

        var top = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(pad, Dock.Right);
        top.Children.Add(pad);
        top.Children.Add(_name);

        var list = new StackPanel();
        foreach (string name in ShipNames.All)
        {
            string pick = name;
            var row = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(10, 1, 6, 1),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = name,
                    Foreground = Brushes.Black,
                    FontWeight = FontWeights.Bold,
                    FontSize = 16,
                },
            };
            row.MouseLeftButtonUp += (_, e) => { e.Handled = true; Choose(pick); };
            _rows.Add(row);
            list.Children.Add(row);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 8),
        };
        buttons.Children.Add(GameUi.PushButton("결정", Decide));
        buttons.Children.Add(GameUi.PushButton("중단", Cancel));

        var title = GameUi.TitleBar("선명입력", Cancel);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(Framed(top, new Thickness(4, 4, 4, 0)));
        stack.Children.Add(Framed(new ScrollViewer
        {
            Height = 300,
            Width = 300,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = list,
        }, new Thickness(4, 4, 4, 0)));
        stack.Children.Add(buttons);

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = stack,
        };

        Mark(current);
        KeyDown += (_, e) => { if (e.Key is Key.Escape) Cancel(); };
        MouseRightButtonUp += (_, _) => Cancel();
    }

    private static Border Framed(UIElement child, Thickness margin) => new()
    {
        Background = GameUi.PageFill,
        BorderBrush = GameUi.ItemEdge,
        BorderThickness = new Thickness(2),
        Margin = margin,
        Padding = new Thickness(2),
        Child = child,
    };

    /// <summary>목록에서 하나를 골랐다 — 위 줄에 올려만 놓고 결정은 따로 받는다.</summary>
    private void Choose(string name)
    {
        _name.Text = name;
        Mark(name);
    }

    /// <summary>위 줄과 같은 이름의 줄을 도드라지게 칠한다.</summary>
    private void Mark(string name)
    {
        for (int i = 0; i < _rows.Count; i++)
            _rows[i].Background = ShipNames.All[i] == name ? GameUi.ItemFill : Brushes.Transparent;
    }

    /// <summary>글자판을 열어 손으로 짓는다.</summary>
    private void TypeIt()
    {
        if (TextInputDialog.Ask(this, _name.Text, ShipNames.MaxLength) is { } typed)
        {
            _name.Text = typed;
            Mark(typed);
        }
    }

    private void Decide()
    {
        _result = _name.Text.Trim();
        Close();
    }

    private void Cancel()
    {
        _result = null;
        Close();
    }

    /// <summary>
    /// 창을 띄우고 정한 이름을 낸다. 중단했거나 그대로면 null.
    /// </summary>
    /// <param name="owner">주인 창.</param>
    /// <param name="current">지금 이름.</param>
    public static string? Ask(Window owner, string current)
    {
        var dialog = new ShipNameDialog(current) { Owner = owner };
        dialog.ShowDialog();

        string? name = dialog._result;
        return string.IsNullOrWhiteSpace(name) || name == current ? null : name;
    }
}
