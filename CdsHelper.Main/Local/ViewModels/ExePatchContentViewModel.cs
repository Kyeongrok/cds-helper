using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using CdsHelper.Support.Local.Settings;
using Microsoft.Win32;
using Prism.Commands;
using Prism.Mvvm;

namespace CdsHelper.Main.Local.ViewModels;

public class HireStatusOption
{
    public int Value { get; set; }
    public string Display { get; set; } = "";

    public static List<HireStatusOption> Options { get; } = new()
    {
        new() { Value = 1, Display = "대화" },
        new() { Value = 2, Display = "고용" }
    };
}

/// <summary>
/// 사용자가 직접 정의하는 헥스 패치 한 줄. (.cds 파일로 저장/불러오기)
/// 주소(hex) + 바이트 수 + 허용 범위(min~max) + 현재 값.
/// </summary>
public class CustomPatchItem : BindableBase
{
    public static IReadOnlyList<int> ByteSizeOptions { get; } = new[] { 1, 2, 4 };

    private string _name = "";
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    private string _description = "";
    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    /// <summary>
    /// 같은 값을 동시에 기록할 주소들(콤마/공백/줄바꿈 구분). 단일 주소도 여기에 보관.
    /// 예: "0x5FECB, 0x5FED4, 0x5FF61"
    /// </summary>
    private string _addressesText = "";
    public string AddressesText
    {
        get => _addressesText;
        set
        {
            if (SetProperty(ref _addressesText, value))
            {
                RaisePropertyChanged(nameof(AddressSummary));
                RaisePropertyChanged(nameof(AddressCount));
                RaisePropertyChanged(nameof(AddressHex));
                OnDefinitionChanged?.Invoke(this);
            }
        }
    }

    /// <summary>하위호환용: 첫 주소(단일 주소처럼 다룰 때).</summary>
    public string AddressHex => AddressList.Count > 0 ? AddressList[0] : "";

    /// <summary>파싱된 주소 문자열 목록(원본 표기 유지).</summary>
    public IReadOnlyList<string> AddressList => SplitAddresses(_addressesText);
    public int AddressCount => AddressList.Count;

    /// <summary>목록에 보여줄 주소 요약. 여러 곳이면 "0x... 외 N곳".</summary>
    public string AddressSummary => AddressCount <= 1
        ? (AddressCount == 1 ? AddressList[0] : "")
        : $"{AddressList[0]} 외 {AddressCount - 1}곳";

    public static IReadOnlyList<string> SplitAddresses(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        return text.Split(new[] { ',', ' ', '\t', '\r', '\n', ';' },
            StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>바이트 수별 최대값 (부호 없는 정수 기준).</summary>
    public static long MaxForByteSize(int byteSize) => byteSize switch
    {
        1 => 255,
        2 => 65535,
        _ => 4294967295L,
    };

    private int _byteSize = 1;
    public int ByteSize
    {
        get => _byteSize;
        set
        {
            if (SetProperty(ref _byteSize, value))
            {
                // 바이트 수에 따라 허용 범위 자동 결정
                MinValue = 0;
                MaxValue = MaxForByteSize(value);
                OnDefinitionChanged?.Invoke(this);
            }
        }
    }

    private long _minValue;
    public long MinValue
    {
        get => _minValue;
        set => SetProperty(ref _minValue, value);
    }

    private long _maxValue = 255;
    public long MaxValue
    {
        get => _maxValue;
        set => SetProperty(ref _maxValue, value);
    }

    private long _value;
    public long Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value))
                OnValueChanged?.Invoke(this);
        }
    }

    // ===== 패치 종류 =====
    /// <summary>패치 종류: "number"(값 입력) | "toggle"(적용/해제). 직렬화 기준 필드.</summary>
    private string _patchType = "number";
    public string PatchType
    {
        get => _patchType;
        set
        {
            var v = string.Equals(value, "toggle", StringComparison.OrdinalIgnoreCase) ? "toggle" : "number";
            if (SetProperty(ref _patchType, v))
            {
                RaisePropertyChanged(nameof(IsToggle));
                RaisePropertyChanged(nameof(IsValueMode));
                OnDefinitionChanged?.Invoke(this);
            }
        }
    }

    /// <summary>토글 종류 여부(UI 바인딩·분기용 접근자). set 시 PatchType을 바꾼다.</summary>
    public bool IsToggle
    {
        get => _patchType == "toggle";
        set => PatchType = value ? "toggle" : "number";
    }

    /// <summary>값 입력 종류 여부(값 입력칸 표시용).</summary>
    public bool IsValueMode => _patchType != "toggle";

    /// <summary>토글 OFF(해제)일 때 기록할 원본 값.</summary>
    private long _originalValue;
    public long OriginalValue
    {
        get => _originalValue;
        set => SetProperty(ref _originalValue, value);
    }

    /// <summary>토글 ON(적용)일 때 기록할 패치 값.</summary>
    private long _patchedValue;
    public long PatchedValue
    {
        get => _patchedValue;
        set => SetProperty(ref _patchedValue, value);
    }

    /// <summary>현재 적용 상태(토글). true=적용값 기록됨.</summary>
    private bool _isApplied;
    public bool IsApplied
    {
        get => _isApplied;
        set
        {
            if (SetProperty(ref _isApplied, value))
                OnToggleChanged?.Invoke(this);
        }
    }

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    /// <summary>내보내기 대상 선택용 체크박스 (저장/직렬화 대상 아님).</summary>
    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }

    /// <summary>값이 사용자 입력으로 바뀌면 EXE에 기록하기 위한 콜백.</summary>
    public Action<CustomPatchItem>? OnValueChanged { get; set; }

    /// <summary>토글 적용 상태가 바뀌면 EXE에 기록하기 위한 콜백.</summary>
    public Action<CustomPatchItem>? OnToggleChanged { get; set; }

    /// <summary>주소/바이트 수가 바뀌면 현재 값을 다시 읽기 위한 콜백.</summary>
    public Action<CustomPatchItem>? OnDefinitionChanged { get; set; }

    /// <summary>EXE에 기록하지 않고 값만 갱신(현재 값 표시용).</summary>
    public void SetValueSilent(long v)
    {
        _value = v;
        RaisePropertyChanged(nameof(Value));
    }

    /// <summary>EXE에 기록하지 않고 적용 상태만 갱신(현재 상태 표시용).</summary>
    public void SetAppliedSilent(bool v)
    {
        _isApplied = v;
        RaisePropertyChanged(nameof(IsApplied));
    }
}

/// <summary>커스텀 패치 .cds 파일 직렬화용 DTO.</summary>
public class CustomPatchDto
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Address { get; set; } = "";          // 단일 주소(하위호환)
    public List<string>? Addresses { get; set; }        // 다중 주소(신규, 있으면 우선)
    public int ByteSize { get; set; } = 1;
    public long Min { get; set; }
    public long Max { get; set; }
    public long Value { get; set; }

    /// <summary>패치 종류: "number"(값 입력) | "toggle"(적용/해제). 기본 number.</summary>
    public string Type { get; set; } = "number";

    public long OriginalValue { get; set; }             // 토글 해제 시 값
    public long PatchedValue { get; set; }              // 토글 적용 시 값
}

