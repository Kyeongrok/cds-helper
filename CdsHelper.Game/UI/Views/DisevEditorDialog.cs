using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Settings;
using Microsoft.Win32;

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
    private readonly ListBox _discoveries = new() { Width = 240, Margin = new Thickness(10, 6, 4, 6) };
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

    private readonly Button _open = Bar("DISEV 열기");
    private readonly Button _applyChunk = Bar("덩이 적용");
    private readonly Button _revert = Bar("이 발견물 되돌리기");
    private readonly Button _revertAll = Bar("전부 되돌리기");
    private readonly Button _save = Bar("저장");
    private readonly TextBlock _status = new() { Margin = new Thickness(10, 4, 10, 8), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _header = new() { Margin = new Thickness(4, 6, 10, 2) };

    private DisevArchive? _archive;
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
        Width = 1220;
        Height = 800;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Col("자리", nameof(OpRow.At), 70);
        Col("바이트", nameof(OpRow.Hex), 330);
        Col("풀이", nameof(OpRow.Text), 660);

        _open.Click += (_, _) => Pick();
        _applyChunk.Click += (_, _) => ApplyChunk();
        _applyOp.Click += (_, _) => ApplyOp();
        _wide.Click += (_, _) => { if (_textBox != null) _textBox.Text = DisevForm.ToWide(_textBox.Text); };
        _revert.Click += (_, _) => RevertOne();
        _revertAll.Click += (_, _) => { _archive?.RevertAll(); RefreshDiscoveries(); ShowPart(); };
        _save.Click += (_, _) => Save();

        _discoveries.SelectionChanged += (_, _) => ShowPart();
        _chunks.SelectionChanged += (_, _) => ShowChunk();
        _ops.SelectionChanged += (_, _) => BuildForm();

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 8, 10, 0),
            Children = { _open, _applyChunk, _revert, _revertAll, _save },
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

        var body = new DockPanel();
        DockPanel.SetDock(_discoveries, Dock.Left);
        body.Children.Add(_discoveries);
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

    private void Col(string header, string path, double width) =>
        _ops.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(path),
            Width = new DataGridLength(width),
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

    /// <summary>세이브를 연 폴더에 <c>DISEV.CDS</c> 가 있으면 그것부터 연다.</summary>
    private void OpenDefault()
    {
        string dir = Path.GetDirectoryName(AppSettings.LastSaveFilePath) ?? "";
        string guess = Path.Combine(dir, "DISEV.CDS");
        if (File.Exists(guess)) Load(guess);
        else _status.Text = "「DISEV 열기」로 게임 폴더의 DISEV.CDS 를 골라 주세요.";
    }

    private void Pick()
    {
        string dir = Path.GetDirectoryName(AppSettings.LastSaveFilePath) ?? "";
        var dialog = new OpenFileDialog
        {
            Title = "DISEV.CDS 열기",
            Filter = "발견 이벤트 (DISEV.CDS)|DISEV.CDS|CDS 아카이브 (*.cds)|*.cds|모든 파일|*.*",
            InitialDirectory = Directory.Exists(dir) ? dir : "",
        };
        if (dialog.ShowDialog(this) == true) Load(dialog.FileName);
    }

    private void Load(string path)
    {
        _archive = DisevArchive.Open(path);
        if (_archive == null)
        {
            _status.Text = $"열지 못했습니다 — {DisevArchive.LastError}";
            _discoveries.ItemsSource = null;
            _chunks.ItemsSource = null;
            _ops.ItemsSource = null;
            return;
        }

        // 이름표는 같은 폴더의 EXE 에서 온다. 없어도 번호로는 다룰 수 있다.
        string dir = Path.GetDirectoryName(path) ?? "";
        _names = DiscoveryTable.Open(dir);
        _items = ItemTable.Open(dir);
        _cities = CityTable.Open();

        RefreshDiscoveries();
        if (_discoveries.Items.Count > 0) _discoveries.SelectedIndex = 0;

        string missing = _names == null ? " (CDS_95.EXE 를 못 읽어 이름 없이 번호로만 보입니다)" : "";
        _status.Text = $"{path} — 파트 {_archive.PartCount}개{missing}";
    }

    private void RefreshDiscoveries()
    {
        if (_archive == null) return;
        int keep = _discoveries.SelectedIndex;

        var rows = new List<PartRow>(_archive.PartCount);
        for (int i = 0; i < _archive.PartCount; i++)
        {
            var record = _names?.Find(i);
            string name = record?.Name ?? $"발견물 {i}";
            string category = record is { } row && row.CategoryName.Length > 0 ? $" · {row.CategoryName}" : "";
            string mark = _archive.IsModified(i) ? " ●" : "";
            rows.Add(new PartRow { Index = i, Text = $"{i:000}  {name}{category}{mark}" });
        }

        _discoveries.ItemsSource = rows;
        if (keep >= 0 && keep < rows.Count) _discoveries.SelectedIndex = keep;
    }

    private int SelectedPart => (_discoveries.SelectedItem as PartRow)?.Index ?? -1;

    private void ShowPart()
    {
        _chunks.ItemsSource = null;
        _ops.ItemsSource = null;
        _hex.Clear();
        ClearForm();
        _part = null;

        if (_archive == null || SelectedPart < 0) return;

        var data = _archive.Part(SelectedPart);
        _part = DisevPart.Parse(data, out string error);
        if (_part == null)
        {
            _header.Text = $"파트 {SelectedPart}: 뼈대를 못 읽었습니다 — {error}";
            return;
        }

        _header.Text = $"파트 {SelectedPart} · {data.Length}바이트 · 단계 번호 {_part.Step} · "
                     + $"슬롯 {_part.Slots.Count}개 · 덩이 {_part.ChunkStarts.Count}개"
                     + (_archive.IsModified(SelectedPart) ? "   ● 고침" : "");

        var rows = _part.ChunkStarts
            .Select(start =>
            {
                var (from, to) = _part.ChunkRange(start);
                return new ChunkRow
                {
                    Start = start,
                    Text = $"덩이 +0x{start:X4}  {to - from,4}바이트  ({_part.UsersOf(start)})",
                };
            })
            .ToList();

        _chunks.ItemsSource = rows;
        if (rows.Count > 0) _chunks.SelectedIndex = 0;
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
        if (_archive == null || _part == null || _op is not { } op ||
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
        if (_archive == null || _part == null || _chunks.SelectedItem is not ChunkRow chunk)
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
        if (_archive == null || _part == null) return;

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
        _archive.ReplacePart(part, rebuilt);
        RefreshDiscoveries();
        ShowPart();
        if (chunkIndex >= 0 && chunkIndex < _chunks.Items.Count) _chunks.SelectedIndex = chunkIndex;
        if (opIndex >= 0 && opIndex < _ops.Items.Count) _ops.SelectedIndex = opIndex;
        _status.Text = message + " — 아직 파일에는 안 썼습니다. 「저장」을 눌러야 들어갑니다.";
    }

    private void RevertOne()
    {
        if (_archive == null || SelectedPart < 0) return;
        _archive.Revert(SelectedPart);
        RefreshDiscoveries();
        ShowPart();
        _status.Text = $"파트 {SelectedPart} 를 원래대로 되돌렸습니다.";
    }

    private void Save()
    {
        if (_archive == null) return;
        if (!_archive.HasChanges)
        {
            _status.Text = "고친 것이 없습니다.";
            return;
        }

        string? backup = _archive.Save();
        if (backup == null)
        {
            _status.Text = $"저장하지 못했습니다 — {DisevArchive.LastError}";
            return;
        }

        RefreshDiscoveries();
        ShowPart();
        _status.Text = $"저장했습니다. 백업: {Path.GetFileName(backup)}";
    }
}
