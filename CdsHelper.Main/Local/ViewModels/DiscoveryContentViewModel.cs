using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CdsHelper.Api.Entities;
using CdsHelper.Support.Local.Events;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Settings;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;

namespace CdsHelper.Main.Local.ViewModels;

public class DiscoveryContentViewModel : BindableBase
{
    private readonly DiscoveryService _discoveryService;
    private readonly SaveDataService _saveDataService;
    private readonly IEventAggregator _eventAggregator;
    private List<DiscoveryEntity> _allDiscoveries = new();
    private Dictionary<int, List<int>> _parentMappings = new();
    private HashSet<int>? _discoveredHintIds;
    private HashSet<int>? _hasHintIds;

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

    private int _discoveryFilterIndex;
    public int DiscoveryFilterIndex
    {
        get => _discoveryFilterIndex;
        set { SetProperty(ref _discoveryFilterIndex, value); ApplyFilter(); }
    }

    // 0: 전체, 1: 발견, 2: 미발견
    public List<string> DiscoveryFilterOptions { get; } = new() { "전체", "발견", "미발견" };

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

    public DiscoveryContentViewModel(
        DiscoveryService discoveryService,
        SaveDataService saveDataService,
        IEventAggregator eventAggregator)
    {
        _discoveryService = discoveryService;
        _saveDataService = saveDataService;
        _eventAggregator = eventAggregator;
        ResetFilterCommand = new DelegateCommand(ResetFilter);

        // 세이브 데이터 로드 이벤트 구독
        eventAggregator.GetEvent<SaveDataLoadedEvent>().Subscribe(OnSaveDataLoaded);

        Initialize();
    }

    private void OnSaveDataLoaded(SaveDataLoadedEventArgs args)
    {
        UpdateHintStatus();
        ApplyFilter();
    }

    private void UpdateHintStatus()
    {
        if (_saveDataService.CurrentSaveGameInfo?.Hints == null) return;

        _discoveredHintIds = _saveDataService.CurrentSaveGameInfo.Hints
            .Where(h => h.IsDiscovered)
            .Select(h => h.Index - 1) // 1-based -> 0-based (Hint ID)
            .ToHashSet();

        _hasHintIds = _saveDataService.CurrentSaveGameInfo.Hints
            .Where(h => h.HasHint)
            .Select(h => h.Index - 1) // 1-based -> 0-based (Hint ID)
            .ToHashSet();

        System.Diagnostics.Debug.WriteLine($"[Discovery] HasHintIds count: {_hasHintIds?.Count ?? 0}");
        System.Diagnostics.Debug.WriteLine($"[Discovery] DiscoveredHintIds count: {_discoveredHintIds?.Count ?? 0}");
        if (_hasHintIds?.Count > 0)
            System.Diagnostics.Debug.WriteLine($"[Discovery] HasHintIds: {string.Join(", ", _hasHintIds.Take(20))}");
    }

    private async void Initialize()
    {
        try
        {
            StatusText = "발견물 데이터 로드 중...";

            _allDiscoveries = _discoveryService.GetAllDiscoveries().Values.ToList();
            _parentMappings = await _discoveryService.GetAllParentMappingsAsync();

            // 세이브 데이터가 있으면 힌트 상태 업데이트
            UpdateHintStatus();

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

        // 발견 상태 필터 (0: 전체, 1: 발견, 2: 미발견)
        if (DiscoveryFilterIndex == 1)
        {
            filtered = filtered.Where(d => d.HintId.HasValue && _discoveredHintIds?.Contains(d.HintId.Value) == true);
        }
        else if (DiscoveryFilterIndex == 2)
        {
            filtered = filtered.Where(d => !d.HintId.HasValue || _discoveredHintIds?.Contains(d.HintId.Value) != true);
        }

        var displayItems = filtered.Select(d =>
        {
            var item = new DiscoveryDisplayItem
            {
                Id = d.Id,
                Name = d.Name,
                HintId = d.HintId,
                HintName = d.Hint?.Name ?? "",
                AppearCondition = d.AppearCondition ?? "",
                BookName = d.BookName ?? "",
                ParentNames = GetParentNames(d.Id),
                IsHintObtained = d.HintId.HasValue && _hasHintIds?.Contains(d.HintId.Value) == true,
                IsDiscoveryFound = d.HintId.HasValue && _discoveredHintIds?.Contains(d.HintId.Value) == true
            };
            // 저장된 체크 상태 로드 (setter 호출 없이 직접 설정)
            item.SetCheckedWithoutSave(AppSettings.IsDiscoveryChecked(d.Id));
            return item;
        }).ToList();

        // 디버그: 힌트가 있는 발견물 몇 개 출력
        var withHint = displayItems.Where(d => d.HintId.HasValue).Take(5).ToList();
        foreach (var d in withHint)
        {
            System.Diagnostics.Debug.WriteLine($"[Discovery] {d.Name} HintId={d.HintId}, IsHintObtained={d.IsHintObtained}, IsDiscoveryFound={d.IsDiscoveryFound}");
        }

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
        DiscoveryFilterIndex = 0;
    }
}

/// <summary>
/// 발견물 표시용 모델
/// </summary>
public class DiscoveryDisplayItem : INotifyPropertyChanged
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? HintId { get; set; }
    public string HintName { get; set; } = "";
    public string AppearCondition { get; set; } = "";
    public string BookName { get; set; } = "";
    public string ParentNames { get; set; } = "";

    /// <summary>
    /// 힌트 획득 여부 (연한 초록색)
    /// </summary>
    public bool IsHintObtained { get; set; }

    /// <summary>
    /// 발견물 발견 여부 (연한 파란색)
    /// </summary>
    public bool IsDiscoveryFound { get; set; }

    /// <summary>
    /// 발견여부 표시 (O / 빈값)
    /// </summary>
    public string DiscoveryStatusDisplay => IsDiscoveryFound ? "O" : "";

    private bool _isChecked;
    /// <summary>
    /// 사용자가 체크한 발견물 여부
    /// </summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked != value)
            {
                _isChecked = value;
                AppSettings.SetDiscoveryChecked(Id, value);
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 저장 없이 체크 상태만 설정 (로드 시 사용)
    /// </summary>
    public void SetCheckedWithoutSave(bool value)
    {
        _isChecked = value;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
