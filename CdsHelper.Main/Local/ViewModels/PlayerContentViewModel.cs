using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using Prism.Commands;
using Prism.Mvvm;

namespace CdsHelper.Main.Local.ViewModels;

public class PlayerContentViewModel : BindableBase
{
    private readonly SaveDataService _saveDataService;

    // 스킬 인덱스 -> 스킬명 매핑
    private static readonly Dictionary<int, string> SkillIndexToName = new()
    {
        { 1, "항해술" }, { 2, "운용술" }, { 3, "검술" }, { 4, "포술" }, { 5, "사격술" },
        { 6, "의학" }, { 7, "웅변술" }, { 8, "측량술" }, { 9, "역사학" }, { 10, "회계" },
        { 11, "조선술" }, { 12, "신학" }, { 13, "과학" },
        { 14, "스페인어" }, { 15, "포르투갈어" }, { 16, "로망스어" }, { 17, "게르만어" },
        { 18, "슬라브어" }, { 19, "아랍어" }, { 20, "페르시아어" }, { 21, "중국어" },
        { 22, "힌두어" }, { 23, "위그르어" }, { 24, "아프리카어" }, { 25, "아메리카어" },
        { 26, "동남아시아어" }, { 27, "동아시아어" }
    };

    private PlayerData? _player;
    public PlayerData? Player
    {
        get => _player;
        set => SetProperty(ref _player, value);
    }

    // 선택된 동료 정보
    private CharacterData? _selectedCrewMember;
    public CharacterData? SelectedCrewMember
    {
        get => _selectedCrewMember;
        set => SetProperty(ref _selectedCrewMember, value);
    }

    // 통합 스킬 목록
    private ObservableCollection<SkillDisplayItem> _combinedSkills = new();
    public ObservableCollection<SkillDisplayItem> CombinedSkills
    {
        get => _combinedSkills;
        set => SetProperty(ref _combinedSkills, value);
    }

    // 통합 언어 목록
    private ObservableCollection<SkillDisplayItem> _combinedLanguages = new();
    public ObservableCollection<SkillDisplayItem> CombinedLanguages
    {
        get => _combinedLanguages;
        set => SetProperty(ref _combinedLanguages, value);
    }

    private string _statusText = "세이브 파일을 로드하세요";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private string _filePath = "";
    public string FilePath
    {
        get => _filePath;
        set
        {
            if (SetProperty(ref _filePath, value))
            {
                RefreshCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public ICommand LoadSaveCommand { get; }
    public DelegateCommand RefreshCommand { get; }
    public ICommand ShowCrewMemberCommand { get; }

    public PlayerContentViewModel(SaveDataService saveDataService)
    {
        _saveDataService = saveDataService;
        LoadSaveCommand = new DelegateCommand(LoadSaveFile);
        RefreshCommand = new DelegateCommand(RefreshSaveFile, CanRefresh);
        ShowCrewMemberCommand = new DelegateCommand<string>(ShowCrewMember);

        // 기본 세이브 파일 로드 시도 (지연)
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            var defaultPath = @"C:\Users\ocean\Desktop\대항해시대3\savedata.cds";
            if (System.IO.File.Exists(defaultPath))
                LoadSaveFile(defaultPath);
        }), DispatcherPriority.Background);
    }

    public void LoadSaveFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "세이브 파일 (SAVEDATA.CDS)|SAVEDATA.CDS",
            Title = "세이브 파일 선택"
        };

        if (dialog.ShowDialog() == true)
        {
            LoadSaveFile(dialog.FileName);
        }
    }

    private bool CanRefresh() => !string.IsNullOrEmpty(FilePath);

    private void RefreshSaveFile()
    {
        if (!string.IsNullOrEmpty(FilePath))
        {
            LoadSaveFile(FilePath);
            StatusText = $"새로고침 완료: {Player?.FullName}";
        }
    }

    public void LoadSaveFile(string filePath)
    {
        try
        {
            StatusText = "로딩 중...";

            Player = _saveDataService.ReadPlayerData(filePath);
            FilePath = filePath;
            SelectedCrewMember = null;

            if (Player != null)
            {
                BuildCombinedSkills();
                StatusText = $"로드 완료: {Player.FullName}";
            }
            else
            {
                StatusText = "플레이어 데이터를 읽을 수 없습니다";
            }
        }
        catch (Exception ex)
        {
            StatusText = "로드 실패";
            System.Windows.MessageBox.Show($"파일 읽기 실패:\n\n{ex.Message}",
                "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void ShowCrewMember(string role)
    {
        if (Player == null) return;

        SelectedCrewMember = role switch
        {
            "Adjutant" => Player.AdjutantData,
            "Navigator" => Player.NavigatorData,
            "Surveyor" => Player.SurveyorData,
            "Interpreter" => Player.InterpreterData,
            _ => null
        };
    }

    private void BuildCombinedSkills()
    {
        if (Player == null) return;

        var skills = new ObservableCollection<SkillDisplayItem>();
        var languages = new ObservableCollection<SkillDisplayItem>();

        // 기능 스킬 (1-13)
        for (int i = 1; i <= 13; i++)
        {
            if (!SkillIndexToName.TryGetValue(i, out var skillName)) continue;

            var item = new SkillDisplayItem
            {
                Name = skillName,
                PlayerLevel = GetPlayerSkillLevel(skillName),
                AdjutantLevel = GetCrewSkillLevel(Player.AdjutantData, i),
                NavigatorLevel = GetCrewSkillLevel(Player.NavigatorData, i),
                SurveyorLevel = GetCrewSkillLevel(Player.SurveyorData, i),
                InterpreterLevel = GetCrewSkillLevel(Player.InterpreterData, i)
            };
            skills.Add(item);
        }

        // 언어 스킬 (14-27)
        for (int i = 14; i <= 27; i++)
        {
            if (!SkillIndexToName.TryGetValue(i, out var skillName)) continue;

            var item = new SkillDisplayItem
            {
                Name = skillName,
                PlayerLevel = GetPlayerLanguageLevel(skillName),
                AdjutantLevel = GetCrewSkillLevel(Player.AdjutantData, i),
                NavigatorLevel = GetCrewSkillLevel(Player.NavigatorData, i),
                SurveyorLevel = GetCrewSkillLevel(Player.SurveyorData, i),
                InterpreterLevel = GetCrewSkillLevel(Player.InterpreterData, i)
            };
            languages.Add(item);
        }

        CombinedSkills = skills;
        CombinedLanguages = languages;
    }

    private byte GetPlayerSkillLevel(string skillName)
    {
        if (Player?.Skills.TryGetValue(skillName, out var level) == true)
            return level;
        return 0;
    }

    private byte GetPlayerLanguageLevel(string skillName)
    {
        if (Player?.Languages.TryGetValue(skillName, out var level) == true)
            return level;
        return 0;
    }

    private byte GetCrewSkillLevel(CharacterData? crew, int skillIndex)
    {
        if (crew?.RawSkills.TryGetValue(skillIndex, out var level) == true)
            return level;
        return 0;
    }
}
