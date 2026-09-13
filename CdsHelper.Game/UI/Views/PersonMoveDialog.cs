using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 인물들이 어느 도시로 가고 있는지, 다음에 어디로 갈 수 있는지 늘어놓는 창. 제목 줄 햄버거에서 연다.
/// </summary>
/// <remarks>
/// 셈은 모두 <see cref="PersonWorld"/> 가 한다 — 여기서는 보여 주기만 한다.
/// <list type="bullet">
///   <item>14~200 은 매월 1일에 다섯 달에 한 번꼴로 떠난다. 고를 수 있는 도시는 갈래(해역 ·
///         문화권 · 나라)가 정한다(<c>0x004327F0</c>).</item>
///   <item>0~13 역사 항해자는 주사위가 아니라 대본(<c>HISTCHR.CDS</c>)대로 떠난다 — 앞으로의
///         수를 함께 보인다.</item>
///   <item>닿으면 예순 날 쉰다. 201 이상은 움직이지 않는다.</item>
/// </list>
/// 볼트 <c>72.분석-인물 이동(역사 항해사와 매달 굴림)</c>.
/// </remarks>
public sealed class PersonMoveDialog : GameWindow
{
    /// <summary>역사 항해자의 대본을 몇 달 앞까지 보일지.</summary>
    private const int ScriptMonths = 60;

    private static readonly string[] KindNames = ["해역", "문화권", "안 움직임", "나라"];

    private static readonly string[] Views = ["길 위에 있는 사람", "움직일 수 있는 사람", "모두"];

    private readonly Engine.Game _game;
    private readonly PersonWorld _world;