public class Unko2CharacterItem : BindableBase
{
    public int Index { get; set; }
    public int PatchOffset { get; set; }  // 파일 내 실제 오프셋
    public string RecordAddress { get; set; } = "";
    public string PatchAddress { get; set; } = "";
    public string Name { get; set; } = "";
    public int AppearOffset { get; set; }  // 등장 지연값의 파일 내 오프셋

    internal string _appearYear = "";
    public string AppearYear
    {
        get => _appearYear;
        set
        {
            if (SetProperty(ref _appearYear, value))
            {
                OnAppearYearChanged?.Invoke(this);
            }
        }
    }

    public Action<Unko2CharacterItem>? OnAppearYearChanged { get; set; }

    public int AppearTypeOffset { get; set; }  // 등장조건(+0x08)의 파일 내 오프셋

    internal int _appearType;
    public int AppearType
    {
        get => _appearType;
        set
        {
            if (SetProperty(ref _appearType, value))
            {
                OnAppearTypeChanged?.Invoke(this);
            }
        }
    }

    public Action<Unko2CharacterItem>? OnAppearTypeChanged { get; set; }

    public int BirthYearOffset { get; set; }  // 출생연도(+0xAC)의 파일 내 오프셋

    internal int _birthYear;
    public int BirthYear
    {
        get => _birthYear;
        set
        {
            if (SetProperty(ref _birthYear, value))
            {
                OnBirthYearChanged?.Invoke(this);
            }
        }
    }

    public Action<Unko2CharacterItem>? OnBirthYearChanged { get; set; }

    public string Gender { get; set; } = "";
    public int Hp { get; set; }
    public int Intelligence { get; set; }
    public int Combat { get; set; }
    public int Charisma { get; set; }
    public int Luck { get; set; }
    public int Navigation { get; set; }
    public int Surveying { get; set; }

    private int _hireStatusValue;
    public int HireStatusValue
    {
        get => _hireStatusValue;
        set
        {
            if (SetProperty(ref _hireStatusValue, value))
            {
                OnHireStatusChanged?.Invoke(this);
            }
        }
    }

    public Action<Unko2CharacterItem>? OnHireStatusChanged { get; set; }

    public string Stats => $"{Intelligence}/{Combat}/{Charisma}/{Luck}/{Navigation}/{Surveying}";
}

public class ExePatchContentViewModel : BindableBase
{
    // PE 섹션 정보
    private List<(string Name, int VA, int Size, int RawOffset, int RawSize)> _sections = new();
    private int _imageBase = 0x400000;

    // 대항2 인물 레코드 상수
    private const int RecordSize = 0xCC;
    private const int DataStart = 0xE6198;
    private const int MaxRecords = 80;

    // 등장 조건 오프셋
    private const int AppearConditionOffset = 0x00030F6D;
    private const int AppearConditionOriginal = 191;   // 0xBF → 1671년
    private const int AppearConditionPatched = 201;    // 0xC9 → 1681년

    // 장기휴양 기간 제한 오프셋
    private const int LongRestLimitOffset = 0x05FB83;
    private const int LongRestLimitOriginal = 12;

    // 직업버튼 능력치 갱신 패치 오프셋
    private const int JobButtonPatchOffset = 0x0005CCDA;
    private const int JobButtonPatchLength = 31;
    private static readonly byte[] JobButtonOriginalBytes = new byte[]
    {
        0x8B, 0x45, 0xF0, 0x6A, 0x05, 0x83, 0xE8, 0x11, 0x89, 0x86, 0x50, 0x01, 0x00, 0x00, 0x8B, 0x0C,
        0x85, 0x18, 0x27, 0x55, 0x00, 0x51, 0xB9, 0x48, 0x0C, 0x58, 0x00, 0xE8, 0xA6, 0x07, 0xFB
    };
    private static readonly byte[] JobButtonPatchedBytes = new byte[]
    {
        0x89, 0x86, 0x50, 0x01, 0x00, 0x00, 0xB9, 0x48, 0x0C, 0x58, 0x00, 0x6A, 0x05, 0xFF, 0x34, 0x85,
        0x18, 0x27, 0x55, 0x00, 0xE8, 0xAD, 0x07, 0xFB, 0xFF, 0x8B, 0xCE, 0xE8, 0x56, 0xFB, 0xFF
    };

    private ObservableCollection<Unko2CharacterItem> _characters = new();
    public ObservableCollection<Unko2CharacterItem> Characters
    {
        get => _characters;
        set => SetProperty(ref _characters, value);
    }

    private string _exeFilePath = "";
    public string ExeFilePath
    {
        get => _exeFilePath;
        set => SetProperty(ref _exeFilePath, value);
    }

    private string _statusText = "대기 중";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private int _totalCount;
    public int TotalCount
    {
        get => _totalCount;
        set => SetProperty(ref _totalCount, value);
    }

    private int _talkOnlyCount;
    public int TalkOnlyCount
    {
        get => _talkOnlyCount;
        set => SetProperty(ref _talkOnlyCount, value);
    }

    public string AppearConditionAddress => $"0x{AppearConditionOffset:X6}";
    public string AppearConditionOriginalDisplay => $"0x{AppearConditionOriginal:X2} ({AppearConditionOriginal})";
    public string AppearConditionPatchedDisplay => $"0x{AppearConditionPatched:X2} ({AppearConditionPatched})";

    private string _appearConditionCurrent = "";
    public string AppearConditionCurrent
    {
        get => _appearConditionCurrent;
        set => SetProperty(ref _appearConditionCurrent, value);
    }

    private int _longRestLimit;
    public int LongRestLimit
    {
        get => _longRestLimit;
        set => SetProperty(ref _longRestLimit, value);
    }

    private bool _isJobButtonPatched;
    public bool IsJobButtonPatched
    {
        get => _isJobButtonPatched;
        set => SetProperty(ref _isJobButtonPatched, value);
    }

    private string _jobButtonPatchStatus = "";
    public string JobButtonPatchStatus
    {
        get => _jobButtonPatchStatus;
        set => SetProperty(ref _jobButtonPatchStatus, value);
    }

    public string JobButtonPatchAddress => $"0x{JobButtonPatchOffset:X6}";

    // ===== 커스텀 패치 =====
    private ObservableCollection<CustomPatchItem> _customPatches = new();
    public ObservableCollection<CustomPatchItem> CustomPatches
    {
        get => _customPatches;
        set => SetProperty(ref _customPatches, value);
    }

