using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using CdsHelper.Support.Local.Settings;
using Prism.Commands;
using Prism.Mvvm;

namespace CdsHelper.Main.Local.ViewModels;

public class AppearPatchItem : BindableBase
{
    public int Index { get; set; }
    public string Address { get; set; } = "";
    public string Description { get; set; } = "";
    public string OriginalHex { get; set; } = "";
    public string PatchedHex { get; set; } = "";
    public string CurrentHex { get; set; } = "";

    private bool _isPatched;
    public bool IsPatched
    {
        get => _isPatched;
        set => SetProperty(ref _isPatched, value);
    }

    public string Status => IsPatched ? "패치됨" : "원본";
}

public class ExeAppearPatchContentViewModel : BindableBase
{
    // 장기휴양 기간 제한 오프셋 (push 0Ch 명령의 operand 위치)
    private const int LongRestLimitOffset = 0x05FB83;
    private const int LongRestLimitOriginal = 12;

    // 대항해시대2 인물 등장 패치 주소들
    private static readonly (int Address, int Length, byte[] Original, byte[] Patched, string Description)[] PatchData = new[]
    {
        (0x00030F6D, 4, new byte[] { 0xBF, 0x00, 0x00, 0x00 }, new byte[] { 0xC9, 0x00, 0x00, 0x00 }, "등장 조건 1"),
        (0x00031C23, 4, new byte[] { 0xBF, 0x00, 0x00, 0x00 }, new byte[] { 0xC9, 0x00, 0x00, 0x00 }, "등장 조건 2"),
        (0x000E78B4, 8, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x04, 0x00, 0x00, 0x00 }, new byte[] { 0xC4, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF }, "도냐·마리나 (이벤트→1676년)"),
        (0x000E7980, 8, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x05, 0x00, 0x00, 0x00 }, new byte[] { 0xCE, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF }, "미켈란젤로 (이벤트→1686년)"),
    };

    private ObservableCollection<AppearPatchItem> _patchItems = new();
    public ObservableCollection<AppearPatchItem> PatchItems
    {
        get => _patchItems;
        set => SetProperty(ref _patchItems, value);
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

    private int _patchedCount;
    public int PatchedCount
    {
        get => _patchedCount;
        set => SetProperty(ref _patchedCount, value);
    }

    private int _longRestLimit;
    public int LongRestLimit
    {
        get => _longRestLimit;
        set => SetProperty(ref _longRestLimit, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand ApplyPatchCommand { get; }
    public ICommand RestoreOriginalCommand { get; }
    public ICommand SaveLongRestLimitCommand { get; }
    public ICommand RestoreLongRestLimitCommand { get; }

    public ExeAppearPatchContentViewModel()
    {
        RefreshCommand = new DelegateCommand(LoadExeData);
        ApplyPatchCommand = new DelegateCommand(ApplyPatch);
        RestoreOriginalCommand = new DelegateCommand(RestoreOriginal);
        SaveLongRestLimitCommand = new DelegateCommand(SaveLongRestLimit);
        RestoreLongRestLimitCommand = new DelegateCommand(RestoreLongRestLimit);

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

    private static string BytesToHex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", " ");

    private static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    private void LoadExeData()
    {
        PatchItems.Clear();
        PatchedCount = 0;

        if (string.IsNullOrEmpty(ExeFilePath) || !File.Exists(ExeFilePath))
        {
            StatusText = $"파일을 찾을 수 없습니다: {ExeFilePath}";
            return;
        }

        try
        {
            var data = File.ReadAllBytes(ExeFilePath);
            LoadLongRestLimit(data);

            for (int i = 0; i < PatchData.Length; i++)
            {
                var (address, length, original, patched, description) = PatchData[i];

                if (address + length > data.Length)
                {
                    StatusText = $"파일 크기가 예상보다 작습니다 (주소: 0x{address:X8})";
                    continue;
                }

                var currentBytes = new byte[length];
                Array.Copy(data, address, currentBytes, 0, length);

                var item = new AppearPatchItem
                {
                    Index = i + 1,
                    Address = $"0x{address:X8}",
                    Description = description,
                    OriginalHex = BytesToHex(original),
                    PatchedHex = BytesToHex(patched),
                    CurrentHex = BytesToHex(currentBytes),
                    IsPatched = BytesEqual(currentBytes, patched)
                };

                PatchItems.Add(item);

                if (item.IsPatched)
                    PatchedCount++;
            }

            StatusText = $"로드 완료 - {PatchedCount}/{PatchItems.Count} 패치됨";
        }
        catch (Exception ex)
        {
            StatusText = $"오류: {ex.Message}";
        }
    }

    private void ApplyPatch()
    {
        if (string.IsNullOrEmpty(ExeFilePath) || !File.Exists(ExeFilePath))
        {
            StatusText = "파일을 찾을 수 없습니다";
            return;
        }

        try
        {
            var data = File.ReadAllBytes(ExeFilePath);
            int patchCount = 0;

            foreach (var (address, length, original, patched, _) in PatchData)
            {
                if (address + length > data.Length) continue;

                var currentBytes = new byte[length];
                Array.Copy(data, address, currentBytes, 0, length);

                // 원본 또는 다른 값이면 패치 적용
                if (!BytesEqual(currentBytes, patched))
                {
                    Array.Copy(patched, 0, data, address, length);
                    patchCount++;
                }
            }

            if (patchCount > 0)
            {
                // 백업 생성
                var backupPath = ExeFilePath + ".appear.bak";
                if (!File.Exists(backupPath))
                {
                    File.Copy(ExeFilePath, backupPath);
                }

                File.WriteAllBytes(ExeFilePath, data);
                StatusText = $"{patchCount}개 주소 패치 완료";
            }
            else
            {
                StatusText = "패치할 항목이 없습니다 (이미 패치됨)";
            }

            LoadExeData();
        }
        catch (Exception ex)
        {
            StatusText = $"패치 오류: {ex.Message}";
        }
    }

    private void LoadLongRestLimit(byte[] data)
    {
        if (LongRestLimitOffset < data.Length)
        {
            LongRestLimit = data[LongRestLimitOffset];
        }
    }

    private void SaveLongRestLimit()
    {
        if (string.IsNullOrEmpty(ExeFilePath) || !File.Exists(ExeFilePath))
        {
            StatusText = "파일을 찾을 수 없습니다";
            return;
        }

        if (LongRestLimit < 1 || LongRestLimit > 127)
        {
            StatusText = "장기휴양 기간은 1~127 사이 값이어야 합니다";
            return;
        }

        try
        {
            var backupPath = ExeFilePath + ".appear.bak";
            if (!File.Exists(backupPath))
            {
                File.Copy(ExeFilePath, backupPath);
            }

            using var fs = new FileStream(ExeFilePath, FileMode.Open, FileAccess.Write);
            fs.Seek(LongRestLimitOffset, SeekOrigin.Begin);
            fs.WriteByte((byte)LongRestLimit);

            StatusText = $"장기휴양 기간 → {LongRestLimit}개월로 변경됨";
        }
        catch (Exception ex)
        {
            StatusText = $"저장 오류: {ex.Message}";
        }
    }

    private void RestoreLongRestLimit()
    {
        if (string.IsNullOrEmpty(ExeFilePath) || !File.Exists(ExeFilePath))
        {
            StatusText = "파일을 찾을 수 없습니다";
            return;
        }

        try
        {
            using var fs = new FileStream(ExeFilePath, FileMode.Open, FileAccess.Write);
            fs.Seek(LongRestLimitOffset, SeekOrigin.Begin);
            fs.WriteByte(LongRestLimitOriginal);

            LongRestLimit = LongRestLimitOriginal;
            StatusText = $"장기휴양 기간 → 원본({LongRestLimitOriginal}개월)으로 복원됨";
        }
        catch (Exception ex)
        {
            StatusText = $"복원 오류: {ex.Message}";
        }
    }

    private void RestoreOriginal()
    {
        if (string.IsNullOrEmpty(ExeFilePath) || !File.Exists(ExeFilePath))
        {
            StatusText = "파일을 찾을 수 없습니다";
            return;
        }

        try
        {
            var data = File.ReadAllBytes(ExeFilePath);
            int restoreCount = 0;

            foreach (var (address, length, original, patched, _) in PatchData)
            {
                if (address + length > data.Length) continue;

                var currentBytes = new byte[length];
                Array.Copy(data, address, currentBytes, 0, length);

                // 패치 값이면 원본으로 복원
                if (BytesEqual(currentBytes, patched))
                {
                    Array.Copy(original, 0, data, address, length);
                    restoreCount++;
                }
            }

            if (restoreCount > 0)
            {
                File.WriteAllBytes(ExeFilePath, data);
                StatusText = $"{restoreCount}개 주소 원본 복원 완료";
            }
            else
            {
                StatusText = "복원할 항목이 없습니다 (이미 원본)";
            }

            LoadExeData();
        }
        catch (Exception ex)
        {
            StatusText = $"복원 오류: {ex.Message}";
        }
    }
}
