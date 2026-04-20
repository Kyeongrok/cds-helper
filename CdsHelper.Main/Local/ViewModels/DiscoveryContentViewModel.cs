using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CdsHelper.Api.Entities;
using CdsHelper.Support.Local.Events;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Settings;
using Prism.Events;

namespace CdsHelper.Main.Local.ViewModels;

public partial class DiscoveryContentViewModel : ObservableObject
{
    private readonly DiscoveryService _discoveryService;
    private readonly SaveDataService _saveDataService;
    private readonly IEventAggregator _eventAggregator;
    private List<DiscoveryEntity> _allDiscoveries = new();
    private Dictionary<int, List<int>> _parentMappings = new();
    private HashSet<int>? _discoveredHintIds;
    private HashSet<int>? _hasHintIds;

    #region Collections

    [ObservableProperty] private ObservableCollection<DiscoveryDisplayItem> _discoveries = new();

    #endregion

    #region Filter Properties

    [ObservableProperty] private string _nameSearch = "";
    partial void OnNameSearchChanged(string value) => ApplyFilter();

    [ObservableProperty] private string _hintSearch = "";
    partial void OnHintSearchChanged(string value) => ApplyFilter();

    [ObservableProperty] private bool _showOnlyWithHint;
    partial void OnShowOnlyWithHintChanged(bool value) => ApplyFilter();

    [ObservableProperty] private int _discoveryFilterIndex;
    partial void OnDiscoveryFilterIndexChanged(int value) => ApplyFilter();

    // 0: 전체, 1: 발견, 2: 미발견
    public List<string> DiscoveryFilterOptions { get; } = new() { "전체", "발견", "미발견" };

    #endregion

    #region Status

    [ObservableProperty] private string _statusText = "";

    #endregion

    public DiscoveryContentViewModel(
        DiscoveryService discoveryService,
        SaveDataService saveDataService,
        IEventAggregator eventAggregator)
    {
        _discoveryService = discoveryService;
        _saveDataService = saveDataService;
        _eventAggregator = eventAggregator;

        eventAggregator.GetEvent<SaveDataLoadedEvent>().Subscribe(OnSaveDataLoaded);

        // 슬롯 상태 ComboBox 변경 시 즉시 세이브 파일에 기록
        DiscoveryDisplayItem.OnSlotStateChanged = _saveDataService.SaveDiscoveryState;

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
            .Select(h => h.Index - 1)
            .ToHashSet();

        _hasHintIds = _saveDataService.CurrentSaveGameInfo.Hints
            .Where(h => h.HasHint)
            .Select(h => h.Index - 1)
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

            UpdateHintStatus();

            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusText = $"로드 실패: {ex.Message}";
        }
    }

    private void ApplyFilter()
    {
        var filtered = _allDiscoveries.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(NameSearch))
            filtered = filtered.Where(d => d.Name.Contains(NameSearch, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(HintSearch))
            filtered = filtered.Where(d => d.Hint?.Name?.Contains(HintSearch, StringComparison.OrdinalIgnoreCase) == true);

        if (ShowOnlyWithHint)
            filtered = filtered.Where(d => d.HintId != null);

        if (DiscoveryFilterIndex == 1)
            filtered = filtered.Where(d => d.HintId.HasValue && _discoveredHintIds?.Contains(d.HintId.Value) == true);
        else if (DiscoveryFilterIndex == 2)
            filtered = filtered.Where(d => !d.HintId.HasValue || _discoveredHintIds?.Contains(d.HintId.Value) != true);

        var slotStateMap = _saveDataService.CurrentSaveGameInfo?.Discoveries
            .ToDictionary(x => x.Id, x => x.State);

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
                CoordinateDisplay = FormatCoordinate(d.LatFrom, d.LatTo, d.LonFrom, d.LonTo),
                LatFrom = d.LatFrom,
                LatTo = d.LatTo,
                LonFrom = d.LonFrom,
                LonTo = d.LonTo,
                IsHintObtained = d.HintId.HasValue && _hasHintIds?.Contains(d.HintId.Value) == true,
                IsDiscoveryFound = d.HintId.HasValue && _discoveredHintIds?.Contains(d.HintId.Value) == true
            };
            item.SetCheckedWithoutSave(AppSettings.IsDiscoveryChecked(d.Id));
            if (slotStateMap != null && slotStateMap.TryGetValue(d.Id, out var stateByte))
                item.SetSlotStateWithoutSave(stateByte);
            return item;
        }).ToList();

        var withHint = displayItems.Where(d => d.HintId.HasValue).Take(5).ToList();
        foreach (var d in withHint)
            System.Diagnostics.Debug.WriteLine($"[Discovery] {d.Name} HintId={d.HintId}, IsHintObtained={d.IsHintObtained}, IsDiscoveryFound={d.IsDiscoveryFound}");

        Discoveries = new ObservableCollection<DiscoveryDisplayItem>(displayItems);

        var totalCount = _allDiscoveries.Count;
        var totalFound = _allDiscoveries.Count(d =>
            d.HintId.HasValue && _discoveredHintIds?.Contains(d.HintId.Value) == true);
        var percent = totalCount == 0 ? 0 : (double)totalFound / totalCount * 100;
        StatusText = $"발견: {totalFound} / {totalCount} ({percent:F1}%)   |   표시: {displayItems.Count}개";
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

    public static string FormatCoordinatePublic(int? latFrom, int? latTo, int? lonFrom, int? lonTo)
        => FormatCoordinate(latFrom, latTo, lonFrom, lonTo);

    private static string FormatCoordinate(int? latFrom, int? latTo, int? lonFrom, int? lonTo)
    {
        if (latFrom == null && lonFrom == null) return "";

        var lat = FormatRange(latFrom, latTo, "N", "S");
        var lon = FormatRange(lonFrom, lonTo, "E", "W");

        if (!string.IsNullOrEmpty(lat) && !string.IsNullOrEmpty(lon))
            return $"{lat}, {lon}";
        return lat + lon;
    }

    private static string FormatRange(int? from, int? to, string positive, string negative)
    {
        if (from == null) return "";

        string Format(int v) => v >= 0 ? $"{positive}{v}" : $"{negative}{Math.Abs(v)}";

        if (to == null || from == to)
            return Format(from.Value);

        return $"{Format(from.Value)}~{Format(to.Value)}";
    }

    [RelayCommand]
    private void ResetFilter()
    {
        NameSearch = "";
        HintSearch = "";
        ShowOnlyWithHint = false;
        DiscoveryFilterIndex = 0;
    }
}

