using System.Collections.ObjectModel;
using System.Windows.Input;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using Prism.Commands;
using Prism.Mvvm;

namespace CdsHelper.Main.Local.ViewModels;

public class ItemContentViewModel : BindableBase
{
    private readonly ItemService _itemService;

    private List<Item> _allItems = new();

    #region Collections

    private ObservableCollection<Item> _items = new();
    public ObservableCollection<Item> Items
    {
        get => _items;
        set => SetProperty(ref _items, value);
    }

    #endregion

    #region Filter Properties

    private string _itemNameSearch = "";
    public string ItemNameSearch
    {
        get => _itemNameSearch;
        set { SetProperty(ref _itemNameSearch, value); ApplyFilter(); }
    }

    private string? _selectedItemCategory;
    public string? SelectedItemCategory
    {
        get => _selectedItemCategory;
        set { SetProperty(ref _selectedItemCategory, value); ApplyFilter(); }
    }

    private string _itemDiscoverySearch = "";
    public string ItemDiscoverySearch
    {
        get => _itemDiscoverySearch;
        set { SetProperty(ref _itemDiscoverySearch, value); ApplyFilter(); }
    }

    public ObservableCollection<string> ItemCategories { get; } = new();

    #endregion

    #region Status Properties

    private string _statusText = "준비됨";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    #endregion

    #region Commands

    public ICommand ResetFilterCommand { get; }

    #endregion

    public ItemContentViewModel(ItemService itemService)
    {
        _itemService = itemService;

        ResetFilterCommand = new DelegateCommand(ResetFilter);

        Initialize();
    }

    private void Initialize()
    {
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        var itemsPath = System.IO.Path.Combine(basePath, "item.json");

        if (System.IO.File.Exists(itemsPath))
            LoadItems(itemsPath);
    }

    private void LoadItems(string filePath)
    {
        try
        {
            _allItems = _itemService.LoadItems(filePath);

            ItemCategories.Clear();
            foreach (var category in _itemService.GetDistinctCategories())
                ItemCategories.Add(category);

            ApplyFilter();
            StatusText = $"아이템 로드 완료: {_allItems.Count}개";
        }
        catch (Exception ex)
        {
            StatusText = "아이템 로드 실패";
            System.Windows.MessageBox.Show($"아이템 파일 읽기 실패:\n\n{ex.Message}",
                "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void ApplyFilter()
    {
        if (_allItems.Count == 0) return;

        var filtered = _itemService.Filter(
            string.IsNullOrWhiteSpace(ItemNameSearch) ? null : ItemNameSearch,
            SelectedItemCategory,
            string.IsNullOrWhiteSpace(ItemDiscoverySearch) ? null : ItemDiscoverySearch);

        Items = new ObservableCollection<Item>(filtered);
        StatusText = $"아이템: {filtered.Count}개";
    }

    private void ResetFilter()
    {
        ItemNameSearch = "";
        SelectedItemCategory = null;
        ItemDiscoverySearch = "";
    }
}