    // 하나라도 체크되면 내보내기 버튼 표시
    private bool _hasAnyChecked;
    public bool HasAnyChecked
    {
        get => _hasAnyChecked;
        set => SetProperty(ref _hasAnyChecked, value);
    }

    private string _newPatchName = "";
    public string NewPatchName
    {
        get => _newPatchName;
        set => SetProperty(ref _newPatchName, value);
    }

    private string _newPatchDescription = "";
    public string NewPatchDescription
    {
        get => _newPatchDescription;
        set => SetProperty(ref _newPatchDescription, value);
    }

    private string _newPatchAddress = "";
    public string NewPatchAddress
    {
        get => _newPatchAddress;
        set => SetProperty(ref _newPatchAddress, value);
    }

    private int _newPatchByteSize = 1;
    public int NewPatchByteSize
    {
        get => _newPatchByteSize;
        set
        {
            if (SetProperty(ref _newPatchByteSize, value))
            {
                // 바이트 수에 따라 최소/최대 자동 결정
                NewPatchMin = 0;
                NewPatchMax = CustomPatchItem.MaxForByteSize(value);
            }
        }
    }

    private long _newPatchMin;
    public long NewPatchMin
    {
        get => _newPatchMin;
        set => SetProperty(ref _newPatchMin, value);
    }

    private long _newPatchMax = 255;
    public long NewPatchMax
    {
        get => _newPatchMax;
        set => SetProperty(ref _newPatchMax, value);
    }

    // 새 패치를 토글(적용/해제)로 등록할지 여부
    private bool _newPatchIsToggle;
    public bool NewPatchIsToggle
    {
        get => _newPatchIsToggle;
        set => SetProperty(ref _newPatchIsToggle, value);
    }

    // 토글 패치 등록 시 원본값(해제 시)
    private long _newPatchOriginalValue;
    public long NewPatchOriginalValue
    {
        get => _newPatchOriginalValue;
        set => SetProperty(ref _newPatchOriginalValue, value);
    }

    // 토글 패치 등록 시 적용값(적용 시)
    private long _newPatchPatchedValue;
    public long NewPatchPatchedValue
    {
        get => _newPatchPatchedValue;
        set => SetProperty(ref _newPatchPatchedValue, value);
    }

