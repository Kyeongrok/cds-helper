using System.Collections.ObjectModel;
using System.Windows.Input;
using CdsHelper.Api.Entities;
using CdsHelper.Support.Local.Helpers;
using Prism.Commands;
using Prism.Mvvm;

namespace CdsHelper.Main.Local.ViewModels;

public class DiscoveryContentViewModel : BindableBase
{
    private readonly DiscoveryService _discoveryService;
    private List<DiscoveryEntity> _allDiscoveries = new();
    private Dictionary<int, List<int>> _parentMappings = new();

    #region Collections

    private ObservableCollection<DiscoveryDisplayItem> _discoveries = new();
    public ObservableCollection<DiscoveryDisplayItem> Discoveries
    {
        get => _discoveries;
        set => SetProperty(ref _discoveries, value);
    }

    #endregion

    #region Filter Properties

    private string _nameSearch = "";
    public string NameSearch
    {
        get => _nameSearch;
        set { SetProperty(ref _nameSearch, value); ApplyFilter(); }
    }

    private string _hintSearch = "";
    public string HintSearch
    {
        get => _hintSearch;
        set { SetProperty(ref _hintSearch, value); ApplyFilter(); }
    }

    private bool _showOnlyWithHint;
    public bool ShowOnlyWithHint
    {
        get => _showOnlyWithHint;
        set { SetProperty(ref _showOnlyWithHint, value); ApplyFilter(); }
    }

    #endregion

    #region Status

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    #endregion

    #region Commands

    public ICommand ResetFilterCommand { get; }

    #endregion

    public DiscoveryContentViewModel(DiscoveryService discoveryService)
    {
        _discoveryService = discoveryService;
        ResetFilterCommand = new DelegateCommand(ResetFilter);

        Initialize();
    }

    private async void Initialize()
    {
        try
        {
            StatusText = "발견물 데이터 로드 중...";

            _allDiscoveries = _discoveryService.GetAllDiscoveries().Values.ToList();
            _parentMappings = await _discoveryService.GetAllParentMappingsAsync();

            ApplyFilter();
            StatusText = $"발견물 로드 완료: {_allDiscoveries.Count}개";
        }
        catch (Exception ex)
        {
            StatusText = $"로드 실패: {ex.Message}";
        }
    }

    private void ApplyFilter()
    {
        var filtered = _allDiscoveries.AsEnumerable();

        // 이름 검색
        if (!string.IsNullOrWhiteSpace(NameSearch))
        {
            filtered = filtered.Where(d => d.Name.Contains(NameSearch, StringComparison.OrdinalIgnoreCase));
        }

        // 힌트 검색
        if (!string.IsNullOrWhiteSpace(HintSearch))
        {
            filtered = filtered.Where(d => d.Hint?.Name?.Contains(HintSearch, StringComparison.OrdinalIgnoreCase) == true);
        }

        // 힌트 있는 것만
        if (ShowOnlyWithHint)
        {
            filtered = filtered.Where(d => d.HintId != null);
        }

        var displayItems = filtered.Select(d => new DiscoveryDisplayItem
        {
            Id = d.Id,
            Name = d.Name,
            HintName = d.Hint?.Name ?? "",
            AppearCondition = d.AppearCondition ?? "",
            BookName = d.BookName ?? "",
            ParentNames = GetParentNames(d.Id)
        }).ToList();

        Discoveries = new ObservableCollection<DiscoveryDisplayItem>(displayItems);
        StatusText = $"발견물: {displayItems.Count}개";
    }

    private string GetParentNames(int discoveryId)
    {
        if (!_parentMappings.TryGetValue(discoveryId, out var parentIds))
            return "";

        var parentNames = parentIds
            .Select(pid => _allDiscoveries.FirstOrDefault(d => d.Id == pid)?.Name)
            .Where(n => n != null)
            .ToList();

        return string.Join(", ", parentNames);
    }

    private void ResetFilter()
    {
        NameSearch = "";
        HintSearch = "";
        ShowOnlyWithHint = false;
    }
}

/// <summary>
/// 발견물 표시용 모델
/// </summary>
public class DiscoveryDisplayItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string HintName { get; set; } = "";
    public string AppearCondition { get; set; } = "";
    public string BookName { get; set; } = "";
    public string ParentNames { get; set; } = "";
}
