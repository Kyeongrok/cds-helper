using System.Windows;
using CdsHelper.Form.Local.ViewModels;

namespace CdsHelper.Form.UI.Views;

public class CdsHelperWindow : Window
{
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
}
