using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CdsHelper.Main.UI.Views.D3D;

/// <summary>
/// 게임의 "[도시]의 항구로 들어가겠습니까?" 창을 흉내낸 예/아니오 물음.
/// </summary>
/// <remarks>
/// 창(HWND)을 따로 쓰므로 D3D 자식 창 위에 제대로 뜬다 — airspace 를 안 탄다.
/// 색은 게임 화면에서 뽑았다. 짙은 밤색 바탕에 밝은 테를 두르고 글씨는 흰빛이며,
/// 단추만 양피지에 검은 글씨다.
/// </remarks>
public sealed class PortDialog : Window
{
    private static readonly Brush Back = new SolidColorBrush(Color.FromRgb(0x3A, 0x24, 0x1E));
    private static readonly Brush Edge = new SolidColorBrush(Color.FromRgb(0xC8, 0xB4, 0x90));
    private static readonly Brush Text = new SolidColorBrush(Color.FromRgb(0xF2, 0xEA, 0xD6));
    private static readonly Brush BtnFill = new SolidColorBrush(Color.FromRgb(0xD2, 0xCA, 0xAD));
    private static readonly Brush BtnEdge = new SolidColorBrush(Color.FromRgb(0x4A, 0x40, 0x30));

    private PortDialog(string cityName)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Back;

        var ask = new TextBlock
        {
            Text = $"[{cityName}]의 항구로 들어가겠습니까?",
            Foreground = Text,
            FontWeight = FontWeights.Bold,
            FontSize = 18,
            Margin = new Thickness(28, 22, 28, 18),
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 22),
        };
        buttons.Children.Add(MakeButton("YES", true));
        buttons.Children.Add(MakeButton("NO", false));

        var stack = new StackPanel();
        stack.Children.Add(ask);
        stack.Children.Add(buttons);

        Content = new Border
        {
            BorderBrush = Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = stack,
        };

        // 제목 줄이 없어(WindowStyle.None) 창을 잡아 옮길 데가 없다. 바탕 아무 데나 끌면
        // 옮겨지게 한다. 단추 위에서 누른 것은 단추가 삼키므로 여기까지 올라오지 않는다.
        MouseLeftButtonDown += (_, _) =>
        {
            // 누르자마자 뗀 경우 DragMove 가 터진다. 아직 눌려 있을 때만 부른다.
            if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove();
        };
    }

    private Button MakeButton(string label, bool answer)
    {
        var b = new Button
        {
            Width = 96,
            Height = 30,
            Margin = new Thickness(12, 0, 12, 0),
            Background = BtnFill,
            BorderBrush = BtnEdge,
            BorderThickness = new Thickness(2),
            Foreground = Brushes.Black,
            FontWeight = FontWeights.Bold,
            FontSize = 16,
            Content = label,
            IsDefault = answer,
            IsCancel = !answer,
        };
        b.Click += (_, _) => { DialogResult = answer; };
        return b;
    }

    /// <summary>물어보고 예를 골랐으면 true.</summary>
    public static bool Ask(Window owner, string cityName)
    {
        var dlg = new PortDialog(cityName) { Owner = owner };
        return dlg.ShowDialog() == true;
    }
}
