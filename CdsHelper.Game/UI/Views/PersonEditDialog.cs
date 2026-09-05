using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 인물 표를 고치는 창 — 이름 · 나이 · 자리 · 고용 · 이동 갈래 · 기술 · 언어.
/// </summary>
/// <remarks>
/// 고치는 것은 <b>세이브가 아니라 <see cref="PersonTable"/>(<c>인물표.json</c>)</b> 이다.
/// 나라 표·도시 문화권 창과 같은 결이라 남의 세이브는 한 바이트도 건드리지 않고,
/// 고친 것은 <c>%APPDATA%\CdsHelper\exe-tables</c> 에 남아 <b>놀이에도 그대로 쓰인다</b>.
/// "본으로 되돌리기" 는 고쳐 둔 것을 걷어 같이 깔린 <c>인물표.json</c> 으로 돌려놓고,
/// "세이브에서 굽기" 는 고른 세이브에서 표를 통째로 다시 굽는다.
///
/// 번호가 뜻을 가른다(볼트 <c>72.분석-인물 이동</c>) — <b>0~13</b> 은 역사 항해사라
/// <c>HISTCHR.CDS</c> 각본이 옮기므로 갈래·목적지가 뜻이 없고, <b>14~200</b> 만 매월 1일에
/// 굴려 목적지를 뽑으며, <b>201~280</b> 은 이벤트 인물·괴물·누적 캐릭터라 안 움직인다.
/// 그 경계를 목록의 <c>구분</c> 칸으로 세워 둔다.
/// </remarks>
public sealed class PersonEditDialog : Window
{
    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        SelectionMode = DataGridSelectionMode.Single,
        Margin = new Thickness(10, 6, 10, 4),
    };

    private readonly TextBox _search = new() { Width = 160, Padding = new Thickness(3, 2, 3, 2) };

    private readonly CheckBox _onlyMoving = new()
    {
        Content = "매달 굴리는 사람만 (14~200)",
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(14, 0, 0, 0),
    };

    private readonly CheckBox _onlyAppeared = new()
    {
        Content = "등장한 사람만",
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(14, 0, 0, 0),
        IsChecked = true,
    };

    private readonly TextBlock _who = new()
    {
        Margin = new Thickness(10, 6, 10, 2),
        FontWeight = FontWeights.Bold,
    };

    private readonly Button _revert = new()
    {
        Content = "본으로 되돌리기",
        Padding = new Thickness(10, 2, 10, 2),
        Margin = new Thickness(14, 0, 0, 0),
        ToolTip = "고쳐 둔 것을 걷고 같이 깔린 인물표.json 으로 돌려놓는다",
    };

    private readonly Button _bake = new()
    {
        Content = "세이브에서 굽기",
        Padding = new Thickness(10, 2, 10, 2),
        Margin = new Thickness(6, 0, 0, 0),
        ToolTip = "고른 SAVEDATA.CDS 에서 표를 통째로 다시 굽는다",
    };

    private readonly WrapPanel _skillPanel = new() { Margin = new Thickness(10, 0, 10, 6) };
    private readonly TextBlock _status = new() { Margin = new Thickness(10, 2, 10, 8) };

    private readonly List<TextBox> _skillBoxes = [];
    private readonly List<TextBox> _langBoxes = [];
    private readonly List<Choice> _cityChoices = [];

    private PersonTable? _table;
    private PersonTable.Row? _picked;
    private bool _filling;

    public PersonEditDialog()
    {
        Title = "인물 표 고치기";
        Width = 1180;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // 도시 목록은 칸을 짜기 전에 채워야 한다 — 고르는 칸이 이 목록을 그대로 문다.
        FillCityChoices();
        BuildColumns();
        BuildSkillPanel();

        _search.TextChanged += (_, _) => Rebuild();
        _onlyMoving.Checked += (_, _) => Rebuild();
        _onlyMoving.Unchecked += (_, _) => Rebuild();
        _onlyAppeared.Checked += (_, _) => Rebuild();
        _onlyAppeared.Unchecked += (_, _) => Rebuild();

        _revert.Click += (_, _) => Revert();
        _bake.Click += (_, _) => BakeFromSave();

        _grid.SelectionChanged += (_, _) => FillSkills(_grid.SelectedItem as PersonTable.Row);
        _grid.CellEditEnding += (_, e) =>
        {
            // 칸을 다 쓰고 나서야 값이 들어온다 — 한 박자 뒤에 거둔다.
            if (e.EditAction == DataGridEditAction.Commit && e.Row.Item is PersonTable.Row row)
                Dispatcher.BeginInvoke(new Action(() => Save(row)));
        };

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 8, 10, 0),
            Children =
            {
                new TextBlock
                {
                    Text = "이름 찾기",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0),
                },
                _search, _onlyMoving, _onlyAppeared, _revert, _bake,
            },
        };

        var page = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        DockPanel.SetDock(_skillPanel, Dock.Bottom);
        DockPanel.SetDock(_who, Dock.Bottom);
        page.Children.Add(bar);
        page.Children.Add(_status);
        page.Children.Add(_skillPanel);
        page.Children.Add(_who);
        page.Children.Add(_grid);
        Content = page;

        Loaded += (_, _) => Load();
    }

    // ── 고르는 칸의 목록 ───────────────────────────────────────────────────────

    /// <summary>고르는 칸 한 줄. 번호를 값으로 두고 이름을 보인다.</summary>
    private sealed record Choice(int Id, string Name);

    private static readonly Choice[] Buildings =
    [
        new(-1, "없음"), new(4, "주점"), new(5, "여관"),
    ];

    private static readonly Choice[] Hires =
    [
        new(0, "0 없음"), new(1, "대화만"), new(2, "고용가능"), new(3, "고용중"),
    ];

    /// <summary>이동 갈래. 2 는 후보를 하나도 못 담아 영영 안 움직인다.</summary>
    private static readonly Choice[] Kinds =
    [
        new(0, "0 같은 해역"), new(1, "1 같은 문화권"),
        new(2, "2 안 움직임"), new(3, "3 같은 나라"),
    ];

    private static readonly Choice[] Appears = [new(0, "미등장"), new(1, "등장")];

    /// <summary>등급. 2 이상이면 술집, 아니면 여관에 앉는다.</summary>
    private static readonly Choice[] Grades =
    [
        new(0, "0 (여관)"), new(1, "1 (여관)"), new(2, "2 (술집)"), new(3, "3 (술집)"),
    ];

    private void FillCityChoices()
    {
        _cityChoices.Add(new Choice(-1, "없음"));
        foreach (var city in CityTable.Open().Cities.OrderBy(c => c.Id))
            _cityChoices.Add(new Choice(city.Id, $"{city.Id} {city.Name}"));
    }

    // ── 칸 짜기 ────────────────────────────────────────────────────────────────

    private void BuildColumns()
    {
        Text("번호", nameof(PersonTable.Row.Id), 46, readOnly: true);
        Col("구분", nameof(PersonTable.Row.Id), new KindOfPerson(), 66);
        Text("이름", nameof(PersonTable.Row.First), 110);
        Text("성", nameof(PersonTable.Row.Last), 140);
        Text("나이", nameof(PersonTable.Row.Age), 48);
        Pick("등장", nameof(PersonTable.Row.Appear), Appears, 70);
        Pick("등급", nameof(PersonTable.Row.Grade), Grades, 82);
        Pick("도시", nameof(PersonTable.Row.City), _cityChoices, 138);
        Pick("건물", nameof(PersonTable.Row.Building), Buildings, 70);
        Pick("고용", nameof(PersonTable.Row.Hire), Hires, 84);
        Text("명성", nameof(PersonTable.Row.Fame), 58);
        Pick("이동 갈래", nameof(PersonTable.Row.Kind), Kinds, 108);
        Pick("목적지", nameof(PersonTable.Row.Dest), _cityChoices, 138);
        Text("날 셈", nameof(PersonTable.Row.Wait), 58);
    }

    private void Text(string header, string path, double width, bool readOnly = false) =>
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus },
            Width = new DataGridLength(width),
            IsReadOnly = readOnly,
        });

    private void Col(string header, string path, IValueConverter converter, double width) =>
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(path) { Converter = converter },
            Width = new DataGridLength(width),
            IsReadOnly = true,
        });

    private void Pick(string header, string path, System.Collections.IEnumerable source,
                      double width) =>
        _grid.Columns.Add(new DataGridComboBoxColumn
        {
            Header = header,
            ItemsSource = source,
            DisplayMemberPath = nameof(Choice.Name),
            SelectedValuePath = nameof(Choice.Id),
            SelectedValueBinding = new Binding(path),
            Width = new DataGridLength(width),
        });

    /// <summary>기술 열셋 · 언어 열넷을 고치는 판. 고른 줄의 것을 보인다.</summary>
    private void BuildSkillPanel()
    {
        for (int i = 0; i < Skill.Names.Length; i++) _skillBoxes.Add(Cell(Skill.Names[i], i, false));
        for (int i = 0; i < Skill.Languages.Length; i++)
            _langBoxes.Add(Cell(Skill.Languages[i], i, true));
    }

    private TextBox Cell(string label, int slot, bool language)
    {
        var box = new TextBox
        {
            Width = 26,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(3, 1, 0, 1),
        };
        box.LostFocus += (_, _) => TakeSkill(box, slot, language);

        _skillPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2, 10, 2),
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = language ? Brushes.SteelBlue : Brushes.Black,
                },
                box,
            },
        });
        return box;
    }

    // ── 읽고 그리기 ────────────────────────────────────────────────────────────

    private void Load()
    {
        _table = PersonTable.Open();
        if (_table.IsEmpty)
        {
            _status.Text = "인물 표가 비어 있습니다 — 같이 깔린 인물표.json 을 못 찾았습니다"
                         + (PersonTable.LastError.Length == 0 ? "" : $" ({PersonTable.LastError})");
            _grid.IsEnabled = false;
            _skillPanel.IsEnabled = false;
            _revert.IsEnabled = false;
            return;
        }
        Rebuild();
    }

    private void Rebuild()
    {
        if (_table is not { } table) return;

        int keep = _grid.SelectedItem is PersonTable.Row picked ? picked.Id : -1;
        string find = _search.Text.Trim();

        var rows = table.People.Where(r =>
                r.Name != "???"
                && (find.Length == 0 || r.Name.Contains(find, StringComparison.OrdinalIgnoreCase))
                && (_onlyMoving.IsChecked != true
                    || (r.Id >= PersonTable.VoyagerCount && r.Id < PersonTable.MovingEnd))
                && (_onlyAppeared.IsChecked != true || r.Appear != 0))
            .ToList();

        _grid.ItemsSource = rows;
        if (keep >= 0) _grid.SelectedItem = rows.FirstOrDefault(r => r.Id == keep);
        FillSkills(_grid.SelectedItem as PersonTable.Row);

        _revert.IsEnabled = PersonTable.Edited;
        _status.Text =
            $"인물 {rows.Count}명 보임 / 표 {PersonTable.Count}칸"
            + $" — {(PersonTable.Edited ? "고쳐 둔 것" : "같이 깔린 본")}"
            + (PersonTable.Source.Length == 0 ? "" : $" ({PersonTable.Source})")
            + "   ·   고친 것은 놀이 안에서도 그대로 쓰인다";
    }

    private void FillSkills(PersonTable.Row? row)
    {
        _picked = row;
        _skillPanel.IsEnabled = row != null;
        _who.Text = row == null
            ? "기술 열셋 · 언어 열넷 — 위에서 사람을 고르세요"
            : $"{row.Id}. {row.Name} — 기술 열셋 · 언어 열넷 (게임 한도는 {Skill.MaxLevel})";

        _filling = true;
        for (int i = 0; i < _skillBoxes.Count; i++)
            _skillBoxes[i].Text = row == null ? "" : row.Skills[i].ToString();
        for (int i = 0; i < _langBoxes.Count; i++)
            _langBoxes[i].Text = row == null ? "" : row.Languages[i].ToString();
        _filling = false;
    }

    private void TakeSkill(TextBox box, int slot, bool language)
    {
        if (_filling || _picked is not { } row) return;

        int was = language ? row.Languages[slot] : row.Skills[slot];
        if (!byte.TryParse(box.Text, out byte level))
        {
            box.Text = was.ToString();     // 숫자가 아니면 있던 값으로 되돌린다
            return;
        }
        if (level == was) return;

        if (language) row.Languages[slot] = level;
        else row.Skills[slot] = level;
        Save(row);
    }

    /// <summary>고친 표를 적어 둔다. 줄 하나를 만질 때마다 한 벌을 다시 적는다.</summary>
    /// <remarks>
    /// 고친 줄은 표가 들고 있는 그 줄이라 따로 넣을 것이 없다 — 적어 두기만 하면 된다.
    /// </remarks>
    private void Save(PersonTable.Row row)
    {
        _ = row;
        _table?.Save();
        Rebuild();
    }

    /// <summary>고쳐 둔 것을 걷어 같이 깔린 본으로 돌려놓는다.</summary>
    private void Revert()
    {
        if (MessageBox.Show(this,
                "고쳐 둔 것을 몽땅 걷고 같이 깔린 인물표.json 으로 돌려놓습니다.\n계속할까요?",
                "본으로 되돌리기", MessageBoxButton.OKCancel, MessageBoxImage.Question)
            != MessageBoxResult.OK) return;

        PersonTable.Forget();
        Load();
    }

    /// <summary>고른 세이브에서 표를 통째로 다시 굽는다.</summary>
    private void BakeFromSave()
    {
        var pick = new Microsoft.Win32.OpenFileDialog
        {
            Title = "표를 구울 세이브를 고르세요",
            Filter = "세이브 파일 (*.CDS)|*.CDS|모든 파일 (*.*)|*.*",
            FileName = AppSettings.LastSaveFilePath ?? "SAVEDATA.CDS",
        };
        if (pick.ShowDialog(this) != true) return;

        string error = PersonTable.Bake(pick.FileName);
        if (error.Length > 0)
        {
            MessageBox.Show(this, $"세이브에서 굽지 못했습니다:\n\n{error}", "인물 표 고치기",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Load();
    }

    /// <summary>번호로 갈리는 세 갈래를 이름으로 보인다.</summary>
    private sealed class KindOfPerson : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is not int i ? ""
            : i < PersonTable.VoyagerCount ? "역사"
            : i < PersonTable.MovingEnd ? "항해사"
            : "이벤트";

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }


    /// <summary>창을 띄운다.</summary>
    public static void Show(Window? owner)
    {
        var window = new PersonEditDialog();
        if (owner != null) window.Owner = owner;
        window.ShowDialog();
    }
}
