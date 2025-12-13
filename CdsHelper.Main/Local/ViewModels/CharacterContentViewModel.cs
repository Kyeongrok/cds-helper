using System.Collections.ObjectModel;
using System.Windows.Input;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using Prism.Commands;
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

    public ICommand LoadSaveCommand { get; }

    #endregion

    public CharacterContentViewModel(
        CharacterService characterService,
        SaveDataService saveDataService)
    {
        _characterService = characterService;
        _saveDataService = saveDataService;

        LoadSaveCommand = new DelegateCommand(LoadSaveFile);

        // 기본 세이브 파일 로드
        var savePath = @"C:\Users\ocean\Desktop\대항해시대3\savedata.cds";
        if (System.IO.File.Exists(savePath))
            LoadSaveFile(savePath);
    }

    public void LoadSaveFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "세이브 파일 (*.CDS)|*.CDS|모든 파일 (*.*)|*.*",
            Title = "세이브 파일 선택"
        };

        if (dialog.ShowDialog() == true)
        {
            LoadSaveFile(dialog.FileName);
        }
    }

    public void LoadSaveFile(string filePath)
    {
        try
        {
            StatusText = "파일 읽는 중...";
            _saveGameInfo = _saveDataService.ReadSaveFile(filePath);
            _playerData = _saveDataService.ReadPlayerData(filePath);
            _allCharacters = _saveGameInfo.Characters;

            // 플레이어 명성을 각 캐릭터에 설정 (고용 가능 여부 판단용)
            PlayerFame = _playerData?.Fame ?? 0;
            foreach (var character in _allCharacters)
            {
                character.PlayerFame = PlayerFame;
            }

            FilePath = $"파일 경로: {filePath}";
            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusText = "로드 실패";
            System.Windows.MessageBox.Show($"파일 읽기 실패:\n\n{ex.Message}",
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

        Characters = new ObservableCollection<CharacterData>(filtered);

        var grayCount = _allCharacters.Count(c => c.IsGray);
        var normalCount = _allCharacters.Count - grayCount;

        StatusText = ShowGrayCharacters
            ? $"로드 완료: {_allCharacters.Count}개 (등장: {normalCount}, 미등장: {grayCount})"
            : $"로드 완료: {normalCount}개 (미등장 {grayCount}개 숨김)";
    }
}
