using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Engine.Market;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 교역소 「매매」 창 — 담아 두었다가 [결정] 한 번에 사고판다.
/// </summary>
/// <remarks>
/// cds95-mod 의 MarketUtilKR 매매 창(<c>plugins-src/MarketUtilKR/src/market.c</c>)을 옮겼다.
/// <code>
///   ┌ 매매 — 리스본 · 시세 100 · 통상 ───────────────────────────────┐
///   │ 이 도시가 파는 것                 │ 내 짐                [비우기] │
///   │ [그림] ★ 대포                     │ [그림] 대포                    │
///   │        단가 232닢 · 공급 100      │        50개  매각 155닢   +0   │
///   │        무게 20  리스본산 [1][10][100][모두] │ 리스본산 · 매입 232닢 [1][10][100][모두] │
///   │ …                                 │ …                              │
///   │ 소지금 1,000닢                    │ 지출  0닢                      │
///   │ 짐용량 ▓▓▓░░  120 → 170 / 300     │ 수입  0닢              [결정]  │
///   │ 짐중량 ▓▓░░░                      │ 수익  —                        │
///   └────────────────────────────────────────────────────────────────────┘
/// </code>
/// · 줄을 두 번 누르면 총량의 절반을 담는다(두 번이면 전량)<br/>
/// · [1][10][100] 은 그만큼 담고, [모두] 는 전량(다시 누르면 뺀다)<br/>
/// · [결정] 에서 먼저 팔고 그 돈으로 산다. 흥정 재주가 되면 「결정 / 값을 깎는다 / 돌아간다」 판이 뜬다
/// </remarks>
public sealed class TradePostDialog : GameWindow
{
    private const double ColumnWidth = 420, RowHeight = 60, Pic = 55;
    private const int VisibleRows = 8;

    private static readonly Brush Ink = Frozen(0x10, 0x10, 0x18);
    private static readonly Brush Dim = Frozen(0x5A, 0x50, 0x46);
    private static readonly Brush Cart = Frozen(25, 95, 55);
    private static readonly Brush Warn = Frozen(170, 30, 20);
    private static readonly Brush Gain = Frozen(30, 60, 150);
    private static readonly Brush RowAlt = Frozen(0xE6, 0xD8, 0xBA);
    private static readonly Brush BarBack = Frozen(0x20, 0x12, 0x12);
    private static readonly Brush BarUsed = Frozen(150, 55, 20);
    private static readonly Brush BarAdd = Frozen(60, 110, 190);
    private static readonly Brush BarFree = Frozen(0x7A, 0x6A, 0x5A);
    private static readonly Brush PanelBack = Frozen(49, 24, 24);
    private static readonly Brush PanelEdge = Frozen(226, 214, 189);

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private readonly Engine.Game _game;
    private readonly Player _player;
    private readonly TradePost _post;
    private readonly int _city;
    private readonly string _cityName;
    private readonly Random _random = new();

    private List<TradePost.Row> _rows = [];
    private int[] _qty = [];
    private readonly int[] _sell = new int[Player.CargoSlots];

    /// <summary>흥정으로 깎인 값(%) · 이번에 몇 번 걸었나 · 판이 떠 있나.</summary>
    private int _pct = 100, _tries;
    private bool _bargainOn;

    private string _message = "";
    private bool _messageWarn;

    private readonly Grid _root = new();

    /// <summary>오른쪽 칸에 그린 줄 — 짐 칸 번호(없으면 -1)와 왼쪽 줄 번호(없으면 -1).</summary>
    private readonly record struct Right(int Kind, int Origin, int Slot, int Row);

    private TradePostDialog(Engine.Game game, TradePost post, int city, string cityName)
    {
        _game = game;
        _player = game.Player;
        _post = post;
        _city = city;
        _cityName = cityName;

        Title = "매매";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;
        Content = _root;

        KeyDown += OnKey;
        Closing += (_, e) => { if (QuitBargain()) e.Cancel = true; };
        Reload();
    }

    // ── 셈 ───────────────────────────────────────────────────────────────────

