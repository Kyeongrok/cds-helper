using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using CdsHelper.Support.Local.Events;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;

namespace CdsHelper.Main.Local.ViewModels;

public class PlayerContentViewModel : BindableBase
{
    private readonly SaveDataService _saveDataService;
    private readonly HintService _hintService;

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

    // 전체 캐릭터 목록 (시뮬레이션용)
    private List<CharacterData> _allCharacters = new();
    private ObservableCollection<CharacterData> _availableCharacters = new();
    public ObservableCollection<CharacterData> AvailableCharacters
    {
        get => _availableCharacters;
        set => SetProperty(ref _availableCharacters, value);
    }

    // 시뮬레이션 모드
    private bool _isSimulationMode;
    public bool IsSimulationMode
    {
        get => _isSimulationMode;
        set
        {
            if (SetProperty(ref _isSimulationMode, value))
                BuildCombinedSkills();
        }
    }

    // 시뮬레이션용 선택된 캐릭터
    private CharacterData? _simAdjutant;
    public CharacterData? SimAdjutant
    {
        get => _simAdjutant;
        set { if (SetProperty(ref _simAdjutant, value)) BuildCombinedSkills(); }
    }

    private CharacterData? _simNavigator;
    public CharacterData? SimNavigator
    {
        get => _simNavigator;
        set { if (SetProperty(ref _simNavigator, value)) BuildCombinedSkills(); }
    }

    private CharacterData? _simSurveyor;
    public CharacterData? SimSurveyor
    {
        get => _simSurveyor;
        set { if (SetProperty(ref _simSurveyor, value)) BuildCombinedSkills(); }
    }

    private CharacterData? _simInterpreter;
    public CharacterData? SimInterpreter
    {
        get => _simInterpreter;
        set { if (SetProperty(ref _simInterpreter, value)) BuildCombinedSkills(); }
    }

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

    // 힌트 목록
    private ObservableCollection<HintData> _hints = new();
    public ObservableCollection<HintData> Hints
    {
        get => _hints;
        set => SetProperty(ref _hints, value);
    }

    // 힌트 요약
    private string _hintSummary = "";
    public string HintSummary
    {
        get => _hintSummary;
        set => SetProperty(ref _hintSummary, value);
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
        set => SetProperty(ref _filePath, value);
    }

    // LoadSaveCommand와 RefreshCommand는 CdsHelperWindow의 공통 영역에서 처리
    public ICommand ShowCrewMemberCommand { get; }

    public PlayerContentViewModel(
        SaveDataService saveDataService,
        HintService hintService,
        IEventAggregator eventAggregator)
    {
        _saveDataService = saveDataService;
        _hintService = hintService;
        ShowCrewMemberCommand = new DelegateCommand<string>(ShowCrewMember);

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
            StatusText = "플레이어 데이터 로드 중...";

            Player = playerData;
            SelectedCrewMember = null;

            // 전체 캐릭터 목록 로드 (시뮬레이션용)
            _allCharacters = saveGameInfo.Characters
                .Where(c => !c.IsGray) // 등장한 캐릭터만
                .OrderBy(c => c.Name)
                .ToList();

            // 플레이어 명성 설정
            if (Player != null)
            {
                foreach (var c in _allCharacters)
                    c.PlayerFame = Player.Fame;
            }

            // 고용 가능한 캐릭터만 (HireStatus == 2, 함대소속 아님)
            var hirableCharacters = _allCharacters
                .Where(c => c.HireStatus == 2 && c.Location != "함대소속")
                .ToList();
            AvailableCharacters = new ObservableCollection<CharacterData>(hirableCharacters);

            // 시뮬레이션 초기값을 현재 동료로 설정
            SimAdjutant = _allCharacters.FirstOrDefault(c => c.Name == Player?.AdjutantName);
            SimNavigator = _allCharacters.FirstOrDefault(c => c.Name == Player?.NavigatorName);
            SimSurveyor = _allCharacters.FirstOrDefault(c => c.Name == Player?.SurveyorName);
            SimInterpreter = _allCharacters.FirstOrDefault(c => c.Name == Player?.InterpreterName);

            // 힌트 데이터 로드 (이름 설정)
            foreach (var hint in saveGameInfo.Hints)
            {
                hint.Name = _hintService.GetHintName(hint.Index - 1); // 0부터 시작하는 인덱스로 변환
            }
            Hints = new ObservableCollection<HintData>(saveGameInfo.Hints);
            HintSummary = $"발견: {saveGameInfo.DiscoveredHintCount} / {saveGameInfo.TotalHintCount}";

            if (Player != null)
            {
                BuildCombinedSkills();
                StatusText = $"플레이어 로드 완료: {Player.FullName} (고용가능: {hirableCharacters.Count}명)";
            }
            else
            {
                StatusText = "플레이어 데이터를 읽을 수 없습니다";
            }
        }
        catch (Exception ex)
        {
            StatusText = "로드 실패";
            System.Windows.MessageBox.Show($"플레이어 데이터 로드 실패:\n\n{ex.Message}",
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

        // 시뮬레이션 모드일 때 사용할 캐릭터
        var adjutant = IsSimulationMode ? SimAdjutant : Player.AdjutantData;
        var navigator = IsSimulationMode ? SimNavigator : Player.NavigatorData;
        var surveyor = IsSimulationMode ? SimSurveyor : Player.SurveyorData;
        var interpreter = IsSimulationMode ? SimInterpreter : Player.InterpreterData;

        // 기능 스킬 (1-13)
        for (int i = 1; i <= 13; i++)
        {
            if (!SkillIndexToName.TryGetValue(i, out var skillName)) continue;

            var item = new SkillDisplayItem
            {
                Name = skillName,
                PlayerLevel = GetPlayerSkillLevel(skillName),
                AdjutantLevel = GetCrewSkillLevel(adjutant, i),
                NavigatorLevel = GetCrewSkillLevel(navigator, i),
                SurveyorLevel = GetCrewSkillLevel(surveyor, i),
                InterpreterLevel = GetCrewSkillLevel(interpreter, i)
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
                AdjutantLevel = GetCrewSkillLevel(adjutant, i),
                NavigatorLevel = GetCrewSkillLevel(navigator, i),
                SurveyorLevel = GetCrewSkillLevel(surveyor, i),
                InterpreterLevel = GetCrewSkillLevel(interpreter, i)
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
