using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Input;
using CdsHelper.Support.Local.Settings;
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

    public ICommand RefreshCommand { get; }
    public ICommand RestoreOriginalCommand { get; }

    public ExePatchContentViewModel()
    {
        // EUC-KR 인코딩 등록
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        RefreshCommand = new DelegateCommand(LoadExeData);
        RestoreOriginalCommand = new DelegateCommand(RestoreOriginal);

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

            for (int i = 0; i < MaxRecords; i++)
            {
                int recAddr = DataStart + (i * RecordSize);
                if (recAddr + RecordSize > data.Length) break;

                int delay = BitConverter.ToInt32(data, recAddr);
                int type = BitConverter.ToInt32(data, recAddr + 0x04);
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
                    RecordAddress = $"0x{recAddr:X6}",
                    PatchAddress = $"0x{patchAddr:X6}",
                    Name = fullName,
                    _appearYear = appearYear,
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
                    OnAppearYearChanged = SaveAppearYear
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