    private void Reload()
    {
        _rows = _post.RowsOf(_player, _city);
        _qty = new int[_rows.Count];
        Array.Clear(_sell);
        _pct = 100;
        _tries = 0;
        _bargainOn = false;
        Paint();
    }

    private TradePost.Deal DealNow() => new(
        [.. _rows.Select((r, i) => (r, _qty[i])).Where(p => p.Item2 > 0)],
        [.. Enumerable.Range(0, _player.CargoHold.Count).Where(s => _sell[s] > 0).Select(s => (s, _sell[s]))],
        _pct);

    private int Cost => TradePost.CostOf(DealNow());
    private int Income => _post.GainOf(_player, _city, DealNow());

    /// <summary>팔아서 남는 것 — (여기 매각가 − 원산지 매입가) x 수량.</summary>
    private int Profit => Enumerable.Range(0, _player.CargoHold.Count).Sum(s =>
    {
        var c = _player.CargoHold[s];
        return _sell[s] * (_post.SellPrice(_city, c.Kind) - _post.BuyPrice(c.Origin, c.Kind));
    });

    private int Pending(int kind, int origin) =>
        _rows.Select((r, i) => (r, i)).Where(p => p.r.Kind == kind && p.r.Origin == origin).Sum(p => _qty[p.i]);

    private List<Right> RightRows()
    {
        var list = new List<Right>();
        for (int s = 0; s < _player.CargoHold.Count; s++)
        {
            var c = _player.CargoHold[s];
            list.Add(new Right(c.Kind, c.Origin, s, _rows.FindIndex(r => r.Kind == c.Kind && r.Origin == c.Origin)));
        }
        for (int i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            if (_qty[i] <= 0 || list.Any(x => x.Kind == r.Kind && x.Origin == r.Origin)) continue;
            list.Add(new Right(r.Kind, r.Origin, -1, i));
        }
        return list;
    }

    private void AddBuy(int i, int n)
    {
        if (i < 0 || i >= _rows.Count) return;
        _qty[i] = Math.Clamp(_qty[i] + n, 0, _rows[i].Supply);
        Paint();
    }

    private void AddSell(int slot, int n)
    {
        if (slot < 0 || slot >= _player.CargoHold.Count) return;
        _sell[slot] = Math.Clamp(_sell[slot] + n, 0, _player.CargoHold[slot].Count);
        Paint();
    }

    private void Say(string text, bool warn)
    {
        _message = text;
        _messageWarn = warn;
    }

    // ── 결정 · 흥정 ──────────────────────────────────────────────────────────

    private void Decide()
    {
        if (_bargainOn) return;
        _pct = 100;
        _tries = 0;
        int cost = Cost;
        if (!_post.CanBargain(_player, _city, cost)) { Apply(close: true); return; }
        _bargainOn = true;
        Paint();
    }

    private void Apply(bool close)
    {
        var deal = DealNow();
        int cost = TradePost.CostOf(deal), gain = _post.GainOf(_player, _city, deal);
        var outcome = _post.Apply(_player, _city, deal);
        if (outcome != TradePost.Outcome.Ok)
        {
            Say(outcome switch
            {
                TradePost.Outcome.Nothing => "담은 것이 없습니다.",
                TradePost.Outcome.NotEnoughGold => "가난한 사람에게는 볼일 없네!",
                TradePost.Outcome.HoldFull => "짐용량이 모자랍니다.",
                TradePost.Outcome.TooHeavy => "짐중량이 모자랍니다.",
                TradePost.Outcome.NoSlot => "짐 칸이 모자랍니다 — 교역품은 여덟 가지까지 실을 수 있습니다.",
                _ => "공급량이 모자랍니다.",
            }, true);
            _pct = 100;
            _tries = 0;
            _bargainOn = false;
            Paint();
            return;
        }

        int keep = _tries;
        string said = _message;
        Reload();
        _tries = 0;
        if (close) { Close(); return; }
        Say(keep > 0 ? $"{said}  (지출 {cost:N0}닢 · 수입 {gain:N0}닢)" : $"지출 {cost:N0}닢 · 수입 {gain:N0}닢.", false);
        Paint();
    }

