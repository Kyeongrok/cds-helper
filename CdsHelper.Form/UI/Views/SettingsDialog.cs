using System.Windows;
using System.Windows.Controls;
using CdsHelper.Support.Local.Settings;

namespace CdsHelper.Form.UI.Views;

[TemplatePart(Name = PART_MarkerSizeSlider, Type = typeof(Slider))]
[TemplatePart(Name = PART_DefaultViewComboBox, Type = typeof(ComboBox))]
[TemplatePart(Name = PART_OkButton, Type = typeof(Button))]
[TemplatePart(Name = PART_CancelButton, Type = typeof(Button))]
public class SettingsDialog : Window
{
    private const string PART_MarkerSizeSlider = "PART_MarkerSizeSlider";
    private const string PART_DefaultViewComboBox = "PART_DefaultViewComboBox";
    private const string PART_OkButton = "PART_OkButton";
    private const string PART_CancelButton = "PART_CancelButton";

    private Slider? _markerSizeSlider;
    private ComboBox? _defaultViewComboBox;

    public static readonly DependencyProperty MarkerSizeProperty =
        DependencyProperty.Register(nameof(MarkerSize), typeof(double), typeof(SettingsDialog),
            new PropertyMetadata(AppSettings.DefaultMarkerSize));

    public double MarkerSize
    {
        get => (double)GetValue(MarkerSizeProperty);
        set => SetValue(MarkerSizeProperty, value);
    }

    public List<ViewOption> AvailableViews => AppSettings.AvailableViews;

    static SettingsDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(SettingsDialog),
            new FrameworkPropertyMetadata(typeof(SettingsDialog)));
    }

    public SettingsDialog()
    {
        Title = "설정";
        Width = 400;
        Height = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        // 현재 설정 로드
        MarkerSize = AppSettings.MarkerSize;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _markerSizeSlider = GetTemplateChild(PART_MarkerSizeSlider) as Slider;
        _defaultViewComboBox = GetTemplateChild(PART_DefaultViewComboBox) as ComboBox;

        if (GetTemplateChild(PART_OkButton) is Button okButton)
            okButton.Click += OnOkClick;

        if (GetTemplateChild(PART_CancelButton) is Button cancelButton)
            cancelButton.Click += OnCancelClick;

        if (_markerSizeSlider != null)
            _markerSizeSlider.Value = MarkerSize;

        if (_defaultViewComboBox != null)
        {
            _defaultViewComboBox.ItemsSource = AvailableViews;
            _defaultViewComboBox.DisplayMemberPath = "DisplayName";
            _defaultViewComboBox.SelectedValuePath = "Name";
            _defaultViewComboBox.SelectedValue = AppSettings.DefaultView;
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (_markerSizeSlider != null)
        {
            MarkerSize = _markerSizeSlider.Value;
            AppSettings.MarkerSize = MarkerSize;
        }

        if (_defaultViewComboBox?.SelectedValue is string selectedView)
        {
            AppSettings.DefaultView = selectedView;
        }

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