public partial class DiscoveryDisplayItem : ObservableObject
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? HintId { get; set; }
    public string HintName { get; set; } = "";
    public string AppearCondition { get; set; } = "";
    public string BookName { get; set; } = "";
    public string ParentNames { get; set; } = "";
    private string _coordinateDisplay = "";
    public string CoordinateDisplay
    {
        get => _coordinateDisplay;
        set => SetProperty(ref _coordinateDisplay, value);
    }
    public int? LatFrom { get; set; }
    public int? LatTo { get; set; }
    public int? LonFrom { get; set; }
    public int? LonTo { get; set; }

    public bool IsHintObtained { get; set; }

    public bool IsDiscoveryFound { get; set; }

    public string DiscoveryStatusDisplay => IsDiscoveryFound ? "O" : "";

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (SetProperty(ref _isChecked, value))
                AppSettings.SetDiscoveryChecked(Id, value);
        }
    }

    public void SetCheckedWithoutSave(bool value)
    {
        _isChecked = value;
    }

    // 슬롯 상태 편집 — 선택 시 세이브 파일에 즉시 반영
    public const string StateUndiscovered = "미발견";
    public const string StateFound = "발견";
    public const string StateAnnounced = "발표";
    public static IReadOnlyList<string> SlotStateOptions { get; } = new[]
    {
        StateUndiscovered, StateFound, StateAnnounced
    };

    // (slotIndex, newStateByte) → 세이브 파일에 저장
    public static Action<int, byte>? OnSlotStateChanged;

    // 현재 슬롯 state 바이트 (하위 6비트 = 카테고리 base, 변경 시 보존)
    public byte CurrentStateByte { get; set; }

    private string _slotState = StateUndiscovered;
    public string SlotState
    {
        get => _slotState;
        set
        {
            if (!SetProperty(ref _slotState, value)) return;
            byte baseBits = (byte)(CurrentStateByte & 0x3F);
            byte newByte = value switch
            {
                StateAnnounced => (byte)(baseBits | 0xC0),
                StateFound => (byte)(baseBits | 0x40),
                _ => baseBits
            };
            CurrentStateByte = newByte;
            OnSlotStateChanged?.Invoke(Id, newByte);
        }
    }

    public void SetSlotStateWithoutSave(byte stateByte)
    {
        CurrentStateByte = stateByte;
        bool announced = (stateByte & 0x80) != 0;
        bool found = (stateByte & 0x40) != 0;
        _slotState = announced ? StateAnnounced : (found ? StateFound : StateUndiscovered);
        OnPropertyChanged(nameof(SlotState));
    }
}
