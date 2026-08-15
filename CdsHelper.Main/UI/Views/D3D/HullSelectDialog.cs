using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Main.UI.Views.D3D;

/// <summary>
/// 조선소 → 구입 에서 뜨는 "선체종류 선택" 창. 한 줄을 고르고 결정을 누르면 그 배를 산다 —
/// 값은 <see cref="Player"/> 의 소지금에서 빠지고 배는 그 사람의 함대에 붙는다.
/// </summary>
/// <remarks>
/// 살 수 없는 줄은 흐려 두고 고를 수도 없게 한다. 소지금이 모자라거나 배가 이미
/// <see cref="Player.MaxShips"/> 척이면 그렇다 — 까닭은 창 아래에 적는다.
/// </remarks>
public sealed class HullSelectDialog : Window
{
    private static readonly string[] Headers =
        ["선체명", "내구력", "추진력", "적재용량", "적재중량", "필요승인", "대포수", "값(닢)"];

    private static readonly double[] Widths = [110, 84, 84, 84, 84, 84, 84, 90];

    private readonly Player _player;
    private readonly Border _decide;
    private readonly TextBlock _purse;
    private readonly Dictionary<Hull, Border> _rows = [];

    private Hull? _picked;

    /// <summary>이 창에서 산 배. 안 샀으면 null.</summary>
    public Hull? Bought { get; private set; }

    private HullSelectDialog(Player player)
    {
        _player = player;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        _decide = GameUi.PushButton("결정", Decide);
        _purse = new TextBlock
        {
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 8),
        };
        buttons.Children.Add(_decide);
        buttons.Children.Add(GameUi.PushButton("중단", Close));

        var stack = new StackPanel();
        stack.Children.Add(GameUi.TitleBar("선체종류 선택", Close));
        stack.Children.Add(new Border
        {
            Background = GameUi.PageFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4, 4, 4, 0),
            Padding = new Thickness(10, 6, 10, 6),
            Child = BuildTable(),
        });
        stack.Children.Add(_purse);
        stack.Children.Add(buttons);

        Content = new Border
        {
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = stack,
        };

        SetDecide(enabled: false);   // 줄을 고르기 전에는 흐리다
        UpdatePurse();
        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
        MouseRightButtonUp += (_, _) => Close();
    }

    private StackPanel BuildTable()
    {
        var table = new StackPanel();
        table.Children.Add(Row(Headers, header: true));

        foreach (var hull in Hull.All)
        {
            string[] cells =
            [
                hull.Name, $"{hull.Hp}", $"{hull.Speed}", $"{hull.Capacity}",
                $"{hull.Tonnage}", $"{hull.Crew}", $"{hull.Guns}", $"{hull.Price}",
            ];
            bool canBuy = _player.CanBuy(hull) == PurchaseResult.Ok;
            var row = Row(cells, header: false, dim: !canBuy);
            if (canBuy)
            {
                row.Cursor = Cursors.Hand;
                row.MouseLeftButtonUp += (_, e) => { e.Handled = true; Pick(hull); };
            }
            _rows[hull] = row;
            table.Children.Add(row);
        }
        return table;
    }

    /// <summary>표 한 줄. 이름은 가운데, 숫자는 오른쪽으로 붙인다 — 게임 표와 같은 모양이다.</summary>
    private static Border Row(string[] cells, bool header, bool dim = false)
    {
        var grid = new Grid();
        for (int c = 0; c < Widths.Length; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Widths[c]) });

        for (int c = 0; c < cells.Length; c++)
        {
            var tb = new TextBlock
            {
                Text = cells[c],
                Foreground = dim ? Brushes.Gray : Brushes.Black,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Margin = new Thickness(4, 2, 4, 2),
                HorizontalAlignment = header || c == 0
                    ? HorizontalAlignment.Center
                    : HorizontalAlignment.Right,
            };
            Grid.SetColumn(tb, c);
            grid.Children.Add(tb);
        }

        return new Border { Background = Brushes.Transparent, Child = grid };
    }

    /// <summary>줄을 고른다. 고른 줄만 짙게 뒤집어 두고 결정을 살린다.</summary>
    private void Pick(Hull hull)
    {
        _picked = hull;
        foreach (var (h, row) in _rows)
        {
            bool on = h == hull;
            row.Background = on ? GameUi.MenuBack : Brushes.Transparent;
            foreach (var tb in ((Grid)row.Child).Children.OfType<TextBlock>())
                tb.Foreground = on ? GameUi.Text : Brushes.Black;
        }
        SetDecide(enabled: true);
        UpdatePurse();
    }

    /// <summary>결정 단추를 살리거나 흐린다. 줄을 고르기 전에는 눌러도 아무 일이 없다.</summary>
    private void SetDecide(bool enabled)
    {
        ((TextBlock)_decide.Child).Foreground = enabled ? Brushes.Black : Brushes.Gray;
        _decide.Cursor = enabled ? Cursors.Hand : Cursors.Arrow;
    }

    private void Decide()
    {
        if (_picked is not { } hull) return;

        var result = _player.Buy(hull);
        switch (result)
        {
            case PurchaseResult.Ok:
                Bought = hull;
                NoticeDialog.Show(this, $"「{hull.Name}」을(를) 샀습니다 · {hull.Price}닢");
                Close();
                break;
            case PurchaseResult.NotEnoughGold:
                NoticeDialog.Show(this, "소지금이 모자랍니다");
                break;
            case PurchaseResult.FleetFull:
                NoticeDialog.Show(this, $"배는 {Player.MaxShips}척까지만 가질 수 있습니다");
                break;
        }
    }

    private void UpdatePurse()
    {
        string tail = _player.IsFleetFull
            ? $" · 배가 {Player.MaxShips}척이라 더 살 수 없습니다"
            : _picked == null ? " · 살 배를 고르십시오" : "";
        _purse.Text = $"소지금 {_player.Gold}닢 · 함선 {_player.Ships.Count}/{Player.MaxShips}척{tail}";
    }

    /// <summary>선체 표를 띄운다. 배를 샀으면 그 선체를 낸다.</summary>
    public static Hull? Show(Window owner, Player player)
    {
        var dlg = new HullSelectDialog(player) { Owner = owner };
        dlg.ShowDialog();
        return dlg.Bought;
    }
}
