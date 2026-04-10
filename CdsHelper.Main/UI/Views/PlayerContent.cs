using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using CdsHelper.Main.Local.ViewModels;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using Prism.Events;
using Prism.Ioc;

namespace CdsHelper.Main.UI.Views;

public class PlayerContent : ContentControl
{
    private DataGrid? _hintsDataGrid;
    private CheckBox? _filterCheckBox;
    private HintService? _hintService;

    static PlayerContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(PlayerContent),
            new FrameworkPropertyMetadata(typeof(PlayerContent)));
    }

    public PlayerContent()
    {
        Loaded += OnLoaded;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _hintsDataGrid = GetTemplateChild("PART_HintsDataGrid") as DataGrid;
        if (_hintsDataGrid != null)
        {
            _hintsDataGrid.SelectionChanged += HintsDataGrid_SelectionChanged;
        }

        _filterCheckBox = GetTemplateChild("PART_FilterUndiscovered") as CheckBox;
        if (_filterCheckBox != null)
        {
            _filterCheckBox.Checked += FilterCheckBox_Changed;
            _filterCheckBox.Unchecked += FilterCheckBox_Changed;
        }
    }

    private void FilterCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_hintsDataGrid?.ItemsSource == null) return;

        var view = CollectionViewSource.GetDefaultView(_hintsDataGrid.ItemsSource);
        if (_filterCheckBox?.IsChecked == true)
        {
            view.Filter = item => item is HintData hint && !hint.IsDiscovered;
        }
        else
        {
            view.Filter = null;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PlayerContentViewModel)
            return;

        var saveDataService = ContainerLocator.Container.Resolve<SaveDataService>();
        _hintService = ContainerLocator.Container.Resolve<HintService>();
        var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
        DataContext = new PlayerContentViewModel(saveDataService, _hintService, eventAggregator);
    }

    private async void HintsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_hintsDataGrid?.SelectedItem is not HintData hint) return;
        _hintService ??= ContainerLocator.Container.Resolve<HintService>();

        var books = await _hintService.GetBooksByHintIndexAsync(hint.Index - 1);
        if (books.Count == 0)
        {
            MessageBox.Show($"'{hint.Name}'을(를) 수록한 도서가 없습니다.",
                "도서 정보", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var vm = DataContext as PlayerContentViewModel;

        var window = new Window
        {
            Title = $"{hint.Name} - 수록 도서 ({books.Count}권)",
            SizeToContent = SizeToContent.Height,
            Width = 420,
            MaxHeight = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ResizeMode = ResizeMode.NoResize
        };

        var panel = new StackPanel { Margin = new Thickness(15) };

        foreach (var (bookName, language, required, condition, cities) in books)
        {
            bool meetsReq = CheckPartyMeetsRequirements(vm, language, required);

            var titlePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 2)
            };
            titlePanel.Children.Add(new TextBlock
            {
                Text = $"■ {bookName}",
                FontWeight = FontWeights.Bold,
                FontSize = 14
            });
            if (meetsReq)
            {
                titlePanel.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "가능",
                        Foreground = Brushes.White,
                        FontSize = 11,
                        FontWeight = FontWeights.Bold
                    }
                });
            }
            panel.Children.Add(titlePanel);

            if (!string.IsNullOrEmpty(language))
                panel.Children.Add(CreateInfoLine("언어", language));
            if (!string.IsNullOrEmpty(required))
                panel.Children.Add(CreateInfoLine("필요", required));
            if (!string.IsNullOrEmpty(condition))
                panel.Children.Add(CreateInfoLine("선행", condition));
            if (cities.Count > 0)
                panel.Children.Add(CreateInfoLine("도시", string.Join(", ", cities)));
        }

        window.Content = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        window.ShowDialog();
    }

    private static TextBlock CreateInfoLine(string label, string value)
    {
        return new TextBlock
        {
            Text = $"  {label}: {value}",
            FontSize = 13,
            Margin = new Thickness(0, 1, 0, 0)
        };
    }

    private static bool CheckPartyMeetsRequirements(PlayerContentViewModel? vm, string language, string required)
    {
        if (vm == null) return false;

        bool langOk = string.IsNullOrEmpty(language) ||
                      HasPartySkill(vm.CombinedLanguages, language, 1);
        bool skillOk = string.IsNullOrEmpty(required) ||
                       CheckRequiredSkill(vm.CombinedSkills, required);

        return langOk && skillOk;
    }

    private static bool HasPartySkill(ObservableCollection<SkillDisplayItem> items, string name, int minLevel)
    {
        var item = items.FirstOrDefault(i => i.Name == name);
        return item != null && item.BestLevel >= minLevel;
    }

    private static bool CheckRequiredSkill(ObservableCollection<SkillDisplayItem> skills, string required)
    {
        // "측량1" → prefix "측량", level 1
        int idx = required.Length;
        while (idx > 0 && char.IsDigit(required[idx - 1]))
            idx--;

        string prefix = required[..idx];
        int requiredLevel = idx < required.Length ? int.Parse(required[idx..]) : 1;

        var item = skills.FirstOrDefault(i => i.Name.StartsWith(prefix));
        return item != null && item.BestLevel >= requiredLevel;
    }
}