    // 커스텀 패치 자동 저장 파일 경로 (%APPDATA%\CdsHelper\custom_patches.json)
    private static string CustomPatchAutoSavePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CdsHelper",
        "custom_patches.json");

    private static readonly JsonSerializerOptions PatchJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // 일괄 로드/현재값 읽기 중에는 자동 저장을 막는다
    private bool _suppressAutoSave;

    public ICommand RefreshCommand { get; }
    public ICommand RestoreOriginalCommand { get; }
    public ICommand SaveAppearConditionCommand { get; }
    public ICommand RestoreAppearConditionCommand { get; }
    public ICommand SaveLongRestLimitCommand { get; }
    public ICommand RestoreLongRestLimitCommand { get; }
    public ICommand ApplyJobButtonPatchCommand { get; }
    public ICommand RestoreJobButtonPatchCommand { get; }
    public ICommand BrowseFileCommand { get; }
    public ICommand AddCustomPatchCommand { get; }
    public ICommand RemoveCustomPatchCommand { get; }
    public ICommand SaveCustomPatchesCommand { get; }
    public ICommand LoadCustomPatchesCommand { get; }
    public ICommand ExportSelectedCustomPatchesCommand { get; }

    public ExePatchContentViewModel()
    {
        // EUC-KR 인코딩 등록
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        RefreshCommand = new DelegateCommand(LoadExeData);
        RestoreOriginalCommand = new DelegateCommand(RestoreOriginal);
        SaveAppearConditionCommand = new DelegateCommand(SaveAppearCondition);
        RestoreAppearConditionCommand = new DelegateCommand(RestoreAppearCondition);
        SaveLongRestLimitCommand = new DelegateCommand(SaveLongRestLimit);
        RestoreLongRestLimitCommand = new DelegateCommand(RestoreLongRestLimit);
        ApplyJobButtonPatchCommand = new DelegateCommand(ApplyJobButtonPatch);
        RestoreJobButtonPatchCommand = new DelegateCommand(RestoreJobButtonPatch);
        BrowseFileCommand = new DelegateCommand(BrowseFile);
        AddCustomPatchCommand = new DelegateCommand(AddCustomPatch);
        RemoveCustomPatchCommand = new DelegateCommand<CustomPatchItem>(RemoveCustomPatch);
        SaveCustomPatchesCommand = new DelegateCommand(SaveCustomPatches);
        LoadCustomPatchesCommand = new DelegateCommand(LoadCustomPatches);
        ExportSelectedCustomPatchesCommand = new DelegateCommand(ExportSelectedCustomPatches);

        // 자동 저장된 커스텀 패치 목록 복원
        LoadAutoSavedCustomPatches();

        // 마지막 세이브 파일 경로에서 게임 폴더 추출
        var lastSavePath = AppSettings.LastSaveFilePath;
        if (!string.IsNullOrEmpty(lastSavePath))
        {
            var gameFolder = Path.GetDirectoryName(lastSavePath);
            if (!string.IsNullOrEmpty(gameFolder))
            {
                ExeFilePath = Path.Combine(gameFolder, "cds_95.exe");
            }
        }

        LoadExeData();
    }

    private void ParsePeHeaders(byte[] data)
    {
        _sections.Clear();

        int peOffset = BitConverter.ToInt32(data, 0x3C);
        if (data[peOffset] != 'P' || data[peOffset + 1] != 'E')
            return;

        int coffHeader = peOffset + 4;
        int numberOfSections = BitConverter.ToInt16(data, coffHeader + 2);
        int optionalHeaderSize = BitConverter.ToInt16(data, coffHeader + 16);

        int optionalHeader = coffHeader + 20;
        _imageBase = BitConverter.ToInt32(data, optionalHeader + 28);

        int sectionHeaderStart = optionalHeader + optionalHeaderSize;

        for (int i = 0; i < numberOfSections; i++)
        {
            int secOffset = sectionHeaderStart + (i * 40);
            string secName = Encoding.ASCII.GetString(data, secOffset, 8).TrimEnd('\0');
            int virtualSize = BitConverter.ToInt32(data, secOffset + 8);
            int virtualAddress = BitConverter.ToInt32(data, secOffset + 12);
            int rawDataSize = BitConverter.ToInt32(data, secOffset + 16);
            int rawDataPointer = BitConverter.ToInt32(data, secOffset + 20);

            _sections.Add((secName, virtualAddress, virtualSize, rawDataPointer, rawDataSize));
        }
    }

    private int VaToFileOffset(int va)
    {
        int rva = va - _imageBase;
        foreach (var sec in _sections)
        {
            if (rva >= sec.VA && rva < sec.VA + sec.Size)
            {
                return sec.RawOffset + (rva - sec.VA);
            }
        }
        return -1;
    }

    private string ReadNullTermString(byte[] data, int offset)
    {
        if (offset <= 0 || offset >= data.Length) return "";

        int len = 0;
        while (offset + len < data.Length && data[offset + len] != 0 && len < 30) len++;
        if (len == 0) return "";

        var eucKr = Encoding.GetEncoding(51949);
        byte[] nb = new byte[len];
        Array.Copy(data, offset, nb, 0, len);
        return eucKr.GetString(nb);
    }

    private void BrowseFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "EXE 파일 선택",
            Filter = "EXE 파일 (*.exe)|*.exe|모든 파일 (*.*)|*.*",
            FileName = "cds_95.exe"
        };

        if (!string.IsNullOrEmpty(ExeFilePath) && File.Exists(ExeFilePath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(ExeFilePath);
        }

        if (dialog.ShowDialog() == true)
        {
            ExeFilePath = dialog.FileName;
            LoadExeData();
        }
    }

    private void LoadExeData()
    {
        Characters.Clear();
        TotalCount = 0;
        TalkOnlyCount = 0;

        if (string.IsNullOrEmpty(ExeFilePath) || !File.Exists(ExeFilePath))
        {
            StatusText = $"파일을 찾을 수 없습니다: {ExeFilePath}";
            return;
        }

        try
        {
            var data = File.ReadAllBytes(ExeFilePath);

            // PE 헤더 파싱
            ParsePeHeaders(data);

            // 등장 조건, 장기휴양 로드
            var appearRaw = BitConverter.ToInt32(data, AppearConditionOffset);
            AppearConditionCurrent = $"0x{appearRaw:X2} ({appearRaw})";
            if (LongRestLimitOffset < data.Length)
                LongRestLimit = data[LongRestLimitOffset];

            // 직업버튼 패치 상태 확인
            CheckJobButtonPatchStatus(data);

            // 커스텀 패치 현재 값 갱신
            RefreshCustomPatchValues(data);

            for (int i = 0; i < MaxRecords; i++)
            {
                int recAddr = DataStart + (i * RecordSize);
                if (recAddr + RecordSize > data.Length) break;

                int delay = BitConverter.ToInt32(data, recAddr);
                int type = BitConverter.ToInt32(data, recAddr + 0x04);
                int appearType = BitConverter.ToInt32(data, recAddr + 0x08);
                int hp = BitConverter.ToInt32(data, recAddr + 0x0C);

                // 유효성 검사 (체력 50~255)
                if (hp < 50 || hp > 255) break;

                // 능력치 (4바이트씩)
                int stat1 = BitConverter.ToInt32(data, recAddr + 0x10); // 지력
                int stat2 = BitConverter.ToInt32(data, recAddr + 0x14); // 무력
                int stat3 = BitConverter.ToInt32(data, recAddr + 0x18); // 매력
                int stat4 = BitConverter.ToInt32(data, recAddr + 0x1C); // 운
                int stat5 = BitConverter.ToInt32(data, recAddr + 0x20); // 항해
                int stat6 = BitConverter.ToInt32(data, recAddr + 0x24); // 측량

                int birthOffset = BitConverter.ToInt32(data, recAddr + 0xAC);
                int birthYear = 1480 - birthOffset;  // 양수=나이→1480-나이, 음수=미래출생→1480+|값|

                int hireStatus = BitConverter.ToInt32(data, recAddr + 0xC8);

                // 이름 포인터 (+0x9C, +0xA0) - VA를 파일 오프셋으로 변환
                int namePtr1 = BitConverter.ToInt32(data, recAddr + 0x9C);
                int namePtr2 = BitConverter.ToInt32(data, recAddr + 0xA0);

                int nameOff1 = VaToFileOffset(namePtr1);
                int nameOff2 = VaToFileOffset(namePtr2);

                string name1 = ReadNullTermString(data, nameOff1);
                string name2 = ReadNullTermString(data, nameOff2);

                string fullName = string.IsNullOrEmpty(name2) ? name1 :
                                 string.IsNullOrEmpty(name1) ? name2 : $"{name1}·{name2}";

                string appearYear = delay == unchecked((int)0xFFFFFFFF) ? "이벤트" :
                                   (delay >= 0 && delay < 200) ? $"{1480 + delay}" : $"0x{delay:X}";

                string genderStr = type switch
                {
                    4 => "남",
                    5 => "여",
                    _ => $"{type}"
                };

                int patchAddr = recAddr + 0xC8;

                var item = new Unko2CharacterItem
                {
                    Index = i + 1,
                    PatchOffset = patchAddr,
                    AppearOffset = recAddr,  // delay는 레코드 시작(+0x00)
                    AppearTypeOffset = recAddr + 0x08,
                    BirthYearOffset = recAddr + 0xAC,
                    RecordAddress = $"0x{recAddr:X6}",
                    PatchAddress = $"0x{patchAddr:X6}",
                    Name = fullName,
                    _appearYear = appearYear,
                    _appearType = appearType,
                    _birthYear = birthYear,
                    Gender = genderStr,
                    Hp = hp,
                    Intelligence = stat1,
                    Combat = stat2,
                    Charisma = stat3,
                    Luck = stat4,
                    Navigation = stat5,
                    Surveying = stat6,
                    HireStatusValue = hireStatus,
                    OnHireStatusChanged = SaveHireStatus,
                    OnAppearYearChanged = SaveAppearYear,
                    OnAppearTypeChanged = SaveAppearType,
                    OnBirthYearChanged = SaveBirthYear
                };

                Characters.Add(item);

                if (hireStatus == 1)
                    TalkOnlyCount++;
            }

            TotalCount = Characters.Count;
            StatusText = $"로드 완료 - 총 {TotalCount}명, 대화만 가능: {TalkOnlyCount}명";
        }
        catch (Exception ex)
        {
            StatusText = $"오류: {ex.Message}";
        }
    }

    private void SaveHireStatus(Unko2CharacterItem item)
    {
        if (string.IsNullOrEmpty(ExeFilePath) || !File.Exists(ExeFilePath))
        {
            StatusText = "파일을 찾을 수 없습니다";
            return;
        }

        try
        {
            // 백업 생성 (최초 1회)
            var backupPath = ExeFilePath + ".bak";
            if (!File.Exists(backupPath))
            {
                File.Copy(ExeFilePath, backupPath);
            }

            using var fs = new FileStream(ExeFilePath, FileMode.Open, FileAccess.Write);
            fs.Seek(item.PatchOffset, SeekOrigin.Begin);
            fs.WriteByte((byte)item.HireStatusValue);

            // TalkOnlyCount 업데이트
            TalkOnlyCount = Characters.Count(c => c.HireStatusValue == 1);
            StatusText = $"{item.Name} → {(item.HireStatusValue == 2 ? "고용" : "대화")} 변경됨";
        }
        catch (Exception ex)
        {
            StatusText = $"저장 오류: {ex.Message}";
        }
    }

    private void SaveAppearYear(Unko2CharacterItem item)
    {
        if (string.IsNullOrEmpty(ExeFilePath) || !File.Exists(ExeFilePath))
        {
            StatusText = "파일을 찾을 수 없습니다";
            return;
        }

        int delay;
        string input = item.AppearYear.Trim();
        if (input == "이벤트" || input.Equals("event", StringComparison.OrdinalIgnoreCase))
        {
            delay = unchecked((int)0xFFFFFFFF);
        }
        else if (int.TryParse(input, out int year) && year >= 1480 && year <= 1680)
        {
            delay = year - 1480;
        }
        else
        {
            StatusText = $"잘못된 등장연도: {input} (1480~1680 또는 '이벤트')";
            return;
        }

        try
        {
            // 백업 생성 (최초 1회)
            var backupPath = ExeFilePath + ".bak";
            if (!File.Exists(backupPath))
            {
                File.Copy(ExeFilePath, backupPath);
            }

            using var fs = new FileStream(ExeFilePath, FileMode.Open, FileAccess.Write);
            fs.Seek(item.AppearOffset, SeekOrigin.Begin);
            fs.Write(BitConverter.GetBytes(delay), 0, 4);

            string displayYear = delay == unchecked((int)0xFFFFFFFF) ? "이벤트" : $"{1480 + delay}";
            StatusText = $"{item.Name} → 등장: {displayYear} 변경됨";
        }
        catch (Exception ex)
        {
            StatusText = $"저장 오류: {ex.Message}";
        }
    }

    private void SaveBirthYear(Unko2CharacterItem item)
    {
        if (string.IsNullOrEmpty(ExeFilePath) || !File.Exists(ExeFilePath))
        {
            StatusText = "파일을 찾을 수 없습니다";
            return;
        }

        try
        {
            var backupPath = ExeFilePath + ".bak";
            if (!File.Exists(backupPath))
            {
                File.Copy(ExeFilePath, backupPath);
            }

            int offsetValue = 1480 - item.BirthYear;  // 출생연도 → 오프셋 역변환

            using var fs = new FileStream(ExeFilePath, FileMode.Open, FileAccess.Write);
            fs.Seek(item.BirthYearOffset, SeekOrigin.Begin);
            fs.Write(BitConverter.GetBytes(offsetValue), 0, 4);

            StatusText = $"{item.Name} → 출생: {item.BirthYear}년 변경됨";
        }
        catch (Exception ex)
        {
            StatusText = $"저장 오류: {ex.Message}";
        }
    }

    private void SaveAppearType(Unko2CharacterItem item)
    {
        if (string.IsNullOrEmpty(ExeFilePath) || !File.Exists(ExeFilePath))
        {
            StatusText = "파일을 찾을 수 없습니다";
            return;
        }

        try
        {
            var backupPath = ExeFilePath + ".bak";
            if (!File.Exists(backupPath))
            {
                File.Copy(ExeFilePath, backupPath);
            }

            using var fs = new FileStream(ExeFilePath, FileMode.Open, FileAccess.Write);
            fs.Seek(item.AppearTypeOffset, SeekOrigin.Begin);
            fs.Write(BitConverter.GetBytes(item.AppearType), 0, 4);

            StatusText = $"{item.Name} → 등장조건: {item.AppearType} 변경됨";
        }
        catch (Exception ex)
        {
            StatusText = $"저장 오류: {ex.Message}";
        }
    }

    private void SaveAppearCondition()
    {
        if (string.IsNullOrEmpty(ExeFilePath) || !File.Exists(ExeFilePath)) { StatusText = "파일을 찾을 수 없습니다"; return; }
        try
        {
            var backupPath = ExeFilePath + ".bak";
            if (!File.Exists(backupPath)) File.Copy(ExeFilePath, backupPath);

            using var fs = new FileStream(ExeFilePath, FileMode.Open, FileAccess.Write);
            fs.Seek(AppearConditionOffset, SeekOrigin.Begin);
            fs.Write(BitConverter.GetBytes(AppearConditionPatched), 0, 4);

            AppearConditionCurrent = $"0x{AppearConditionPatched:X2} ({AppearConditionPatched})";
            StatusText = $"등장 조건 → 0x{AppearConditionPatched:X2} ({AppearConditionPatched})로 변경됨";
        }
        catch (Exception ex) { StatusText = $"저장 오류: {ex.Message}"; }
    }

    private void RestoreAppearCondition()
    {
        if (string.IsNullOrEmpty(ExeFilePath) || !File.Exists(ExeFilePath)) { StatusText = "파일을 찾을 수 없습니다"; return; }
        try
        {
            using var fs = new FileStream(ExeFilePath, FileMode.Open, FileAccess.Write);
            fs.Seek(AppearConditionOffset, SeekOrigin.Begin);
            fs.Write(BitConverter.GetBytes(AppearConditionOriginal), 0, 4);

            AppearConditionCurrent = $"0x{AppearConditionOriginal:X2} ({AppearConditionOriginal})";
            StatusText = $"등장 조건 → 원본 0x{AppearConditionOriginal:X2} ({AppearConditionOriginal})로 복원됨";
        }
        catch (Exception ex) { StatusText = $"복원 오류: {ex.Message}"; }
    }

    private void SaveLongRestLimit()
    {
        if (string.IsNullOrEmpty(ExeFilePath) || !File.Exists(ExeFilePath)) { StatusText = "파일을 찾을 수 없습니다"; return; }
        if (LongRestLimit < 1 || LongRestLimit > 127) { StatusText = "장기휴양 기간은 1~127 사이 값이어야 합니다"; return; }
        try
        {
            var backupPath = ExeFilePath + ".bak";
            if (!File.Exists(backupPath)) File.Copy(ExeFilePath, backupPath);

            using var fs = new FileStream(ExeFilePath, FileMode.Open, FileAccess.Write);
            fs.Seek(LongRestLimitOffset, SeekOrigin.Begin);
            fs.WriteByte((byte)LongRestLimit);

            StatusText = $"장기휴양 기간 → {LongRestLimit}개월로 변경됨";
        }
        catch (Exception ex) { StatusText = $"저장 오류: {ex.Message}"; }
    }

    private void RestoreLongRestLimit()
    {
        if (string.IsNullOrEmpty(ExeFilePath) || !File.Exists(ExeFilePath)) { StatusText = "파일을 찾을 수 없습니다"; return; }
        try
        {
            using var fs = new FileStream(ExeFilePath, FileMode.Open, FileAccess.Write);
            fs.Seek(LongRestLimitOffset, SeekOrigin.Begin);
            fs.WriteByte(LongRestLimitOriginal);

            LongRestLimit = LongRestLimitOriginal;
            StatusText = $"장기휴양 기간 → 원본({LongRestLimitOriginal}개월)으로 복원됨";
        }
        catch (Exception ex) { StatusText = $"복원 오류: {ex.Message}"; }
    }

    private void CheckJobButtonPatchStatus(byte[] data)
    {
        if (JobButtonPatchOffset + JobButtonPatchLength > data.Length)
        {
            JobButtonPatchStatus = "오프셋 범위 초과";
            IsJobButtonPatched = false;
            return;
        }

        bool matchOriginal = true;
        bool matchPatched = true;
        for (int i = 0; i < JobButtonPatchLength; i++)
        {
            if (data[JobButtonPatchOffset + i] != JobButtonOriginalBytes[i]) matchOriginal = false;
            if (data[JobButtonPatchOffset + i] != JobButtonPatchedBytes[i]) matchPatched = false;
        }

        if (matchPatched)
        {
            IsJobButtonPatched = true;
            JobButtonPatchStatus = "적용됨";
        }
        else if (matchOriginal)
        {
            IsJobButtonPatched = false;
            JobButtonPatchStatus = "미적용";
        }
        else
        {
            IsJobButtonPatched = false;
            JobButtonPatchStatus = "알 수 없음";
        }
    }

    private void ApplyJobButtonPatch()
    {
        if (string.IsNullOrEmpty(ExeFilePath) || !File.Exists(ExeFilePath)) { StatusText = "파일을 찾을 수 없습니다"; return; }
        try
        {
            var backupPath = ExeFilePath + ".bak";
            if (!File.Exists(backupPath)) File.Copy(ExeFilePath, backupPath);

            using var fs = new FileStream(ExeFilePath, FileMode.Open, FileAccess.Write);
            fs.Seek(JobButtonPatchOffset, SeekOrigin.Begin);
            fs.Write(JobButtonPatchedBytes, 0, JobButtonPatchLength);

            IsJobButtonPatched = true;
            JobButtonPatchStatus = "적용됨";
            StatusText = "직업버튼 능력치 갱신 패치 적용됨";
        }
        catch (Exception ex) { StatusText = $"저장 오류: {ex.Message}"; }
    }

    private void RestoreJobButtonPatch()
    {
        if (string.IsNullOrEmpty(ExeFilePath) || !File.Exists(ExeFilePath)) { StatusText = "파일을 찾을 수 없습니다"; return; }
        try
        {
            using var fs = new FileStream(ExeFilePath, FileMode.Open, FileAccess.Write);
            fs.Seek(JobButtonPatchOffset, SeekOrigin.Begin);
            fs.Write(JobButtonOriginalBytes, 0, JobButtonPatchLength);

            IsJobButtonPatched = false;
            JobButtonPatchStatus = "미적용";
            StatusText = "직업버튼 능력치 갱신 패치 원본 복원됨";
        }
        catch (Exception ex) { StatusText = $"복원 오류: {ex.Message}"; }
    }

    // ===== 커스텀 패치 =====

    private void EnsureBackup()
    {
        var backupPath = ExeFilePath + ".bak";
        if (!File.Exists(backupPath)) File.Copy(ExeFilePath, backupPath);
    }

    private static bool TryParseAddress(string text, out int addr)
    {
        addr = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
        return int.TryParse(s, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out addr) && addr >= 0;
    }

    private static long ReadValueAt(byte[] data, int addr, int byteSize) => byteSize switch
    {
        1 => data[addr],
        2 => BitConverter.ToUInt16(data, addr),
        _ => BitConverter.ToUInt32(data, addr),
    };

    private void WireCustomPatch(CustomPatchItem item)
    {
        item.OnValueChanged = WriteCustomPatch;
        item.OnToggleChanged = WriteTogglePatch;
        item.OnDefinitionChanged = i => LoadCustomPatchValue(i);
        item.PropertyChanged += OnPatchItemPropertyChanged;
    }

    private void OnPatchItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 상태 텍스트는 표시용이라 저장 대상이 아니다
        if (e.PropertyName == nameof(CustomPatchItem.StatusText)) return;

        // 적용 상태(토글)는 EXE에서 읽어오는 런타임 상태 — 저장 대상 아님
        if (e.PropertyName == nameof(CustomPatchItem.IsApplied)) return;

        // 체크는 내보내기 선택용 — 저장 대상이 아니고, 버튼 표시 여부만 갱신
        if (e.PropertyName == nameof(CustomPatchItem.IsChecked))
        {
            UpdateHasAnyChecked();
            return;
        }

        AutoSaveCustomPatches();
    }

    /// <summary>항목의 주소 문자열들을 파싱한다. 하나라도 실패하면 false.</summary>
    private static bool TryParseAddresses(CustomPatchItem item, out List<int> addrs)
    {
        addrs = new List<int>();
        var list = item.AddressList;
        if (list.Count == 0) return false;
        foreach (var s in list)
        {
            if (!TryParseAddress(s, out int a)) return false;
            addrs.Add(a);
        }
        return true;
    }

    /// <summary>지정 값을 항목의 모든 주소에 (리틀엔디안 ByteSize 바이트로) 기록한다.</summary>
    private bool WriteValueToAllAddresses(CustomPatchItem item, long value, out int count, out string error)
    {
        count = 0;
        error = "";
        if (!TryParseAddresses(item, out var addrs)) { error = "주소 오류"; return false; }

        byte[] bytes = item.ByteSize switch
        {
            1 => new[] { (byte)value },
            2 => BitConverter.GetBytes((ushort)value),
            _ => BitConverter.GetBytes((uint)value),
        };

        EnsureBackup();
        using var fs = new FileStream(ExeFilePath, FileMode.Open, FileAccess.Write);
        foreach (var a in addrs)
        {
            fs.Seek(a, SeekOrigin.Begin);
            fs.Write(bytes, 0, item.ByteSize);
            count++;
        }
        return true;
    }

    private void UpdateHasAnyChecked()
    {
        HasAnyChecked = CustomPatches.Any(p => p.IsChecked);
    }

    private static CustomPatchDto ToDto(CustomPatchItem p) => new()
    {
        Name = p.Name,
        Description = p.Description,
        Address = p.AddressHex,
        Addresses = p.AddressList.ToList(),
        ByteSize = p.ByteSize,
        Min = p.MinValue,
        Max = p.MaxValue,
        Value = p.Value,
        Type = p.PatchType,
        OriginalValue = p.OriginalValue,
        PatchedValue = p.PatchedValue
    };

    private List<CustomPatchDto> BuildPatchDtos() => CustomPatches.Select(ToDto).ToList();

    private void WritePatchesToPath(string path)
    {
        var json = JsonSerializer.Serialize(BuildPatchDtos(), PatchJsonOptions);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    /// <summary>체크된 항목만 위치를 지정해 별도 파일로 내보낸다.</summary>
    private void ExportSelectedCustomPatches()
    {
        var selected = CustomPatches.Where(p => p.IsChecked).ToList();
        if (selected.Count == 0)
        {
            StatusText = "내보낼 항목을 체크하세요";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "선택 패치 내보내기",
            Filter = "패치 파일 (*.json)|*.json|모든 파일 (*.*)|*.*",
            FileName = "custom_patches_export.json",
            DefaultExt = ".json"
        };
        if (!string.IsNullOrEmpty(ExeFilePath) && File.Exists(ExeFilePath))
            dialog.InitialDirectory = Path.GetDirectoryName(ExeFilePath);

        if (dialog.ShowDialog() != true) return;

        try
        {
            var dtos = selected.Select(ToDto).ToList();

            var json = JsonSerializer.Serialize(dtos, PatchJsonOptions);
            File.WriteAllText(dialog.FileName, json, Encoding.UTF8);

            StatusText = $"선택 {dtos.Count}개 내보냄: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            StatusText = $"내보내기 오류: {ex.Message}";
        }
    }

    /// <summary>입력/수정 즉시 기본 경로(%APPDATA%)에 자동 저장.</summary>
    private void AutoSaveCustomPatches()
    {
        if (_suppressAutoSave) return;
        try { WritePatchesToPath(CustomPatchAutoSavePath); }
        catch { /* 자동 저장 실패는 무시 */ }
    }

    /// <summary>EXE에서 읽은 값을 표시만 한다(자동 저장 트리거 안 함).</summary>
    private void SetValueSilentNoSave(CustomPatchItem item, long value)
    {
        var prev = _suppressAutoSave;
        _suppressAutoSave = true;
        try { item.SetValueSilent(value); }
        finally { _suppressAutoSave = prev; }
    }

    /// <summary>EXE에서 읽은 적용 상태를 표시만 한다(EXE 기록 안 함).</summary>
    private void SetAppliedSilentNoSave(CustomPatchItem item, bool applied)
    {
        var prev = _suppressAutoSave;
        _suppressAutoSave = true;
        try { item.SetAppliedSilent(applied); }
        finally { _suppressAutoSave = prev; }
    }

    private void ApplyPatchDtos(List<CustomPatchDto> dtos, bool replace = true)
    {
        var prev = _suppressAutoSave;
        _suppressAutoSave = true;
        try
        {
            if (replace)
            {
                foreach (var old in CustomPatches.ToList())
                    old.PropertyChanged -= OnPatchItemPropertyChanged;
                CustomPatches.Clear();
            }

            foreach (var d in dtos)
            {
                // Addresses(다중)가 있으면 우선, 없으면 Address(단일, 하위호환)
                var addresses = (d.Addresses != null && d.Addresses.Count > 0)
                    ? d.Addresses
                    : (string.IsNullOrWhiteSpace(d.Address) ? new List<string>() : new List<string> { d.Address });

                var item = new CustomPatchItem
                {
                    Name = d.Name,
                    Description = d.Description,
                    AddressesText = string.Join(", ", addresses),
                    ByteSize = (d.ByteSize == 2 || d.ByteSize == 4) ? d.ByteSize : 1,
                    // 최소/최대는 ByteSize 설정 시 자동 결정 (저장된 Min/Max는 무시)
                    PatchType = d.Type,
                    OriginalValue = d.OriginalValue,
                    PatchedValue = d.PatchedValue,
                };
                WireCustomPatch(item);
                LoadCustomPatchValue(item);   // 현재 EXE 값/적용 상태 표시
                CustomPatches.Add(item);
            }
        }
        finally { _suppressAutoSave = prev; }

        UpdateHasAnyChecked();
    }

    /// <summary>앱 시작 시 목록 복원. 사용자 파일이 없으면(첫 실행) 번들 기본값으로 시드한다.</summary>
    private void LoadAutoSavedCustomPatches()
    {
        try
        {
            bool firstRun = !File.Exists(CustomPatchAutoSavePath);

            // 사용자 파일이 있으면 그걸, 없으면(첫 실행) 번들 기본값을 사용
            var json = firstRun
                ? ReadBundledDefaultPatchesJson()
                : File.ReadAllText(CustomPatchAutoSavePath, Encoding.UTF8);

            if (string.IsNullOrWhiteSpace(json)) return;

            var dtos = JsonSerializer.Deserialize<List<CustomPatchDto>>(json) ?? new List<CustomPatchDto>();
            ApplyPatchDtos(dtos);

            // 첫 실행이면 기본값을 사용자 파일(%APPDATA%)로 저장해 둔다
            if (firstRun && CustomPatches.Count > 0)
                WritePatchesToPath(CustomPatchAutoSavePath);
        }
        catch { /* 복원 실패는 무시 */ }
    }

    /// <summary>어셈블리에 임베드된 기본 커스텀 패치 JSON을 읽는다.</summary>
    private static string? ReadBundledDefaultPatchesJson()
    {
        try
        {
            var asm = typeof(ExePatchContentViewModel).Assembly;
            var name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("default_custom_patches.json", StringComparison.OrdinalIgnoreCase));
            if (name == null) return null;

            using var stream = asm.GetManifestResourceStream(name);
            if (stream == null) return null;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch { return null; }
    }

    /// <summary>EXE에서 해당 항목의 현재 값/적용 상태를 읽어 표시(기록하지 않음).</summary>
    private void LoadCustomPatchValue(CustomPatchItem item)
    {
        if (string.IsNullOrEmpty(ExeFilePath) || !File.Exists(ExeFilePath)) return;
        try
        {
            var data = File.ReadAllBytes(ExeFilePath);
            ReadCurrentInto(item, data);
        }
        catch (Exception ex) { item.StatusText = ex.Message; }
    }

    /// <summary>첫 주소의 현재 값을 읽어 항목 상태에 반영한다(EXE 기록 안 함).</summary>
    private void ReadCurrentInto(CustomPatchItem item, byte[] data)
    {
        if (!TryParseAddresses(item, out var addrs)) { item.StatusText = "주소 오류"; return; }
        int addr = addrs[0];
        if (addr + item.ByteSize > data.Length) { item.StatusText = "범위 초과"; return; }

        long cur = ReadValueAt(data, addr, item.ByteSize);
        string where = item.AddressCount > 1 ? $" (외 {item.AddressCount - 1}곳)" : "";

        if (item.IsToggle)
        {
            bool applied = cur == item.PatchedValue;
            SetAppliedSilentNoSave(item, applied);
            item.StatusText = applied ? $"적용됨{where}"
                : (cur == item.OriginalValue ? $"해제됨{where}" : $"현재 {cur}{where}");
        }
        else
        {
            SetValueSilentNoSave(item, cur);
            item.StatusText = $"현재 0x{addr:X}{where}";
        }
    }

    private void RefreshCustomPatchValues(byte[] data)
    {
        foreach (var p in CustomPatches)
            ReadCurrentInto(p, data);
    }

    private void AddCustomPatch()
    {
        var addrList = CustomPatchItem.SplitAddresses(NewPatchAddress);
        if (addrList.Count == 0 || !addrList.All(s => TryParseAddress(s, out _)))
        {
            StatusText = $"잘못된 헥스 주소: {NewPatchAddress}";
            return;
        }

        int byteSize = (NewPatchByteSize == 2 || NewPatchByteSize == 4) ? NewPatchByteSize : 1;

        var item = new CustomPatchItem
        {
            Name = NewPatchName,
            Description = NewPatchDescription,
            AddressesText = string.Join(", ", addrList),
            ByteSize = byteSize,   // ByteSize 설정 시 최소/최대 자동 결정
            IsToggle = NewPatchIsToggle,
            OriginalValue = NewPatchOriginalValue,
            PatchedValue = NewPatchPatchedValue,
        };

        WireCustomPatch(item);
        LoadCustomPatchValue(item);   // 현재 EXE 값/상태 읽어오기
        CustomPatches.Add(item);
        AutoSaveCustomPatches();      // 추가 즉시 자동 저장

        string kind = NewPatchIsToggle ? $"토글 {NewPatchOriginalValue}↔{NewPatchPatchedValue}" : $"{byteSize}바이트";
        StatusText = $"커스텀 패치 추가: {addrList.Count}곳 ({kind})";

        // 입력란 초기화 (범위/바이트 수/토글 옵션은 다음 입력 편의를 위해 유지)
        NewPatchName = "";
        NewPatchDescription = "";
        NewPatchAddress = "";
    }

    private void RemoveCustomPatch(CustomPatchItem? item)
    {
        if (item == null) return;
        item.PropertyChanged -= OnPatchItemPropertyChanged;
        CustomPatches.Remove(item);
        UpdateHasAnyChecked();
        AutoSaveCustomPatches();      // 삭제 즉시 자동 저장
        StatusText = $"커스텀 패치 삭제: {item.AddressSummary}";
    }

    /// <summary>값 입력형 패치: Value를 모든 주소에 기록.</summary>
    private void WriteCustomPatch(CustomPatchItem item)
    {
        if (item.IsToggle) return;   // 토글형은 WriteTogglePatch가 처리
        if (string.IsNullOrEmpty(ExeFilePath) || !File.Exists(ExeFilePath))
        {
            item.StatusText = "파일 없음";
            StatusText = "파일을 찾을 수 없습니다";
            return;
        }
        if (item.Value < item.MinValue || item.Value > item.MaxValue)
        {
            item.StatusText = $"범위({item.MinValue}~{item.MaxValue}) 초과";
            StatusText = $"값이 허용 범위({item.MinValue}~{item.MaxValue})를 벗어났습니다";
            return;
        }

        try
        {
            if (!WriteValueToAllAddresses(item, item.Value, out int count, out string error))
            {
                item.StatusText = error;
                return;
            }
            string where = count > 1 ? $" ({count}곳)" : $" (0x{item.AddressHex})";
            item.StatusText = $"적용됨{where}";
            var label = string.IsNullOrEmpty(item.Name) ? item.AddressSummary : item.Name;
            StatusText = $"커스텀 패치 적용: {label} = {item.Value}";
        }
        catch (Exception ex)
        {
            item.StatusText = "오류";
            StatusText = $"저장 오류: {ex.Message}";
        }
    }

    /// <summary>토글형 패치: 적용 시 PatchedValue, 해제 시 OriginalValue를 모든 주소에 기록.</summary>
    private void WriteTogglePatch(CustomPatchItem item)
    {
        if (string.IsNullOrEmpty(ExeFilePath) || !File.Exists(ExeFilePath))
        {
            item.StatusText = "파일 없음";
            StatusText = "파일을 찾을 수 없습니다";
            return;
        }

        long value = item.IsApplied ? item.PatchedValue : item.OriginalValue;
        try
        {
            if (!WriteValueToAllAddresses(item, value, out int count, out string error))
            {
                item.StatusText = error;
                return;
            }
            string where = count > 1 ? $" ({count}곳)" : "";
            item.StatusText = (item.IsApplied ? "적용됨" : "해제됨") + where;
            var label = string.IsNullOrEmpty(item.Name) ? item.AddressSummary : item.Name;
            StatusText = $"커스텀 패치 {(item.IsApplied ? "적용" : "해제")}: {label} = {value}";
        }
        catch (Exception ex)
        {
            item.StatusText = "오류";
            StatusText = $"저장 오류: {ex.Message}";
        }
    }

    private void SaveCustomPatches()
    {
        var dialog = new SaveFileDialog
        {
            Title = "커스텀 패치 저장",
            Filter = "패치 파일 (*.json)|*.json|모든 파일 (*.*)|*.*",
            FileName = "custom_patches.json",
            DefaultExt = ".json"
        };
        if (!string.IsNullOrEmpty(ExeFilePath) && File.Exists(ExeFilePath))
            dialog.InitialDirectory = Path.GetDirectoryName(ExeFilePath);

        if (dialog.ShowDialog() != true) return;

        try
        {
            WritePatchesToPath(dialog.FileName);
            StatusText = $"커스텀 패치 {CustomPatches.Count}개 저장: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            StatusText = $"저장 오류: {ex.Message}";
        }
    }

    private void LoadCustomPatches()
    {
        var dialog = new OpenFileDialog
        {
            Title = "커스텀 패치 불러오기",
            Filter = "패치 파일 (*.json)|*.json|모든 파일 (*.*)|*.*"
        };
        if (!string.IsNullOrEmpty(ExeFilePath) && File.Exists(ExeFilePath))
            dialog.InitialDirectory = Path.GetDirectoryName(ExeFilePath);

        if (dialog.ShowDialog() != true) return;

        try
        {
            var json = File.ReadAllText(dialog.FileName, Encoding.UTF8);
            var dtos = JsonSerializer.Deserialize<List<CustomPatchDto>>(json) ?? new List<CustomPatchDto>();

            ApplyPatchDtos(dtos, replace: false);   // 기존 목록에 추가(append)
            AutoSaveCustomPatches();   // 합쳐진 목록을 자동 저장 파일에도 반영

            StatusText = $"커스텀 패치 {dtos.Count}개 추가됨 (총 {CustomPatches.Count}개): {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            StatusText = $"불러오기 오류: {ex.Message}";
        }
    }

    private void RestoreOriginal()
    {
        if (string.IsNullOrEmpty(ExeFilePath) || !File.Exists(ExeFilePath))
        {
            StatusText = "파일을 찾을 수 없습니다";
            return;
        }

        // 백업 파일에서 복원
        var backupPath = ExeFilePath + ".bak";
        if (File.Exists(backupPath))
        {
            try
            {
                File.Copy(backupPath, ExeFilePath, true);
                StatusText = "백업에서 원본 복원 완료";
                LoadExeData();
            }
            catch (Exception ex)
            {
                StatusText = $"복원 오류: {ex.Message}";
            }
        }
        else
        {
            StatusText = "백업 파일이 없습니다";
        }
    }
}
