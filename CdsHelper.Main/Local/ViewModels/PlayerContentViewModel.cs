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

    private PlayerData? _player;
    public PlayerData? Player
    {
        get => _player;
        set => SetProperty(ref _player, value);
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

    public ICommand LoadSaveCommand { get; }

    public PlayerContentViewModel(SaveDataService saveDataService)
    {
        _saveDataService = saveDataService;
        LoadSaveCommand = new DelegateCommand(LoadSaveFile);

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
            StatusText = "로딩 중...";

            Player = _saveDataService.ReadPlayerData(filePath);
            FilePath = filePath;

            if (Player != null)
            {
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
}
