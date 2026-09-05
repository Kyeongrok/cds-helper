using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.UI.Units;
using Microsoft.Win32;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 그림 한 장을 골라 <b>게임 초상화로 넣는</b> 창.
/// </summary>
/// <remarks>
/// 게임 초상화는 <b>80x96 · 256색 색인</b>이다. 그래서 두 걸음을 거친다 —
/// 네모에 맞추고(비율이 다르면 넘치게 키워 잘라 낸다), 색을 게임 팔레트로 줄인다.
/// 넣기 전에 <b>줄인 결과 그대로</b>를 옆에 보여 주므로 색이 어떻게 뭉개지는지
/// 눈으로 보고 고를 수 있다.
///
/// 넣는 데는 <c>asset\MALE.CDS</c> · <c>asset\FEMALE.CDS</c> 다. 게임 파일은 안 건드리고,
/// 처음 손댈 때 옆에 <c>.bak</c> 을 하나 남긴다.
/// </remarks>
public sealed class PortraitAddDialog : Window
{
    /// <summary>보여 줄 때 키우는 배수 — 도트를 눈으로 재려면 이만큼은 커야 한다.</summary>
    private const int Zoom = 3;

    private readonly Image _before = Frame();
    private readonly Image _after = Frame();
    private readonly TextBlock _status = new()
    {
        Margin = new Thickness(0, 8, 0, 0),
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly TextBlock _picked = new()
    {
        Margin = new Thickness(0, 0, 0, 6),
        Foreground = Brushes.DimGray,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private readonly RadioButton _male = new() { Content = "남자", IsChecked = true };
    private readonly RadioButton _female = new()
    {
        Content = "여자",
        Margin = new Thickness(10, 0, 0, 0),
    };

    private readonly RadioButton _cover = new()
    {
        Content = "넘치게 키우고 자른다",
        IsChecked = true,
    };
    private readonly RadioButton _contain = new()
    {
        Content = "통째로 줄여 넣는다",
        Margin = new Thickness(10, 0, 0, 0),
    };

    private readonly CheckBox _append = new()
    {
        Content = "맨 뒤에 새로 붙인다",
        IsChecked = true,
        Margin = new Thickness(0, 0, 12, 0),
    };
    private readonly NumericSpinner _at = new()
    {
        Minimum = 0,
        Maximum = 9999,
        Step = 1,
        DecimalPlaces = 0,
        Width = 90,
        IsEnabled = false,
    };

    /// <summary>넣고 나서 그 얼굴을 누구의 중년 얼굴로 삼을지. 안 삼으면 꺼 둔다.</summary>
    private readonly CheckBox _pair = new()
    {
        Content = "이 얼굴을 다음 얼굴의 중년으로 삼는다",
        Margin = new Thickness(0, 0, 12, 0),
    };
    private readonly NumericSpinner _pairWith = new()
    {
        Minimum = 0,
        Maximum = 9999,
        Step = 1,
        DecimalPlaces = 0,
        Width = 90,
        IsEnabled = false,
    };

    private string _source = "";
    private byte[]? _indexed;

    /// <summary>넣고 나면 그 얼굴 번호. 안 넣었으면 −1.</summary>
    public int Added { get; private set; } = -1;

    public PortraitAddDialog()
    {
        Title = "초상화 넣기";
        Width = 560;
        Height = 480;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var open = new Button { Content = "그림 고르기…", Padding = new Thickness(12, 3, 12, 3) };
        open.Click += (_, _) => Choose();

        _male.Checked += (_, _) => Reshape();
        _female.Checked += (_, _) => Reshape();
        _cover.Checked += (_, _) => Reshape();
        _contain.Checked += (_, _) => Reshape();
        _append.Checked += (_, _) => { _at.IsEnabled = false; Tell(); };
        _append.Unchecked += (_, _) => { _at.IsEnabled = true; Tell(); };
        _pair.Checked += (_, _) => { _pairWith.IsEnabled = true; Tell(); };
        _pair.Unchecked += (_, _) => { _pairWith.IsEnabled = false; Tell(); };

        var put = new Button
        {
            Content = "넣는다",
            Padding = new Thickness(16, 3, 16, 3),
            Margin = new Thickness(0, 0, 8, 0),
        };
        put.Click += (_, _) => Put();

        var close = new Button { Content = "닫기", Padding = new Thickness(16, 3, 16, 3) };
        close.Click += (_, _) => Close();

        var pictures = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 6),
            Children = { Titled("맞춘 그림", _before), Titled("게임 색으로", _after) },
        };

        var rows = new StackPanel { Margin = new Thickness(14) };
        rows.Children.Add(Line(open, _picked));
        rows.Children.Add(pictures);
        rows.Children.Add(Line(new TextBlock { Text = "성별", Width = 60 }, _male, _female));
        rows.Children.Add(Line(new TextBlock { Text = "맞추기", Width = 60 }, _cover, _contain));
        rows.Children.Add(Line(new TextBlock { Text = "자리", Width = 60 }, _append, _at));
        rows.Children.Add(Line(new TextBlock { Text = "중년", Width = 60 }, _pair, _pairWith));
        rows.Children.Add(_status);
        rows.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
            Children = { put, close },
        });
        Content = rows;

        Tell();
    }

    /// <summary>창을 띄운다. 넣었으면 그 얼굴 번호, 아니면 −1.</summary>
    public static int Show(Window? owner)
    {
        var window = new PortraitAddDialog();
        if (owner != null) window.Owner = owner;
        window.ShowDialog();
        return window.Added;
    }

    // ── 고르고 맞추기 ──────────────────────────────────────────────────────────

    private void Choose()
    {
        var box = new OpenFileDialog
        {
            Title = "초상화로 넣을 그림",
            Filter = "그림 (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
                     + "|모든 파일 (*.*)|*.*",
        };
        if (box.ShowDialog(this) != true) return;

        _source = box.FileName;
        _picked.Text = _source;
        Reshape();
    }

    private void Reshape()
    {
        if (_source.Length == 0) return;

        var fit = _contain.IsChecked == true
            ? PortraitImport.Fit.Contain : PortraitImport.Fit.Cover;

        var shaped = PortraitImport.Shape(_source, fit);
        if (shaped == null)
        {
            _status.Text = PortraitImport.LastError;
            _status.Foreground = Brushes.Firebrick;
            return;
        }

        _indexed = PortraitImport.Quantize(shaped);
        _before.Source = Bitmap(shaped);
        _after.Source = Bitmap(PortraitImport.Preview(_indexed));
        Tell();
    }

    /// <summary>지금 어디에 넣게 되는지 한 줄로 이른다.</summary>
    private void Tell()
    {
        bool female = _female.IsChecked == true;
        string path = PortraitImport.PathOf(female);
        if (path.Length == 0)
        {
            _status.Text = $"asset 폴더에 {(female ? "FEMALE" : "MALE")}.CDS 가 없습니다";
            _status.Foreground = Brushes.Firebrick;
            return;
        }

        var faces = Portraits.Open();
        int count = faces == null ? 0 : female ? faces.FemaleCount : faces.MaleCount;
        _at.Maximum = Math.Max(0, count);

        string where = _append.IsChecked == true
            ? $"맨 뒤({count}번)에 새로 붙인다"
            : $"{(int)_at.Value}번을 갈아 끼운다";

        string aging = _pair.IsChecked == true
            ? $" {(int)_pairWith.Value}번이 서른여섯 살부터 이 얼굴로 바뀐다."
            : "";


        _status.Text = $"{Path.GetFileName(path)} 에 {where}. "
                       + $"지금 {count}장이 들어 있다. 처음 손댈 때 옆에 .bak 을 남긴다."
                       + aging;
        _status.Foreground = Brushes.DimGray;
    }

    private void Put()
    {
        if (_indexed is not { } indexed)
        {
            _status.Text = "먼저 그림을 고르세요";
            _status.Foreground = Brushes.Firebrick;
            return;
        }

        bool female = _female.IsChecked == true;
        var faces = Portraits.Open();
        int count = faces == null ? 0 : female ? faces.FemaleCount : faces.MaleCount;
        int at = _append.IsChecked == true ? count : (int)_at.Value;

        int put = PortraitImport.Put(female, at, indexed);
        if (put < 0)
        {
            _status.Text = $"못 넣었습니다 — {PortraitImport.LastError}";
            _status.Foreground = Brushes.Firebrick;
            return;
        }

        // 「중년으로 삼는다」를 켜 두었으면 그 짝을 적어 둔다 — 이 짝이 없으면 나이가
        // 들어도 얼굴이 안 바뀐다(PortraitAges).
        string paired = "";
        if (_pair.IsChecked == true)
        {
            int young = (int)_pairWith.Value;
            PortraitAges.Set(young, put, female);
            paired = $" {young}번의 중년 얼굴로 짝지었습니다.";
        }

        // Portraits 는 열 때마다 파일을 다시 읽으므로 따로 놓아 줄 것이 없다.
        Added = put;
        _status.Text = $"{put}번으로 넣었습니다.{paired}";
        _status.Foreground = Brushes.SeaGreen;
        Tell();
    }

    // ── 잔손 ───────────────────────────────────────────────────────────────────

    private static Image Frame()
    {
        var image = new Image
        {
            Width = Portraits.Width * Zoom,
            Height = Portraits.Height * Zoom,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
        return image;
    }

    private static BitmapSource Bitmap(uint[] bgra)
    {
        var made = BitmapSource.Create(Portraits.Width, Portraits.Height, 96, 96,
                                       PixelFormats.Bgra32, null, bgra,
                                       Portraits.Width * 4);
        made.Freeze();
        return made;
    }

    private static UIElement Titled(string title, UIElement what) => new StackPanel
    {
        Margin = new Thickness(8, 0, 8, 0),
        Children =
        {
            new TextBlock
            {
                Text = title,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = Brushes.DimGray,
            },
            new Border
            {
                BorderBrush = Brushes.Silver,
                BorderThickness = new Thickness(1),
                Child = what,
            },
        },
    };

    private static UIElement Line(params UIElement[] parts)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 6),
        };
        foreach (var part in parts)
        {
            if (part is FrameworkElement box) box.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(part);
        }
        return row;
    }
}
