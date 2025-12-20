using System.Collections.ObjectModel;
using System.Windows.Input;
using CdsHelper.Support.Local.Events;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;

namespace CdsHelper.Main.Local.ViewModels;

public class CharacterContentViewModel : BindableBase
{
    private readonly CharacterService _characterService;
    private readonly SaveDataService _saveDataService;

    private List<CharacterData> _allCharacters = new();
    private SaveGameInfo? _saveGameInfo;
    private PlayerData? _playerData;

    #region Collections

    private ObservableCollection<CharacterData> _characters = new();
    public ObservableCollection<CharacterData> Characters
    {
        get => _characters;
        set => SetProperty(ref _characters, value);
    }

    #endregion

    #region Filter Properties

    private bool _showGrayCharacters;
    public bool ShowGrayCharacters
    {
        get => _showGrayCharacters;
        set { SetProperty(ref _showGrayCharacters, value); ApplyFilter(); }
    }

    private bool _showOnlyHirable = true;  // 기본값 true
    public bool ShowOnlyHirable
    {
        get => _showOnlyHirable;
        set { SetProperty(ref _showOnlyHirable, value); ApplyFilter(); }
    }

    private string _characterNameSearch = "";
    public string CharacterNameSearch
    {
        get => _characterNameSearch;
        set { SetProperty(ref _characterNameSearch, value); ApplyFilter(); }
    }

    private bool _filterBySkill;
    public bool FilterBySkill
    {
        get => _filterBySkill;
        set { SetProperty(ref _filterBySkill, value); ApplyFilter(); }
    }

    private int _selectedSkillIndex = 0;
    public int SelectedSkillIndex
    {
        get => _selectedSkillIndex;
        set { SetProperty(ref _selectedSkillIndex, value); if (FilterBySkill) ApplyFilter(); }
    }

    private byte _selectedSkillLevel = 3;
    public byte SelectedSkillLevel
    {
        get => _selectedSkillLevel;
        set { SetProperty(ref _selectedSkillLevel, value); if (FilterBySkill) ApplyFilter(); }
    }

    public List<string> SkillNames { get; } = CharacterService.SkillNames.Values.ToList();
    public List<byte> SkillLevels { get; } = new() { 1, 2, 3, 4, 5 };

    #endregion

    #region Status Properties

    private string _statusText = "준비됨";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private ushort _playerFame;
    public ushort PlayerFame
    {
        get => _playerFame;
        set => SetProperty(ref _playerFame, value);
    }

    private string _filePath = "파일 경로: 없음";
    public string FilePath
    {
        get => _filePath;
        set => SetProperty(ref _filePath, value);
    }

    #endregion

    #region Commands

    // LoadSaveCommand는 CdsHelperWindow의 공통 영역에서 처리

    #endregion

    public CharacterContentViewModel(
        CharacterService characterService,
        SaveDataService saveDataService,
        IEventAggregator eventAggregator)
    {
        _characterService = characterService;
        _saveDataService = saveDataService;

        // 세이브 데이터 로드 이벤트 구독
        eventAggregator.GetEvent<SaveDataLoadedEvent>().Subscribe(OnSaveDataLoaded);

        // 이미 로드된 데이터가 있으면 표시
        if (saveDataService.CurrentSaveGameInfo != null && saveDataService.CurrentPlayerData != null)
        {
            LoadSaveData(saveDataService.CurrentSaveGameInfo, saveDataService.CurrentPlayerData);
        }
    }

    private void OnSaveDataLoaded(SaveDataLoadedEventArgs args)
    {
        if (args.PlayerData != null)
        {
            LoadSaveData(args.SaveGameInfo, args.PlayerData);
        }
    }

    // 중앙에서 세이브 데이터 로드 시 호출될 메서드
    public void LoadSaveData(SaveGameInfo saveGameInfo, PlayerData playerData)
    {
        try
        {
            StatusText = "캐릭터 데이터 로드 중...";
            _saveGameInfo = saveGameInfo;
            _playerData = playerData;
            _allCharacters = _saveGameInfo.Characters;

            // 플레이어 명성을 각 캐릭터에 설정 (고용 가능 여부 판단용)
            PlayerFame = _playerData?.Fame ?? 0;
            foreach (var character in _allCharacters)
            {
                character.PlayerFame = PlayerFame;
            }

            ApplyFilter();
            StatusText = $"캐릭터 로드 완료: {Characters.Count}명";
        }
        catch (Exception ex)
        {
            StatusText = "로드 실패";
            System.Windows.MessageBox.Show($"캐릭터 데이터 로드 실패:\n\n{ex.Message}",
                "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void ApplyFilter()
    {
        if (_allCharacters.Count == 0) return;

        var filtered = _characterService.Filter(
            _allCharacters,
            ShowGrayCharacters,
            string.IsNullOrWhiteSpace(CharacterNameSearch) ? null : CharacterNameSearch,
            FilterBySkill ? SelectedSkillIndex + 1 : null,
            FilterBySkill ? SelectedSkillLevel : null);

        // 고용 가능 필터: HireStatus == 2 && Location != "함대소속"
        if (ShowOnlyHirable)
        {
            filtered = filtered.Where(c => c.HireStatus == 2 && c.Location != "함대소속").ToList();
        }

        Characters = new ObservableCollection<CharacterData>(filtered);

        var grayCount = _allCharacters.Count(c => c.IsGray);
        var normalCount = _allCharacters.Count - grayCount;
        var hirableCount = _allCharacters.Count(c => c.HireStatus == 2 && c.Location != "함대소속");

        StatusText = $"표시: {filtered.Count}명 (고용가능: {hirableCount}명)";
    }
}
