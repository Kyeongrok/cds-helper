using System.IO;
using System.Text;
using System.Windows;
using CdsHelper.Game.Local.Settings;
using CdsHelper.Game.UI.Views;
using CdsHelper.Support.Local.Settings;

namespace CdsHelper.Play;

/// <summary>
/// 놀이만 띄우는 실행 파일 — <c>CdsHelperPlay.exe</c>.
/// </summary>
/// <remarks>
/// 세이브 뷰어(<c>CdsHelper.exe</c>)를 거치지 않고 <see cref="ShipMapWindow"/> 를 바로 연다.
/// 두 exe 는 <b>같은 폴더에 나란히</b> 놓이고 설정도 같은 자리를 본다
/// (<c>%APPDATA%\CdsHelper</c>) — 뷰어에서 세이브를 열어 두었으면 이쪽도 그 게임 폴더를
/// 그대로 쓴다.
///
/// 게임 폴더를 아직 모르면 <b>처음 켤 때 한 번 묻는다</b>. 뷰어에는 "세이브 파일 열기" 가
/// 있지만 이쪽에는 없으니, 여기서 안 물으면 곡도 그림도 못 읽는다.
/// </remarks>
internal sealed class App : Application
{
    [STAThread]
    public static void Main()
    {
        var app = new App();
        app.InitializeComponent_();
        app.Run();
    }

    private void InitializeComponent_()
    {
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"{args.Exception.Message}\n\n{args.Exception.StackTrace}",
                            "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        Startup += (_, _) => Begin();
    }

    private void Begin()
    {
        // 게임 자료가 CP949 라 코드페이지를 먼저 열어 둔다.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        GameSettings.Load();

        if (!EnsureGameFolder()) { Shutdown(); return; }

        // 미궁·일기토는 딴 어셈블리에 있어 놀이 쪽에서 곧장 못 부른다 — 여기서 걸어 준다.
        ShipMapWindow.MazeGame = Maze.MazeGame.Play;
        ShipMapWindow.DuelGame = Duel.DuelGame.Play;

        var window = new ShipMapWindow();
        MainWindow = window;
        window.Show();
    }

    /// <summary>
    /// 게임 폴더를 아는지 보고, 모르면 <c>SAVEDATA.CDS</c> 를 골라 달라고 한 번 묻는다.
    /// </summary>
    /// <remarks>
    /// 폴더가 아니라 <b>세이브 파일</b>을 고르게 한다 — 앱이 그 파일 경로를 들고 있고
    /// (<see cref="AppSettings.LastSaveFilePath"/>) 게임 폴더는 그 상위 폴더로 얻기 때문이다.
    /// 뷰어와 같은 값을 쓰므로 한쪽에서 열어 두면 다른 쪽도 안다.
    /// </remarks>
    private static bool EnsureGameFolder()
    {
        string? known = AppSettings.LastSaveFilePath;
        if (!string.IsNullOrEmpty(known) && File.Exists(known)) return true;

        MessageBox.Show(
            "대항해시대3 이 어디 있는지 아직 모릅니다.\n게임 폴더의 SAVEDATA.CDS 를 골라 주세요.",
            "대항해시대3", MessageBoxButton.OK, MessageBoxImage.Information);

        var pick = new Microsoft.Win32.OpenFileDialog
        {
            Title = "SAVEDATA.CDS 를 고르세요",
            Filter = "대항해시대3 세이브 (SAVEDATA.CDS)|SAVEDATA.CDS|모든 파일 (*.*)|*.*",
            CheckFileExists = true,
        };

        if (pick.ShowDialog() != true) return false;

        AppSettings.LastSaveFilePath = pick.FileName;
        return true;
    }
}