    private readonly ComboBox _view = new() { Width = 180, VerticalAlignment = VerticalAlignment.Center };

    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false,
        IsReadOnly = true,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        AlternatingRowBackground = Brushes.WhiteSmoke,
        SelectionMode = DataGridSelectionMode.Single,
    };

    private readonly TextBlock _detail = new()
    {
        Margin = new Thickness(10),
        TextWrapping = TextWrapping.Wrap,
    };

    private readonly TextBlock _status = new() { Margin = new Thickness(10, 6, 10, 8) };

    /// <summary>표 한 줄.</summary>
    private sealed record Row(int Id, string Name, int Age, string Now, string To, string Left,
                              string State, string Kind, PersonTable.Row Source);

    private PersonMoveDialog(Engine.Game game, PersonWorld world)
    {
        _game = game;
        _world = world;

        Title = "인물 이동";
        Width = 1000;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Col("번호", nameof(Row.Id), 50);
        Col("이름", nameof(Row.Name), 170);
        Col("나이", nameof(Row.Age), 45);
        Col("지금", nameof(Row.Now), 110);
        Col("가는 곳", nameof(Row.To), 110);
        Col("남은 날", nameof(Row.Left), 60);
        Col("상태", nameof(Row.State), 170);
        Col("갈래", nameof(Row.Kind), 60);

        foreach (var name in Views) _view.Items.Add(name);
        _view.SelectedIndex = 0;
        _view.SelectionChanged += (_, _) => Rebuild();
        _grid.SelectionChanged += (_, _) => ShowDetail();

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 8, 10, 4),
            Children =
            {
                new TextBlock
                {
                    Text = "보기:",
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                },
                _view,
            },
        };

        var side = new ScrollViewer
        {
            Width = 280,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _detail,
        };

        var split = new DockPanel();
        DockPanel.SetDock(side, Dock.Right);
        split.Children.Add(side);
        split.Children.Add(_grid);

        var page = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        page.Children.Add(bar);
        page.Children.Add(_status);
        page.Children.Add(split);
        Content = page;

        Rebuild();
    }

    /// <summary>창을 연다. 인물 표를 못 읽었으면 그렇다고 알린다.</summary>
    public static void Show(Window owner, Engine.Game game)
    {
        if (game.World is not { } world)
        {
            NoticeDialog.Show(owner, $"인물 표를 읽지 못했습니다 {PersonTable.LastError}".TrimEnd());
            return;
        }

        // 그 날짜까지 따라잡고 본다 — 지도를 안 열었으면 세상이 멈춰 있을 수 있다.
        world.Advance(game.Player.Date);
        new PersonMoveDialog(game, world) { Owner = owner }.ShowDialog();
    }

    private void Col(string header, string path, double width) => _grid.Columns.Add(
        new DataGridTextColumn
        {
            Header = header,
            Binding = new System.Windows.Data.Binding(path),
            Width = new DataGridLength(width),
        });

    private string CityOf(int city) => city >= 0 ? _game.CityName(city) : "—";

    /// <summary>표를 다시 짓는다. 보고 있던 사람은 그대로 붙들어 둔다.</summary>
    private void Rebuild()
    {
        int keep = _grid.SelectedItem is Row picked ? picked.Id : -1;
        var date = _game.Player.Date;

        int moving = 0, resting = 0;
        var rows = new List<Row>();
        foreach (var person in _world.People)
        {
            bool onRoad = PersonWorld.Moving(person);
            bool active = _world.IsActive(person);
            if (onRoad) moving++;
            else if (active && person.Wait < 0) resting++;

            bool show = _view.SelectedIndex switch
            {
                0 => onRoad,
                1 => onRoad || Movable(person, active),
                _ => true,
            };
            if (!show) continue;

            string to = person.Dest == PersonWorld.SpotDest ? "발견물 자리" : CityOf(person.Dest);
            string left = _world.DaysLeft(person) is { } days ? $"{days}일" : "";
            string kind = person.Kind >= 0 && person.Kind < KindNames.Length
                ? KindNames[person.Kind] : $"{person.Kind}";
            if (person.Id < PersonTable.VoyagerCount) kind = "대본";

            rows.Add(new Row(person.Id, person.Name, _world.Table.AgeOn(person, date.Year),
                             CityOf(person.City), onRoad ? to : "", left,
                             StateOf(person, active, onRoad), kind, person));
        }

        _grid.ItemsSource = rows;
        if (keep >= 0) _grid.SelectedItem = rows.FirstOrDefault(r => r.Id == keep);

        _status.Text = $"{date:yyyy년 M월 d일} · 길 위 {moving}명 · 쉬는 중 {resting}명 · 목록 {rows.Count}명";
        ShowDetail();
    }

    /// <summary>언젠가 스스로 떠날 수 있는 사람인가.</summary>
    private static bool Movable(PersonTable.Row person, bool active) =>
        person.Id < PersonTable.VoyagerCount
        || (active && person.Id < PersonTable.MovingEnd && person.Kind != 2);

    private string StateOf(PersonTable.Row person, bool active, bool onRoad)
    {
        if (onRoad) return person.Dest == PersonWorld.SpotDest ? "발견물 자리로 항해 중" : "항해 중";

        if (person.Id < PersonTable.VoyagerCount)
        {
            var next = _world.ScriptAhead(person, _game.Player.Date, ScriptMonths).FirstOrDefault();
            return next.Year > 0
                ? $"대본 {next.Year}.{next.Month:D2} → {(next.ToCity ? CityOf(next.City) : "발견물 자리")}"
                : "남은 대본 없음";
        }

        if (person.Id >= PersonTable.MovingEnd) return "이벤트 인물 — 안 움직임";
        if (!active) return "나오지 않음(등장·나이)";
        if (person.Kind == 2) return "갈래 2 — 안 움직임";
        if (person.Wait < 0) return $"쉬는 중 {-person.Wait}일 남음";
        return "매월 1일 5분의 1로 떠남";
    }

    /// <summary>고른 사람의 자세한 것 — 갈 수 있는 도시나 앞으로의 대본.</summary>
    private void ShowDetail()
    {
        if (_grid.SelectedItem is not Row row)
        {
            _detail.Text = "사람을 고르면 어디로 갈 수 있는지 보입니다.";
            return;
        }

        var person = row.Source;
        var lines = new List<string> { $"{person.Name} (#{person.Id})", "" };

        if (PersonWorld.Moving(person))
            lines.Add($"{CityOf(person.From)} → {row.To}  ·  남은 날 {row.Left}");

        if (person.Id < PersonTable.VoyagerCount)
        {
            lines.Add($"앞으로 {ScriptMonths / 12}년의 대본");
            var moves = _world.ScriptAhead(person, _game.Player.Date, ScriptMonths).ToList();
            if (moves.Count == 0) lines.Add("  (없음)");
            foreach (var move in moves)
                lines.Add($"  {move.Year}.{move.Month:D2}  {(move.ToCity ? CityOf(move.City) : $"발견물 {move.Discovery} 자리")}");
        }
        else if (person.City >= 0)
        {
            var picks = _world.CandidatesOf(person);
            lines.Add($"다음에 갈 수 있는 도시 {picks.Count}곳 (갈래: {row.Kind})");
            lines.Add(picks.Count == 0 ? "  (없음 — 굴려도 안 떠난다)"
                                       : "  " + string.Join(", ", picks.Select(CityOf)));
        }
        else if (!PersonWorld.Moving(person))
        {
            lines.Add("어느 도시에도 앉아 있지 않아 후보를 모을 수 없습니다.");
        }

        _detail.Text = string.Join("\n", lines);
    }
}
