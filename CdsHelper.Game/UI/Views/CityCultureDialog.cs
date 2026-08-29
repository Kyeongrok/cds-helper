using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 도시마다의 문화권과, 그 문화권이 부르는 시설 화자 얼굴을 맞대어 보는 창.
/// </summary>
/// <remarks>
/// 같은 조선소라도 마을에 따라 딴 사람이 말을 건다 — 화자표(<c>0x0056823C</c>)를
/// <c>[건물코드][문화권]</c> 으로 읽기 때문이다. 도시를 고르면 그 마을 문화권의 얼굴이
/// 서고, 문화권을 손으로 갈아 보면 <b>같은 건물이 어떻게 바뀌는지</b>가 한눈에 보인다.
///
/// 보기만 한다 — 여기서 문화권을 갈아도 게임 판에는 손대지 않는다.
/// </remarks>
public sealed class CityCultureDialog : Window
{
    /// <summary>얼굴을 낼 시설들. 화자가 없는 자택·저택 따위는 뺐다.</summary>
    private static readonly (int Code, string Name)[] Kinds =
    [
        (0, "항구"), (1, "교역소"), (2, "왕궁"), (3, "교회"), (4, "술집"),
        (5, "여관"), (6, "조선소"), (7, "시장"), (8, "도서관"), (9, "조합"), (10, "성문"),
    ];

    private readonly DataGrid _cities = new()
    {
        AutoGenerateColumns = false,
        IsReadOnly = true,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        AlternatingRowBackground = Brushes.WhiteSmoke,
        Width = 320,
    };

    private readonly ComboBox _culture = new() { Width = 150, Margin = new Thickness(8, 0, 0, 0) };
    private readonly WrapPanel _faces = new() { Margin = new Thickness(8) };
    private readonly TextBlock _status = new() { Margin = new Thickness(10, 6, 10, 8) };

    private CityTable? _names;
    private CityExeTable? _rows;
    private SpeakerFaceTable? _speakers;
    private Portraits? _portraits;

    public CityCultureDialog()
    {
        Title = "도시 · 문화권 — 시설 화자";
        Width = 980;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Col("번호", nameof(Row.Id), 56);
        Col("도시", nameof(Row.Name), 130);
        Col("문화권", nameof(Row.CultureNo), 60);
        Col("이름", nameof(Row.Culture), 90);
        _cities.SelectionChanged += (_, _) => PickCity();

        _culture.SelectionChanged += (_, _) => ShowFaces();

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 8, 10, 4),
            Children =
            {
                new TextBlock { Text = "문화권을 갈아 보기:", VerticalAlignment = VerticalAlignment.Center },
                _culture,
            },
        };

        var right = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        right.Children.Add(bar);
        right.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _faces,
        });

        var split = new DockPanel();
        DockPanel.SetDock(_cities, Dock.Left);
        split.Children.Add(_cities);
        split.Children.Add(right);

        var page = new DockPanel();
        DockPanel.SetDock(_status, Dock.Bottom);
        page.Children.Add(_status);
        page.Children.Add(split);
        Content = page;

        Loaded += (_, _) => Load();
    }

    /// <summary>목록 한 줄 — 도시와 그 문화권.</summary>
    private sealed record Row(int Id, string Name, int CultureNo, string Culture);

    private void Col(string header, string path, double width) => _cities.Columns.Add(
        new DataGridTextColumn
        {
            Header = header,
            Binding = new System.Windows.Data.Binding(path),
            Width = new DataGridLength(width),
        });

    private void Load()
    {
        string dir = Path.GetDirectoryName(AppSettings.LastSaveFilePath) ?? "";
        _names = CityTable.Open();
        _rows = CityExeTable.Open(dir);
        _speakers = SpeakerFaceTable.Open(dir);
        _portraits = Portraits.Open(dir);

        if (_rows == null || _speakers == null)
        {
            _status.Text = "표를 못 읽었습니다 — 세이브를 한 번 열어 게임 폴더를 알려 주세요"
                         + $" ({CityExeTable.LastError} {SpeakerFaceTable.LastError})".TrimEnd();
            return;
        }

        for (int i = 0; i < SpeakerFaceTable.Cultures; i++) _culture.Items.Add(i);

        var rows = new List<Row>();
        foreach (var city in _names.Cities)
        {
            int no = _rows.CultureOf(city.Id);
            rows.Add(new Row(city.Id, city.Name, no, city.Culture));
        }
        _cities.ItemsSource = rows;

        _status.Text = $"도시 {rows.Count}곳 · 문화권 {SpeakerFaceTable.Cultures}가지 — "
                     + "화자표 0x0056823C 를 [건물코드][문화권] 으로 읽는다";
        if (rows.Count > 0) _cities.SelectedIndex = 0;
    }

    /// <summary>고른 도시의 문화권으로 콤보를 맞춰 준다.</summary>
    private void PickCity()
    {
        if (_cities.SelectedItem is not Row row) return;
        _culture.SelectedItem = row.CultureNo;   // 고르면 ShowFaces 가 따라 돈다
        ShowFaces();
    }

    /// <summary>그 문화권일 때 시설마다 누가 말을 거는지.</summary>
    private void ShowFaces()
    {
        _faces.Children.Clear();
        if (_speakers is not { } speakers || _culture.SelectedItem is not int culture) return;

        foreach (var (code, name) in Kinds)
        {
            int face = speakers.FaceOf(code, culture);
            _faces.Children.Add(Cell(name, code, face));
        }
    }

    /// <summary>시설 한 칸 — 이름 · 얼굴 · 번호.</summary>
    private UIElement Cell(string name, int code, int face)
    {
        var box = new StackPanel { Margin = new Thickness(8), Width = 96 };
        box.Children.Add(new TextBlock
        {
            Text = $"{name} ({code})",
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4),
        });

        var px = face < 0 ? null : _portraits?.TryGetBgra(face, female: false);
        if (px != null)
        {
            var bmp = BitmapSource.Create(Portraits.Width, Portraits.Height, 96, 96,
                                          PixelFormats.Bgra32, null, px, Portraits.Width * 4);
            bmp.Freeze();

            var image = new Image { Source = bmp, Width = Portraits.Width, Height = Portraits.Height };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            box.Children.Add(image);
        }
        else
        {
            box.Children.Add(new Border
            {
                Width = Portraits.Width,
                Height = Portraits.Height,
                Background = Brushes.Gainsboro,
                Child = new TextBlock
                {
                    Text = face < 0 ? "없음" : "그림 없음",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });
        }

        box.Children.Add(new TextBlock
        {
            Text = face < 0 ? "—" : face.ToString(),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
        });
        return box;
    }
}