    /// <summary>판에서 고른 것 — 0 결정, 1 값을 깎는다, 2 돌아간다.</summary>
    private void Pick(int k)
    {
        if (k == 0) { _bargainOn = false; Apply(close: true); return; }
        if (k != 1) { _bargainOn = false; Paint(); return; }

        bool ok = TradePost.RollBargain(_player, _random);
        if (ok) _pct = _pct * TradePost.BargainPct / 100;
        Say(TradePost.BargainLine(ok, _tries, Cost), !ok);
        _tries++;

        if (ok && _tries >= TradePost.BargainWins)
        {
            _bargainOn = false;
            Apply(close: false);
            return;
        }
        if (!ok && _tries >= TradePost.BargainLosses)
        {
            int cut = _tries >= 3 ? TradePost.CutHard : TradePost.CutBreak;
            string said = _message;
            _post.CutSupply(_player, _city, cut);
            Reload();
            Say($"{said} (공급이 {cut}% 줄었습니다)", true);
            Paint();
            return;
        }
        Paint();
    }

    /// <summary>흥정만 걸고 나가면 상인이 10% 를 거둬 간다. 그 말을 읽게 첫 닫기는 삼킨다.</summary>
    private bool QuitBargain()
    {
        if (_tries <= 0) return false;
        _post.CutSupply(_player, _city, TradePost.CutQuit);
        Reload();
        Say($"{TradePost.QuitLine} (공급이 {TradePost.CutQuit}% 줄었습니다)", true);
        Paint();
        return true;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_bargainOn)
        {
            e.Handled = true;
            if (e.Key is >= Key.D1 and <= Key.D3) Pick(e.Key - Key.D1);
            else if (e.Key == Key.Enter) Pick(0);
            else if (e.Key == Key.Escape) Pick(2);
            return;
        }
        switch (e.Key)
        {
            case Key.Enter: Decide(); e.Handled = true; break;
            case Key.Escape: Close(); e.Handled = true; break;
            case Key.F5: Say("", false); Reload(); e.Handled = true; break;
        }
    }

    // ── 그리기 ───────────────────────────────────────────────────────────────

    private void Paint()
    {
        _root.Children.Clear();

        var body = new StackPanel { IsHitTestVisible = !_bargainOn };
        var title = GameUi.TitleBar($"매매 — {_cityName} · 시세 {_game.Rates.Of(_city)} · 통상", Close);
        GameUi.EnableDrag(this, title);
        body.Children.Add(title);

        var columns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 6, 8, 0) };
        columns.Children.Add(LeftColumn());
        columns.Children.Add(RightColumn());
        body.Children.Add(columns);
        body.Children.Add(Bottom());

        _root.Children.Add(GameUi.DialogEdge(body));
        if (_bargainOn) _root.Children.Add(BargainPanel());
    }

    private FrameworkElement LeftColumn()
    {
        var panel = new StackPanel { Width = ColumnWidth, Margin = new Thickness(0, 0, 12, 0) };
        panel.Children.Add(Header("이 도시가 파는 것", null));

        var list = new StackPanel();
        for (int i = 0; i < _rows.Count; i++) list.Children.Add(BuyRow(i));
        panel.Children.Add(ListBox(list, _rows.Count == 0 ? "이 도시에는 파는 것이 없습니다." : null));
        return panel;
    }

    private FrameworkElement RightColumn()
    {
        var panel = new StackPanel { Width = ColumnWidth };
        panel.Children.Add(Header("내 짐", new GameButton("비우기", () =>
        {
            Array.Clear(_qty);
            Array.Clear(_sell);
            Say("", false);
            Paint();
        }) { Margin = default }));

        var rights = RightRows();
        var list = new StackPanel();
        for (int v = 0; v < rights.Count; v++) list.Children.Add(CargoRow(v, rights[v]));
        panel.Children.Add(ListBox(list, rights.Count == 0 ? "실은 것이 없습니다." : null));
        return panel;
    }

    private static FrameworkElement Header(string text, UIElement? right)
    {
        var line = new DockPanel { Height = 28, LastChildFill = true };
        if (right != null)
        {
            DockPanel.SetDock(right, Dock.Right);
            line.Children.Add(right);
        }
        line.Children.Add(Light(text, 15));
        return line;
    }

    private static FrameworkElement ListBox(StackPanel list, string? empty)
    {
        var inside = new Grid { Height = RowHeight * VisibleRows + 6 };
        inside.Children.Add(GameUi.Scroller(list, RowHeight * VisibleRows + 6));
        if (empty != null)
            inside.Children.Add(new TextBlock
            {
                Text = empty,
                Foreground = Ink,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        return new Border
        {
            Background = GameUi.PageFill,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3),
            Child = inside,
        };
    }

    private FrameworkElement BuyRow(int i)
    {
        var row = _rows[i];
        int q = _qty[i], rest = Math.Max(0, row.Supply - q);
        string name = row.Kind == _post.SpecialOf(_city) && row.Origin == _city
            ? $"★ {_post.NameOf(row.Kind)}" : _post.NameOf(row.Kind);

        var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        lines.Children.Add(Dark(name, 14, Ink));
        lines.Children.Add(Dark($"단가 {row.Price:N0}닢 · 공급 {rest:N0}", 14, q > 0 && rest == 0 ? Cart : Ink));
        var third = new StackPanel { Orientation = Orientation.Horizontal };
        third.Children.Add(Dark($"무게 {_post.WeightOf(row.Kind)}   ", 12, Ink));
        third.Children.Add(Dark($"{CityName(row.Origin)}산", 12, q > 0 ? Cart : Dim));
        lines.Children.Add(third);

        var buttons = StepButtons(n => AddBuy(i, n),
            () => { _qty[i] = _qty[i] >= row.Supply ? 0 : row.Supply; Paint(); });

        var border = Row(i, row.Kind, lines, buttons);
        border.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) AddBuy(i, (row.Supply + 1) / 2);
        };
        return border;
    }

    private FrameworkElement CargoRow(int v, Right r)
    {
        int have = r.Slot >= 0 ? _player.CargoHold[r.Slot].Count : 0;
        int pend = Pending(r.Kind, r.Origin);
        int sell = r.Slot >= 0 ? _sell[r.Slot] : 0;
        int here = _post.SellPrice(_city, r.Kind);
        int buy = _post.BuyPrice(r.Origin, r.Kind);
        int diff = here - buy;

        var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        lines.Children.Add(Dark(_post.NameOf(r.Kind), 14, Ink));

        var second = new DockPanel { LastChildFill = true };
        var count = Dark(have + pend > 0 ? $"{have + pend:N0}개" : "아직 없음", 14, pend > 0 ? Cart : Ink);
        count.Width = 62;
        DockPanel.SetDock(count, Dock.Left);
        second.Children.Add(count);
        if (r.Slot >= 0 && buy > 0)
        {
            var mark = Dark(diff >= 0 ? $"+{diff:N0}" : $"−{-diff:N0}", 12, diff >= 0 ? Gain : Warn);
            DockPanel.SetDock(mark, Dock.Right);
            second.Children.Add(mark);
            second.Children.Add(Dark($"매각 {here:N0}닢", 14, diff >= 0 ? Gain : Warn));
        }
        lines.Children.Add(second);

        string third = sell > 0 ? $"{CityName(r.Origin)}산 · 팔 것 {sell:N0}개 ({sell * here:N0}닢)"
                     : buy > 0 ? $"{CityName(r.Origin)}산 · 매입 {buy:N0}닢"
                     : $"{CityName(r.Origin)}산";
        lines.Children.Add(Dark(third, 12, sell > 0 ? Warn : pend > 0 ? Cart : Dim));

        FrameworkElement buttons;
        if (r.Slot >= 0)
        {
            int slot = r.Slot;
            buttons = StepButtons(n => AddSell(slot, n),
                () => { _sell[slot] = _sell[slot] >= have ? 0 : have; Paint(); });
        }
        else
        {
            int at = r.Row;
            buttons = Small("비움", () => { if (at >= 0) _qty[at] = 0; Paint(); }, 50);
        }

        var border = Row(v, r.Kind, lines, buttons);
        if (r.Slot >= 0)
            border.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2) AddSell(r.Slot, (have + 1) / 2);
            };
        return border;
    }

    private Border Row(int index, int kind, FrameworkElement lines, FrameworkElement buttons)
    {
        var pic = new Border
        {
            Width = Pic,
            Height = Pic,
            Background = GameUi.InfoBack,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(4, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (_post.Goods.Find(kind) is { } goods && _game.ItemPictures?.TryGetImage(goods.Pic) is { } image)
            pic.Child = new Image { Source = image, Stretch = Stretch.Uniform };

        buttons.VerticalAlignment = VerticalAlignment.Bottom;
        buttons.Margin = new Thickness(4, 0, 4, 6);

        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(pic, Dock.Left);
        dock.Children.Add(pic);
        DockPanel.SetDock(buttons, Dock.Right);
        dock.Children.Add(buttons);
        dock.Children.Add(lines);

        return new Border
        {
            Height = RowHeight,
            Background = index % 2 == 1 ? RowAlt : Brushes.Transparent,
            Child = dock,
        };
    }

    private StackPanel StepButtons(Action<int> add, Action all)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (int n in (int[])[1, 10, 100])
            panel.Children.Add(Small($"{n}", () => add(n), n switch { 1 => 26, 10 => 32, _ => 40 }));
        panel.Children.Add(Small("모두", all, 50));
        return panel;
    }

    private static Border Small(string text, Action run, double width)
    {
        var b = GameUi.PushButton(text, run, width);
        b.Margin = new Thickness(2, 0, 0, 0);
        b.Padding = new Thickness(0);
        if (b.Child is TextBlock t) t.FontSize = 12;
        return b;
    }

    private FrameworkElement Bottom()
    {
        int cost = Cost, income = Income, profit = Profit;
        int addCount = _qty.Sum() - _sell.Sum();
        int addWeight = _rows.Select((r, i) => _qty[i] * _post.WeightOf(r.Kind)).Sum()
                        - Enumerable.Range(0, _player.CargoHold.Count).Sum(s => _sell[s] * _player.CargoHold[s].UnitWeight);

        var left = new StackPanel { Width = ColumnWidth, Margin = new Thickness(0, 0, 12, 0) };
        left.Children.Add(Light($"소지금 {_player.Gold:N0}닢", 15));
        left.Children.Add(BarLine("짐용량", _player.LoadedBarrels, addCount, _player.Capacity));
        left.Children.Add(BarLine("짐중량", _player.LoadedWeight, addWeight, _player.Tonnage));
        if (_message.Length > 0)
            left.Children.Add(new TextBlock
            {
                Text = _message,
                Foreground = _messageWarn ? Frozen(240, 120, 100) : GameUi.Text,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });

        var sums = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sums.Children.Add(Pair(_pct < 100 ? $"지출({100 - _pct}%↓)" : "지출", $"{cost:N0}닢", GameUi.Text));
        sums.Children.Add(Pair("수입", $"{income:N0}닢", GameUi.Text));
        sums.Children.Add(income > 0
            ? Pair("수익", profit >= 0 ? $"+{profit:N0}닢" : $"−{-profit:N0}닢",
                   profit >= 0 ? Frozen(140, 180, 240) : Frozen(240, 120, 100))
            : Pair("수익", "—", GameUi.Text));

        var decide = new GameButton("결정", Decide, width: 90) { On = cost > 0 || income > 0 };
        var right = new DockPanel { Width = ColumnWidth, LastChildFill = true };
        DockPanel.SetDock(decide, Dock.Right);
        right.Children.Add(decide);
        right.Children.Add(sums);

        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 8, 8, 10) };
        line.Children.Add(left);
        line.Children.Add(right);
        return line;
    }

    private static FrameworkElement Pair(string name, string value, Brush color)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
        var label = Light(name, 14);
        label.Width = 90;
        label.TextAlignment = TextAlignment.Right;
        line.Children.Add(label);
        var val = Light(value, 14);
        val.Margin = new Thickness(10, 0, 0, 0);
        val.Foreground = color;
        line.Children.Add(val);
        return line;
    }

    /// <summary>짐칸 막대 — 실은 것(짙은 빨강) · 담은 것(파랑) · 파느라 빌 자리(회색).</summary>
    private static FrameworkElement BarLine(string name, int used, int add, int max)
    {
        int after = used + add;
        double width = 220;
        double Px(int v) => max <= 0 ? 0 : Math.Clamp(width * v / max, 0, width);

        var bar = new Canvas { Width = width, Height = 12, Background = BarBack, ClipToBounds = true };
        void Fill(double from, double to, Brush brush)
        {
            if (to <= from) return;
            var r = new Border { Width = to - from, Height = 12, Background = brush };
            Canvas.SetLeft(r, from);
            bar.Children.Add(r);
        }
        Fill(0, Px(Math.Min(used, after)), BarUsed);
        if (after > used) Fill(Px(used), Px(after), after > max ? Warn : BarAdd);
        else Fill(Px(after), Px(used), BarFree);

        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        var label = Light(name, 13);
        label.Width = 52;
        line.Children.Add(label);
        line.Children.Add(new Border
        {
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = bar,
        });
        var text = Light(add != 0 ? $"{used:N0} → {after:N0} / {max:N0}" : $"{used:N0} / {max:N0}", 13);
        text.Margin = new Thickness(8, 0, 0, 0);
        if (after > max) text.Foreground = Frozen(240, 120, 100);
        line.Children.Add(text);
        return line;
    }

    /// <summary>상인의 판 — 짙은 자주갈색 바탕에 크림 테두리 두 줄, 게임 띠 단추 셋.</summary>
    private FrameworkElement BargainPanel()
    {
        var stack = new StackPanel { Margin = new Thickness(12) };
        string[] names = ["결정", "값을 깎는다", "돌아간다"];
        for (int k = 0; k < names.Length; k++)
        {
            int pick = k;
            stack.Children.Add(new GameButton(names[k], () => Pick(pick), width: 136)
            {
                Margin = new Thickness(0, k == 0 ? 0 : 2, 0, 0),
            });
        }
        return new Border
        {
            Background = PanelBack,
            BorderBrush = PanelEdge,
            BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = stack,
        };
    }

    private static TextBlock Dark(string text, double size, Brush color) => new()
    {
        Text = text,
        Foreground = color,
        FontSize = size,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private static TextBlock Light(string text, double size) => new()
    {
        Text = text,
        Foreground = GameUi.Text,
        FontSize = size,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private string CityName(int city) =>
        city == _city ? _cityName : _game.CityTable.NameOf(city) is { Length: > 0 } name ? name : $"도시 {city}";

    // ── 열기 ────────────────────────────────────────────────────────────────

    /// <summary>들어설 때 교역소 사람이 건네는 한마디(<c>0x00532610</c>).</summary>
    public static void Greet(Window owner, Engine.Game game, int culture) =>
        ConfirmDialog.Tell(owner, "제독, 여기는 교역소입니다. 무언가 거래를 하실 건가요?",
                           face: game.SpeakerFace(TradingPostCode, culture));

    /// <summary>교역소의 건물 코드(화자표).</summary>
    public const int TradingPostCode = 1;

    /// <summary>
    /// 매매 창을 연다. 배가 없으면 「제독, 배가 없습니다」(<c>0x00532958</c>),
    /// 사고팔 것이 둘 다 없으면 「미안하지만, 자네에게 팔 물건은 아무것도 없네.」(<c>0x00532928</c>).
    /// </summary>
    public static void Show(Window owner, Engine.Game game, TradePost post, int city, string cityName,
                            int culture)
    {
        var face = game.SpeakerFace(TradingPostCode, culture);
        if (game.Player.Ships.Count == 0)
        {
            ConfirmDialog.Tell(owner, "제독, 배가 없습니다", face: face);
            return;
        }
        if (!post.HasGoods(game.Player, city) && game.Player.CargoHold.Count == 0)
        {
            ConfirmDialog.Tell(owner, "미안하지만, 자네에게 팔 물건은 아무것도 없네.", face: face);
            return;
        }
        new TradePostDialog(game, post, city, cityName) { Owner = owner }.ShowDialog();
    }
}
