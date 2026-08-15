using System.IO;
using System.Windows.Media;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 게임 폴더의 BGM 한 곡을 되풀이해 튼다.
/// </summary>
/// <remarks>
/// 파일은 게임 폴더 밑 <c>bgm/TrackNN.mp3</c> 다. 저장소로 복사하지 않고 그 자리에서 읽는다 —
/// 한 벌이 130MB 라 들고 다닐 것이 못 된다. 폴더가 없으면 아무 소리도 내지 않고 넘어간다.
///
/// <see cref="MediaPlayer"/> 는 만든 스레드(UI)에 매인다. 창에서 하나 만들어 쓰고 닫을 때 놓는다.
/// </remarks>
public sealed class BgmPlayer : IDisposable
{
    /// <summary>타이틀 화면에서 도는 곡.</summary>
    public const int TitleTrack = 23;

    /// <summary>바다에서 도는 곡.</summary>
    public const int SeaTrack = 15;

    /// <summary>도시에 들어가 있는 동안 도는 곡.</summary>
    public const int CityTrack = 10;

    /// <summary>술집에 들어가 있는 동안 도는 곡.</summary>
    public const int TavernTrack = 22;

    private readonly MediaPlayer _player = new();
    private string _dir = "";
    private int _track = -1;

    public BgmPlayer()
    {
        // 끝까지 가면 처음으로 되감아 다시 튼다. 이어 재생은 이 한 줄이면 된다.
        _player.MediaEnded += (_, _) => { _player.Position = TimeSpan.Zero; _player.Play(); };
    }

    /// <summary>못 튼 까닭 한 줄. 잘 돌고 있으면 빈 문자열.</summary>
    public string LastError { get; private set; } = "";

    /// <summary>지금 도는 곡 번호. 아무것도 안 틀고 있으면 -1.</summary>
    public int Track => _track;

    /// <summary>게임 폴더를 알려 준다. 그 밑의 <c>bgm</c> 을 본다.</summary>
    public void SetGameDirectory(string gameDir) => _dir = gameDir;

    /// <summary>그 번호의 곡으로 갈아 튼다. 이미 그 곡이 돌고 있으면 그대로 둔다.</summary>
    public void Play(int track)
    {
        if (_track == track) return;

        var path = Path.Combine(_dir, "bgm", $"Track{track:D2}.mp3");
        if (!File.Exists(path))
        {
            LastError = $"{path} 없음";
            return;
        }

        try
        {
            _player.Open(new Uri(path));
            _player.Play();
            _track = track;
            LastError = "";
        }
        catch (Exception ex)
        {
            LastError = $"{path} 를 틀지 못했습니다 — {ex.Message}";
        }
    }

    public void Stop()
    {
        _player.Stop();
        _player.Close();
        _track = -1;
    }

    public void Dispose() => Stop();
}
