using System.Windows;
using System.Windows.Controls;
using CdsHelper.Form.Local.ViewModels;
using CdsHelper.Main.UI.Views;
using CdsHelper.Support.Local.Settings;
using CdsHelper.Support.UI.Units;

namespace CdsHelper.Form.UI.Views;

[TemplatePart(Name = PART_SettingsMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_EventQueueMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_AccordionMenu, Type = typeof(AccordionControl))]
[TemplatePart(Name = PART_ContentRegion, Type = typeof(ContentControl))]
public class CdsHelperWindow : CdsWindow
{
    private const string PART_SettingsMenu = "PART_SettingsMenu";
    private const string PART_EventQueueMenu = "PART_EventQueueMenu";
    private const string PART_AccordionMenu = "PART_AccordionMenu";
    private const string PART_ContentRegion = "PART_ContentRegion";

    private CdsHelperViewModel? _viewModel;
    private readonly IRegionManager _regionManager;

    static CdsHelperWindow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(CdsHelperWindow),
            new FrameworkPropertyMetadata(typeof(CdsHelperWindow)));
    }

    public CdsHelperWindow(CdsHelperViewModel viewModel, IRegionManager regionManager)
    {
        _viewModel = viewModel;
        _regionManager = regionManager;
        DataContext = viewModel;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (GetTemplateChild(PART_SettingsMenu) is MenuItem settingsMenu)
        {
            settingsMenu.Click += OnSettingsMenuClick;
        }

        if (GetTemplateChild(PART_EventQueueMenu) is MenuItem eventQueueMenu)
        {
            eventQueueMenu.Click += OnEventQueueMenuClick;
        }

        if (GetTemplateChild(PART_AccordionMenu) is AccordionControl accordionMenu)
        {
            accordionMenu.ItemClickCommand = new DelegateCommand<string>(OnAccordionItemClick);
            SelectAccordionItemByTag(accordionMenu, AppSettings.DefaultView);
        }

        // ControlTemplate 내의 ContentControl에 Region 설정
        if (GetTemplateChild(PART_ContentRegion) is ContentControl contentRegion)
        {
            RegionManager.SetRegionManager(contentRegion, _regionManager);
            RegionManager.SetRegionName(contentRegion, "MainContentRegion");

            // 초기 Navigation (설정에서 지정한 기본 뷰)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _viewModel?.NavigateToContent(AppSettings.DefaultView);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void OnAccordionItemClick(string? viewName)
    {
        System.Diagnostics.Debug.WriteLine($"[AccordionClick] viewName: {viewName}");
        if (!string.IsNullOrEmpty(viewName))
        {
            _viewModel?.NavigateToContent(viewName);
        }
    }

    private void SelectAccordionItemByTag(ItemsControl parent, string tag)
    {
        foreach (var item in parent.Items)
        {
            if (item is AccordionItem accordionItem)
            {
                if (accordionItem.Tag?.ToString() == tag)
                {
                    accordionItem.IsSelected = true;
                    return;
                }

                // 하위 항목 검색
                SelectAccordionItemByTag(accordionItem, tag);
            }
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

    private void OnEventQueueMenuClick(object sender, RoutedEventArgs e)
    {
        var dialog = new EventQueueDialog
        {
            Owner = this
        };
        dialog.ShowDialog();
    }
}
