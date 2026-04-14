using System.Windows;
using System.Windows.Controls;
using CdsHelper.Main.Local.ViewModels;

namespace CdsHelper.Main.UI.Views;

public class EditDiscoveryDialog : Window
{
    private TextBox? _txtLatFrom;
    private TextBox? _txtLatTo;
    private TextBox? _txtLonFrom;
    private TextBox? _txtLonTo;

    public int? LatFrom { get; private set; }
    public int? LatTo { get; private set; }
    public int? LonFrom { get; private set; }
    public int? LonTo { get; private set; }

    public EditDiscoveryDialog(DiscoveryDisplayItem item)
    {
        Title = $"발견물 좌표 편집 - {item.Name}";
        Width = 350;
        Height = 250;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        var grid = new Grid { Margin = new Thickness(15) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 발견물 이름
        var lblName = new TextBlock
        {
            Text = item.Name,
            FontSize = 14,
            FontWeight = FontWeights.Bold
        };
        Grid.SetRow(lblName, 0);
        Grid.SetColumnSpan(lblName, 4);
        grid.Children.Add(lblName);

        // 위도
        AddRow(grid, 2, "위도:", "From", "To", out _txtLatFrom, out _txtLatTo);
        // 경도
        AddRow(grid, 4, "경도:", "From", "To", out _txtLonFrom, out _txtLonTo);

        // 힌트
        var hint = new TextBlock
        {
            Text = "N=양수, S=음수 / E=양수, W=음수\n점 좌표: From=To 동일값",
            Foreground = System.Windows.Media.Brushes.Gray,
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 0)
        };
        Grid.SetRow(hint, 5);
        Grid.SetColumnSpan(hint, 4);
        grid.Children.Add(hint);

        // 버튼
        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var btnSave = new Button
        {
            Content = "저장",
            Width = 70,
            Height = 28,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        btnSave.Click += (_, _) =>
        {
            LatFrom = ParseInt(_txtLatFrom?.Text);
            LatTo = ParseInt(_txtLatTo?.Text);
            LonFrom = ParseInt(_txtLonFrom?.Text);
            LonTo = ParseInt(_txtLonTo?.Text);
            DialogResult = true;
        };

        var btnCancel = new Button
        {
            Content = "취소",
            Width = 70,
            Height = 28,
            IsCancel = true
        };

        btnPanel.Children.Add(btnSave);
        btnPanel.Children.Add(btnCancel);
        Grid.SetRow(btnPanel, 7);
        Grid.SetColumnSpan(btnPanel, 4);
        grid.Children.Add(btnPanel);

        Content = grid;

        // 기존 값 세팅
        _txtLatFrom!.Text = item.LatFrom?.ToString() ?? "";
        _txtLatTo!.Text = item.LatTo?.ToString() ?? "";
        _txtLonFrom!.Text = item.LonFrom?.ToString() ?? "";
        _txtLonTo!.Text = item.LonTo?.ToString() ?? "";
    }

    private static void AddRow(Grid grid, int row, string label, string fromHint, string toHint,
        out TextBox txtFrom, out TextBox txtTo)
    {
        var lbl = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(lbl, row);
        Grid.SetColumn(lbl, 0);
        grid.Children.Add(lbl);

        txtFrom = new TextBox
        {
            Height = 25,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 2)
        };
        Grid.SetRow(txtFrom, row);
        Grid.SetColumn(txtFrom, 1);
        grid.Children.Add(txtFrom);

        var tilde = new TextBlock
        {
            Text = "~",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(tilde, row);
        Grid.SetColumn(tilde, 2);
        grid.Children.Add(tilde);

        txtTo = new TextBox
        {
            Height = 25,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 2)
        };
        Grid.SetRow(txtTo, row);
        Grid.SetColumn(txtTo, 3);
        grid.Children.Add(txtTo);
    }

    private static int? ParseInt(string? text)
    {
        return int.TryParse(text?.Trim(), out var v) ? v : null;
    }
}
