using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Settings;
using Microsoft.Win32;

using CdsHelper.Game.Engine.Disev;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 발견 이벤트 편집기 — <c>DISEV.CDS</c> 의 파트를 발견물 이름으로 찾아 보고 고친다.
/// </summary>
/// <remarks>
/// 파트 하나가 발견물 하나고, 파트 안은 「조건 · 본문」 덩이 여럿이다
/// (<see cref="DisevPart"/>). 고치는 길은 둘이다.
/// <list type="number">
///   <item><b>명령 하나를 칸으로</b> — 뜻이 확실한 명령만 칸을 준다
///         (<see cref="DisevForm"/>). 칸 밖의 바이트는 손대지 않는다.</item>
///   <item><b>덩이 하나를 날바이트로</b> — 칸이 없는 명령이나 명령을 넣고 뺄 때 쓴다.</item>
/// </list>
/// 길이를 바꿔도 된다 — <see cref="DisevPart.Rebuild"/> 가 슬롯 표의 오프셋을 다시
/// 잡아 준다. 다만 <b>덩이 경계를 넘어 뛰는 상대 이동</b>은 못 고쳐 주므로,
/// 그런 명령이 있으면 창이 미리 일러 준다.
///
/// 저장은 고친 파트만 <b>압축 없이</b> 써 넣고 옆에 시각을 붙인 백업을 남긴다
/// (<see cref="DisevArchive.Save"/>).
/// </remarks>
public sealed class DisevEditorDialog : Window
{
    private readonly ListBox _discoveries = new() { Margin = new Thickness(0, 4, 0, 0) };

    /// <summary>발견물 이름으로 걸러 낸다. 글자를 칠 때마다 목록이 줄어든다.</summary>
    private readonly TextBox _find = new() { Padding = new Thickness(3, 2, 3, 2) };

    /// <summary>갈래로 걸러 낸다. 첫 줄이 「모두」다.</summary>
    private readonly ComboBox _category = new() { Margin = new Thickness(0, 4, 0, 0) };

    /// <summary>걸러 내고 몇 개가 남았는지.</summary>
    private readonly TextBlock _found = new()
    {
        Margin = new Thickness(2, 4, 0, 0),
        Foreground = System.Windows.Media.Brushes.Gray,
        FontSize = 11,
    };
    private readonly ListBox _chunks = new() { Height = 92, Margin = new Thickness(4, 6, 10, 4) };

    private readonly DataGrid _ops = new()
    {
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        IsReadOnly = true,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        SelectionMode = DataGridSelectionMode.Single,
        FontFamily = new FontFamily("Consolas, D2Coding, 맑은 고딕"),
        Margin = new Thickness(4, 0, 10, 4),
    };

    /// <summary>고른 명령의 칸들이 들어앉는 자리.</summary>
    private readonly WrapPanel _form = new() { Margin = new Thickness(4, 2, 10, 2) };

    private readonly Button _applyOp = Bar("명령 적용");
    private readonly Button _wide = Bar("전각으로");

