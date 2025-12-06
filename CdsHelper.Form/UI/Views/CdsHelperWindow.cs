using System.Windows;
using System.Windows.Controls;
using CdsHelper.Form.Local.ViewModels;

namespace CdsHelper.Form.UI.Views;

[TemplatePart(Name = PART_SettingsMenu, Type = typeof(MenuItem))]
public class CdsHelperWindow : Window
{
    private const string PART_SettingsMenu = "PART_SettingsMenu";

    static CdsHelperWindow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(CdsHelperWindow),
            new FrameworkPropertyMetadata(typeof(CdsHelperWindow)));
    }

    public CdsHelperWindow(CdsHelperViewModel viewModel)
    {
        DataContext = viewModel;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (GetTemplateChild(PART_SettingsMenu) is MenuItem settingsMenu)
        {
            settingsMenu.Click += OnSettingsMenuClick;
        }
    }

    private void OnSettingsMenuClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog
        {
            Owner = this
        };
        dialog.ShowDialog();
    }
}