    private readonly TextBox _hex = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new FontFamily("Consolas, D2Coding"),
        Height = 84,
        Margin = new Thickness(4, 0, 10, 4),
    };

    private readonly Button _open = Bar("원본 폴더 고르기");
    private readonly Button _applyChunk = Bar("덩이 적용");
    private readonly Button _revert = Bar("이 발견물만 원본으로");
    private readonly Button _revertAll = Bar("원본에서 다시 뜨기");
    private readonly Button _save = Bar("저장");
    private readonly Button _bake = Bar("게임에 굽기");
    private readonly TextBlock _status = new() { Margin = new Thickness(10, 4, 10, 8), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _header = new() { Margin = new Thickness(4, 6, 10, 2) };

    private DisevBook? _book;

    /// <summary>원본이 있는 게임 폴더 — 굽거나 되돌릴 때만 쓴다.</summary>
    private string _gameDir = "";
    private DiscoveryTable? _names;
    private ItemTable? _items;
    private CityTable? _cities;
    private DisevPart? _part;

    /// <summary>지금 칸에 걸린 명령과 그 칸들.</summary>
    private DisevScript.Op? _op;
    private DisevForm.Field[] _fields = [];
    private readonly List<TextBox> _boxes = [];
    private TextBox? _flagBox, _speakerBox, _textBox;

    public DisevEditorDialog()
    {
        Title = "발견 이벤트 편집기 (DISEV.CDS)";
        Width = 1280;
        Height = 860;
        MinWidth = 900;
        MinHeight = 640;
        // 바탕을 못 박는다 — 안 주면 창 테마에 딸려 글씨가 안 보이는 자리가 생긴다.
        Background = System.Windows.Media.Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Col("자리", nameof(OpRow.At), 60);
        Col("바이트", nameof(OpRow.Hex), 300);
        Col("풀이", nameof(OpRow.Text), 0);      // 남는 자리를 다 먹는다

        _open.Click += (_, _) => Pick();
        _applyChunk.Click += (_, _) => ApplyChunk();
        _applyOp.Click += (_, _) => ApplyOp();
        _wide.Click += (_, _) => { if (_textBox != null) _textBox.Text = DisevForm.ToWide(_textBox.Text); };
        _revert.Click += (_, _) => RevertOne();
        _revertAll.Click += (_, _) => RevertAll();
        _save.Click += (_, _) => Save();
        _bake.Click += (_, _) => Bake();

        _discoveries.SelectionChanged += (_, _) => ShowPart();

        // 글자를 칠 때마다·갈래를 고를 때마다 목록을 다시 짠다.
        _find.TextChanged += (_, _) => RefreshDiscoveries();
        _category.SelectionChanged += (_, _) => RefreshDiscoveries();
        _chunks.SelectionChanged += (_, _) => ShowChunk();
        _ops.SelectionChanged += (_, _) => BuildForm();

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 8, 10, 0),
            Children = { _open, _applyChunk, _revert, _revertAll, _save, _bake },
        };

        var formBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(4, 0, 10, 4),
            Children = { _applyOp, _wide },
        };

        var formHost = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
            BorderThickness = new Thickness(0, 1, 0, 1),
            Margin = new Thickness(0, 2, 0, 2),
            Child = new StackPanel { Children = { _form, formBar } },
        };

        var hexLabel = Label("덩이 날바이트 — 명령을 넣고 빼거나 칸이 없는 명령을 고칠 때 쓴다");
        var right = new DockPanel();
        DockPanel.SetDock(_header, Dock.Top);
        DockPanel.SetDock(_chunks, Dock.Top);
        DockPanel.SetDock(_hex, Dock.Bottom);
        DockPanel.SetDock(hexLabel, Dock.Bottom);
        DockPanel.SetDock(formHost, Dock.Bottom);
        right.Children.Add(_header);
        right.Children.Add(_chunks);
        right.Children.Add(_hex);
        right.Children.Add(hexLabel);
        right.Children.Add(formHost);
        right.Children.Add(_ops);

        // 왼쪽 기둥 — 찾기 칸과 갈래 칸을 목록 위에 얹는다.
        _category.Items.Add(AllCategories);
        foreach (string name in DiscoveryTable.CategoryNames) _category.Items.Add(name);
        _category.SelectedIndex = 0;

        var picker = new DockPanel { Width = 240, Margin = new Thickness(10, 6, 4, 6) };
        DockPanel.SetDock(_find, Dock.Top);
        DockPanel.SetDock(_category, Dock.Top);
        DockPanel.SetDock(_found, Dock.Top);
        picker.Children.Add(_find);
        picker.Children.Add(_category);
        picker.Children.Add(_found);
        picker.Children.Add(_discoveries);

        var body = new DockPanel();
        DockPanel.SetDock(picker, Dock.Left);
        body.Children.Add(picker);
        body.Children.Add(right);

        var page = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        page.Children.Add(bar);
        page.Children.Add(_status);
        page.Children.Add(body);
        Content = page;

        Loaded += (_, _) => OpenDefault();
    }

    /// <summary>임자 창 가운데에 띄운다.</summary>
    public static void Show(Window owner) =>
        new DisevEditorDialog { Owner = owner }.ShowDialog();

    private static Button Bar(string text) => new()
    {
        Content = text,
        Padding = new Thickness(10, 3, 10, 3),
        Margin = new Thickness(0, 0, 6, 0),
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = Brushes.DimGray,
        Margin = new Thickness(4, 4, 10, 2),
    };

    /// <summary>
    /// 명령 목록의 칸 하나. <paramref name="width"/> 가 0 이면 <b>남는 자리를 다 먹는다</b>.
    /// </summary>
    /// <remarks>
    /// 마지막 「풀이」 칸을 못 박아 두었더니 세 칸을 더한 폭이 창보다 넓어 오른쪽이
    /// 잘려 나갔다. 남는 만큼 늘어나게 두어야 창 크기와 상관없이 다 보인다.
    /// </remarks>
    private void Col(string header, string path, double width) =>
        _ops.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(path),
            Width = width > 0 ? new DataGridLength(width) : new DataGridLength(1, DataGridLengthUnitType.Star),
        });

    /// <summary>명령 목록 한 줄.</summary>
    private sealed class OpRow
    {
        public string At { get; init; } = "";
        public string Hex { get; init; } = "";
        public string Text { get; init; } = "";
        public DisevScript.Op Op { get; init; }
    }

    /// <summary>덩이 목록 한 줄.</summary>
    private sealed class ChunkRow
    {
        public int Start { get; init; }
        public string Text { get; init; } = "";
        public override string ToString() => Text;
    }

    /// <summary>발견물 목록 한 줄.</summary>
    private sealed class PartRow
    {
        public int Index { get; init; }
        public string Text { get; init; } = "";
        public override string ToString() => Text;
    }

    /// <summary>
    /// 창이 뜨면 곧장 연다 — 적어 둔 <c>발견이벤트.json</c> 이 있으면 그것을, 없으면
    /// 세이브를 연 폴더의 <c>DISEV.CDS</c> 를 떠서 적고 그것을.
    /// </summary>
    private void OpenDefault() => Load(GameFolder());

    /// <summary>게임 폴더를 고른다 — 적어 둔 것이 없거나 딴 판을 뜰 때 쓴다.</summary>
    private void Pick()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "게임 폴더 고르기 (DISEV.CDS 가 있는 곳)",
            InitialDirectory = GameFolder(),
        };
        if (dialog.ShowDialog(this) == true) Load(dialog.FolderName, fresh: true);
    }

    /// <summary>세이브를 연 폴더가 곧 게임 폴더다 — 앱의 다른 데도 그렇게 잡는다.</summary>
    private static string GameFolder() =>
        Path.GetDirectoryName(AppSettings.LastSaveFilePath) ?? "";

    /// <summary>
    /// 대본 책을 연다 — <b>읽는 것은 <c>발견이벤트.json</c></b> 이다.
    /// </summary>
    /// <remarks>
    /// 그 파일이 없으면 <see cref="DisevBook"/> 이 게임 폴더의 <c>DISEV.CDS</c> 를 통째로
    /// 떠서 <b>먼저 적어 두고</b> 그것을 읽는다. 이 집이 EXE 표를 다루는 결과 같다.
    /// </remarks>
    /// <param name="dir">게임 폴더.</param>
    /// <param name="fresh">참이면 적어 둔 것을 버리고 원본에서 다시 뜬다.</param>
    private void Load(string dir, bool fresh = false)
    {
        _gameDir = dir;
        _book = fresh ? DisevBook.Dump(dir) : DisevBook.Open(dir);

        if (_book == null)
        {
            _status.Text = $"열지 못했습니다 — {DisevBook.LastError}";
            _discoveries.ItemsSource = null;
            _chunks.ItemsSource = null;
            _ops.ItemsSource = null;
            return;
        }

        // 이름표는 게임 폴더의 EXE 에서 온다. 없어도 번호로는 다룰 수 있다.
        _names = DiscoveryTable.Open(dir);
        _items = ItemTable.Open(dir);
        _cities = CityTable.Open();

        RefreshDiscoveries();
        if (_discoveries.Items.Count > 0) _discoveries.SelectedIndex = 0;

        string missing = _names == null ? "  (CDS_95.EXE 를 못 읽어 이름 없이 번호로만 보입니다)" : "";
        _status.Text = $"{DisevBook.Path_} — 파트 {_book.Count}개{missing}";
    }

    /// <summary>갈래 칸의 첫 줄 — 거르지 않는다는 뜻이다.</summary>
    private const string AllCategories = "갈래 모두";

    /// <summary>
    /// 목록을 다시 짠다. <see cref="_find"/> 의 글자와 <see cref="_category"/> 의 갈래로 거른다.
    /// </summary>
    /// <remarks>
    /// <b>고른 줄은 목록 자리가 아니라 발견물 번호로 붙든다.</b> 거르고 나면 자리가 통째로
    /// 밀리므로 자리로 붙들면 엉뚱한 발견물이 뜬다.
    ///
    /// 번호로도 찾게 해 두었다 — "137" 을 치면 137번이 걸린다. 이름을 모르는 채 자리만
    /// 아는 일이 잦다.
    /// </remarks>
    private void RefreshDiscoveries()
    {
        if (_book == null) return;
        int keep = SelectedPart;

        string find = _find.Text.Trim();
        string pick = _category.SelectedItem as string ?? AllCategories;

        var rows = new List<PartRow>(_book.Count);
        for (int i = 0; i < _book.Count; i++)
        {
            var record = _names?.Find(i);
            string name = record?.Name ?? $"발견물 {i}";
            string category = record?.CategoryName ?? "";

            if (pick != AllCategories && category != pick) continue;
            if (find.Length > 0
                && !name.Contains(find, StringComparison.OrdinalIgnoreCase)
                && !$"{i:000}".Contains(find)
                && i.ToString() != find) continue;

            string tail = category.Length > 0 ? $" · {category}" : "";
            string mark = _book.IsEdited(i) ? " ●" : "";
            rows.Add(new PartRow { Index = i, Text = $"{i:000}  {name}{tail}{mark}" });
        }

        _discoveries.ItemsSource = rows;

        // 붙들던 줄이 걸러져 나갔으면 첫 줄로 옮긴다 — 빈 채로 두면 오른쪽이 통째로 빈다.
        _discoveries.SelectedItem = rows.FirstOrDefault(r => r.Index == keep) ?? rows.FirstOrDefault();
        _found.Text = rows.Count == _book.Count
            ? $"{rows.Count}개"
            : $"{rows.Count}개 보임 / 모두 {_book.Count}개";
    }

    private int SelectedPart => (_discoveries.SelectedItem as PartRow)?.Index ?? -1;

    private void ShowPart()
    {
        _chunks.ItemsSource = null;
        _ops.ItemsSource = null;
        _hex.Clear();
        ClearForm();
        _part = null;

        if (_book == null || SelectedPart < 0) return;

        var data = _book.Part(SelectedPart);
        _part = DisevPart.Parse(data, out string error);
        if (_part == null)
        {
            _header.Text = $"파트 {SelectedPart}: 뼈대를 못 읽었습니다 — {error}";
            return;
        }

        _header.Text = $"파트 {SelectedPart} · {data.Length}바이트 · 단계 번호 {_part.Step} · "
                     + $"슬롯 {_part.Slots.Count}개 · 덩이 {_part.ChunkStarts.Count}개"
                     + (_book.IsEdited(SelectedPart) ? "   ● 고침" : "");

        var rows = _part.ChunkStarts
            .Select(start =>
            {
                var (from, to) = _part.ChunkRange(start);
                return new ChunkRow
                {
                    Start = start,
                    // 무엇에 쓰이는 덩이인지를 <b>앞에</b> 적는다 — 조건인지 본문인지가
                    // 자리·크기보다 먼저 눈에 들어와야 한다.
                    Text = $"{_part.UsersOf(start),-14}  +0x{start:X4}  {to - from,4}바이트",
                };
            })
            .ToList();

        _chunks.ItemsSource = rows;

        // <b>첫 덩이는 대개 조건이다.</b> 자리로 늘어놓으면 조건이 본문보다 앞에 서는데,
        // 조건 덩이는 「조건 없음」이면 FF 한 줄뿐이라 골라 봐야 아무것도 안 나온다.
        // 그래서 <b>0번 슬롯의 본문</b>을 먼저 편다 — 사람이 보고 싶은 것은 그쪽이다.
        int first = _part.Slots.Count > 0
            ? rows.FindIndex(r => r.Start == _part.Slots[0].Body)
            : -1;
        if (first < 0 && rows.Count > 0) first = 0;
        if (first >= 0) _chunks.SelectedIndex = first;
    }

    private void ShowChunk()
    {
        _ops.ItemsSource = null;
        _hex.Clear();
        ClearForm();
        if (_part == null || _chunks.SelectedItem is not ChunkRow chunk) return;

        var (from, to) = _part.ChunkRange(chunk.Start);
        int still = _names?.Find(SelectedPart)?.Picture ?? -1;
        var ops = DisevScript.Parse(_part.Data, from, to, still);

        _ops.ItemsSource = ops.Select(op => new OpRow
        {
            At = $"+0x{op.Offset:X4}",
            Hex = op.Hex.Length <= 54 ? op.Hex : op.Hex[..54] + " …",
            Text = op.Text,
            Op = op,
        }).ToList();

        _hex.Text = DisevScript.Hex(_part.Chunk(chunk.Start));

        // 덩이 밖으로 뛰는 상대 이동이 있으면 길이를 바꿀 때 어긋난다 — 미리 일러 준다.
        int outside = ops.Count(op => op.Text.Contains("→ 파트 +0x") && !InsideChunk(op.Text, from, to));
        _status.Text = outside == 0
            ? $"덩이 +0x{chunk.Start:X4} — 명령 {ops.Count}개"
            : $"덩이 +0x{chunk.Start:X4} — 명령 {ops.Count}개, "
              + $"덩이 밖으로 뛰는 이동 {outside}개 있음 (길이를 바꾸면 어긋납니다)";
    }

    private static bool InsideChunk(string text, int from, int to)
    {
        int at = text.LastIndexOf("→ 파트 +0x", StringComparison.Ordinal);
        if (at < 0) return true;
        string digits = new(text[(at + "→ 파트 +0x".Length)..].TakeWhile(Uri.IsHexDigit).ToArray());
        return int.TryParse(digits, System.Globalization.NumberStyles.HexNumber, null, out int target)
               && target >= from && target <= to;
    }

    // ── 명령 하나를 칸으로 고치기 ──────────────────────────────────────────

    private void ClearForm()
    {
        _form.Children.Clear();
        _boxes.Clear();
        _flagBox = _speakerBox = _textBox = null;
        _fields = [];
        _op = null;
        _applyOp.IsEnabled = false;
        _wide.IsEnabled = false;
    }

    /// <summary>고른 명령에 맞는 칸을 깐다.</summary>
    private void BuildForm()
    {
        ClearForm();
        if (_part == null || _ops.SelectedItem is not OpRow row) return;

        var op = row.Op;
        _op = op;
        var raw = RawOf(op);

        if (op.Kind == "대사")
        {
            BuildDialogueForm(raw);
            _applyOp.IsEnabled = true;
            _wide.IsEnabled = true;
            return;
        }

        _fields = DisevForm.FieldsFor(op);
        if (_fields.Length == 0)
        {
            _form.Children.Add(new TextBlock
            {
                Text = $"{op.Kind} — 칸으로 고칠 수 있는 명령이 아닙니다. 아래 날바이트로 고치세요.",
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 4, 0, 4),
            });
            return;
        }

        foreach (var field in _fields)
        {
            long value = DisevForm.Read(raw, field);
            var box = new TextBox { Text = value.ToString(), Width = field.Width == 4 ? 92 : 64 };
            var hint = new TextBlock
            {
                Foreground = Brushes.SteelBlue,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 12, 0),
                MinWidth = 8,
                Text = NameOf(field, value),
            };
            var captured = field;
            box.TextChanged += (_, _) =>
                hint.Text = long.TryParse(box.Text, out long v) ? NameOf(captured, v) : "?";

            _boxes.Add(box);
            _form.Children.Add(new TextBlock
            {
                Text = field.Label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
            });
            _form.Children.Add(box);
            _form.Children.Add(hint);
        }
        _applyOp.IsEnabled = true;
    }

    /// <summary>칸 값 뒤에 붙는 이름 — 발견물·아이템·도시·능력치.</summary>
    private string NameOf(DisevForm.Field field, long value) => field.Kind switch
    {
        DisevForm.Lookup.Stat => DisevScript.StatNames.TryGetValue((int)value, out var s) ? s : "",
        DisevForm.Lookup.Discovery => _names?.Find((int)value)?.Name ?? "",
        DisevForm.Lookup.Item => _items?.Find((int)value)?.Name ?? "",
        DisevForm.Lookup.City => _cities?.NameOf((int)value) ?? "",
        DisevForm.Lookup.Relative => _op is { } op ? $"→ 파트 +0x{op.Offset + op.Length + value:X}" : "",
        _ => "",
    };

    private void BuildDialogueForm(byte[] raw)
    {
        var (flag, tag) = DisevForm.SplitDialogue(raw);
        int textStart = (flag == null ? 1 : 2) + (tag.Length > 0 ? tag.Length + 2 : 0);
        int textEnd = raw.Length > 0 && raw[^1] == 0x00 ? raw.Length - 1 : raw.Length;
        var (speakerName, body) = DisevScript.DecodeDialogue(
            raw.AsSpan(textStart, Math.Max(0, textEnd - textStart)), normalize: false);

        _flagBox = new TextBox { Text = flag?.ToString() ?? "", Width = 48 };
        _speakerBox = new TextBox
        {
            Text = DisevScript.Hex(tag),
            Width = 190,
            FontFamily = new FontFamily("Consolas, D2Coding"),
        };
        _textBox = new TextBox { Text = body, Width = 620, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false };

        _form.Children.Add(Cell("창 플래그", _flagBox, "비우면 0A 로 바로 연다"));
        _form.Children.Add(Cell($"화자 ({(speakerName ?? "없음")})", _speakerBox, "CP932 태그 날바이트. 비우면 화자 없음"));
        _form.Children.Add(Cell("본문", _textBox, ""));
    }

    private static UIElement Cell(string label, TextBox box, string hint)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 2, 12, 2) };
        stack.Children.Add(new TextBlock { Text = label, Foreground = Brushes.DimGray });
        stack.Children.Add(box);
        if (hint.Length > 0)
            stack.Children.Add(new TextBlock { Text = hint, Foreground = Brushes.Gray, FontSize = 10 });
        return stack;
    }

    private byte[] RawOf(DisevScript.Op op)
    {
        int length = Math.Min(op.Length, (_part?.Data.Length ?? 0) - op.Offset);
        return _part == null || length <= 0 ? [] : _part.Data.AsSpan(op.Offset, length).ToArray();
    }

    /// <summary>칸에 적은 값으로 명령 하나를 갈아 끼운다.</summary>
    private void ApplyOp()
    {
        if (_book == null || _part == null || _op is not { } op ||
            _chunks.SelectedItem is not ChunkRow chunk) return;

        byte[] replacement;
        if (op.Kind == "대사")
        {
            int? flag = null;
            if (!string.IsNullOrWhiteSpace(_flagBox?.Text))
            {
                if (!int.TryParse(_flagBox!.Text, out int value) || value is < 0 or > 255)
                {
                    _status.Text = "창 플래그는 0 ~ 255 이거나 비어 있어야 합니다.";
                    return;
                }
                flag = value;
            }

            var tag = DisevScript.ParseHex(_speakerBox?.Text ?? "");
            if (tag == null)
            {
                _status.Text = "화자 태그를 못 읽었습니다 — 16진 두 자리씩 적어 주세요.";
                return;
            }
            replacement = DisevForm.BuildDialogue(flag, tag, _textBox?.Text ?? "");
        }
        else
        {
            replacement = RawOf(op);
            for (int i = 0; i < _fields.Length && i < _boxes.Count; i++)
            {
                if (!long.TryParse(_boxes[i].Text, out long value))
                {
                    _status.Text = $"{_fields[i].Label}: 숫자가 아닙니다.";
                    return;
                }
                var next = DisevForm.Write(replacement, _fields[i], value, out string error);
                if (next == null)
                {
                    _status.Text = error;
                    return;
                }
                replacement = next;
            }
        }

        // 덩이 안에서 그 명령 자리만 갈아 끼운다.
        var chunkBytes = _part.Chunk(chunk.Start);
        int at = op.Offset - chunk.Start;
        int len = Math.Min(op.Length, chunkBytes.Length - at);
        if (at < 0 || len < 0)
        {
            _status.Text = "명령 자리가 덩이 밖입니다.";
            return;
        }

        var merged = new List<byte>(chunkBytes.Length + replacement.Length);
        merged.AddRange(chunkBytes[..at]);
        merged.AddRange(replacement);
        merged.AddRange(chunkBytes[(at + len)..]);

        Commit(chunk.Start, merged.ToArray(),
               $"파트 {SelectedPart} +0x{op.Offset:X4} 「{op.Kind}」 을 {replacement.Length}바이트로 고쳤습니다");
    }

    private void ApplyChunk()
    {
        if (_book == null || _part == null || _chunks.SelectedItem is not ChunkRow chunk)
        {
            _status.Text = "고칠 덩이를 먼저 고르세요.";
            return;
        }

        var bytes = DisevScript.ParseHex(_hex.Text);
        if (bytes == null)
        {
            _status.Text = "날바이트를 못 읽었습니다 — 16진 두 자리씩 적어 주세요.";
            return;
        }
        if (bytes.Length == 0)
        {
            _status.Text = "덩이를 비울 수는 없습니다.";
            return;
        }

        Commit(chunk.Start, bytes,
               $"파트 {SelectedPart} 덩이 +0x{chunk.Start:X4} 를 {bytes.Length}바이트로 고쳤습니다");
    }

    /// <summary>고친 덩이를 파트에 넣고 화면을 다시 그린다.</summary>
    private void Commit(int chunkStart, byte[] chunkBytes, string message)
    {
        if (_book == null || _part == null) return;

        var rebuilt = _part.Rebuild(chunkStart, chunkBytes, out string error);
        if (rebuilt == null)
        {
            _status.Text = $"못 고쳤습니다 — {error}";
            return;
        }
        if (DisevPart.Parse(rebuilt, out string check) == null)
        {
            _status.Text = $"고친 뒤 뼈대가 깨집니다 — {check}";
            return;
        }

        int part = SelectedPart, chunkIndex = _chunks.SelectedIndex, opIndex = _ops.SelectedIndex;
        _book.Replace(part, rebuilt);
        RefreshDiscoveries();
        ShowPart();
        if (chunkIndex >= 0 && chunkIndex < _chunks.Items.Count) _chunks.SelectedIndex = chunkIndex;
        if (opIndex >= 0 && opIndex < _ops.Items.Count) _ops.SelectedIndex = opIndex;
        _status.Text = message + " — 아직 적어 두지 않았습니다. 「저장」을 눌러야 들어갑니다.";
    }

    /// <summary>이 발견물만 원본 대본으로 되돌린다.</summary>
    private void RevertOne()
    {
        if (_book == null || SelectedPart < 0) return;

        if (!_book.Restore(SelectedPart, _gameDir))
        {
            _status.Text = $"되돌리지 못했습니다 — {DisevBook.LastError}";
            return;
        }

        _book.Save();
        RefreshDiscoveries();
        ShowPart();
        _status.Text = $"파트 {SelectedPart} 를 원본 대본으로 되돌렸습니다.";
    }

    /// <summary>적어 둔 것을 통째로 버리고 원본에서 다시 뜬다.</summary>
    private void RevertAll()
    {
        if (!ConfirmDialog.Ask(this,
                "적어 둔 대본을 버리고 DISEV.CDS 에서 다시 뜹니다. 고친 것이 다 사라집니다. 좋습니까?",
                "원본에서 다시 뜨기"))
            return;

        Load(_gameDir, fresh: true);
        if (_book != null) _status.Text = $"원본에서 다시 떴습니다 — 파트 {_book.Count}개";
    }

    /// <summary>
    /// 고친 것을 <c>발견이벤트.json</c> 에 적어 둔다 — 원본 <c>DISEV.CDS</c> 는 안 건드린다.
    /// </summary>
    /// <remarks>
    /// 적어 두면 <b>우리 놀이에는 곧장 든다</b> — 발견하러 가면 그 대본이 돈다
    /// (<see cref="DisevRunner.Open"/> 이 이 책을 읽는다). 원본 게임에 먹이려면
    /// 「게임에 굽기」를 한 번 더 눌러야 한다.
    /// </remarks>
    private void Save()
    {
        if (_book == null) return;
        if (!_book.HasChanges)
        {
            _status.Text = "고친 것이 없습니다.";
            return;
        }

        _book.Save();
        RefreshDiscoveries();
        ShowPart();
        _status.Text = "적어 두었습니다 — 놀이에는 바로 듭니다. "
                     + $"원본 게임에 먹이려면 「게임에 굽기」를 누르세요. ({DisevBook.Path_})";
    }

    /// <summary>
    /// 적어 둔 대본을 <c>DISEV.CDS</c> 에 굽는다 — <b>원본 게임에 먹일 때만</b> 쓴다.
    /// </summary>
    /// <remarks>
    /// <c>CDS_95.EXE</c> 는 우리 JSON 을 모른다. 굽기 전에 파트를 죄다 되읽어 대 보고
    /// 날짜 붙인 <c>.bak</c> 을 남긴 뒤에 덮는다. EXE 패치 창이 <c>custom_patches.json</c> 을
    /// 두고 「적용」할 때만 EXE 를 건드리는 것과 같은 차례다.
    /// </remarks>
    private void Bake()
    {
        if (_book == null) return;

        if (!ConfirmDialog.Ask(this,
                "게임 폴더의 DISEV.CDS 를 다시 씁니다. 원본은 .bak 으로 남깁니다. 좋습니까?",
                "게임에 굽기"))
            return;

        string? backup = _book.Bake(_gameDir);
        _status.Text = backup == null
            ? $"굽지 못했습니다 — {DisevBook.LastError}"
            : $"DISEV.CDS 에 구웠습니다. 백업: {Path.GetFileName(backup)}";
    }
}
