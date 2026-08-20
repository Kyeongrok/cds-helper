using System.IO;
using System.Windows.Media;

namespace CdsHelper.Game.Local.Helpers;

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

    /// <summary>뭍에 올라 말로 다니는 동안 도는 곡.</summary>
    public const int LandTrack = 26;

    /// <summary>술집에 들어가 있는 동안 도는 곡.</summary>
    public const int TavernTrack = 22;

    /// <summary>교회에 들어가 있는 동안 도는 곡. 나오면 도시 곡으로 돌아간다.</summary>
    public const int ChurchTrack = 16;

    /// <summary>중근동(이슬람) 문화권 도시에서 도는 곡.</summary>
    public const int IslamCityTrack = 7;

    /// <summary>북유럽 문화권 도시에서 도는 곡.</summary>
    public const int NorthEuropeCityTrack = 9;

    /// <summary>
    /// 그 도시에서 돌 곡. 문화권마다 다르다 — 세우타처럼 중근동에 드는 도시는 딴 곡이 돈다.
    /// </summary>
    /// <remarks>
    /// 문화권은 <c>cities.json</c> 의 <c>culturalSphere</c> 다. <b>아직 다 채우지 못했다</b> —
    /// 확인한 것만 적고 나머지는 <see cref="CityTrack"/> 으로 둔다. 게임에서 들어 보고
    /// 하나씩 채우면 된다(엉뚱한 곡을 지어내는 것보다 낫다).
    ///
    /// 자료에 적힌 이름은 "이슬람" 인데 게임에서 부르는 말은 "중근동" 이다 — 둘 다 받는다.
    /// </remarks>
    public static int CityTrackFor(string? culturalSphere) => culturalSphere switch
    {
        "이슬람" or "중근동" => IslamCityTrack,
        "북유럽" => NorthEuropeCityTrack,
        _ => CityTrack,
    };

    /// <summary>
    /// 후원자를 알현하는 동안 도는 곡. 집사가 문간에서 돌려보내면 바뀌지 않는다 —
    /// 인사를 받고 안으로 들어갈 때부터다. 그 자리를 나오면 도시 곡으로 돌아간다.
    /// </summary>
    public const int SponsorTrack = 21;

    private readonly MediaPlayer _player = new();
    private string _dir = "";
    private int _track = -1;

    public BgmPlayer()
    {
        _player.MediaEnded += (_, _) =>
        {
            // 기다리라고 해 둔 곡이 있으면 이제 갈아탄다(해상에서 해역이 바뀔 때다).
            if (_queued >= 0)
            {
                int next = _queued;
                _queued = -1;
                Play(next);
                return;
            }

            // 없으면 처음으로 되감아 다시 튼다. 이어 재생은 이 한 줄이면 된다.
            _player.Position = TimeSpan.Zero;
            _player.Play();
        };
    }

    /// <summary>지금 곡이 끝나면 갈아탈 곡. 없으면 -1.</summary>
    private int _queued = -1;

    /// <summary>지금 곡이 끝나면 갈아탈 곡. 기다리는 것이 없으면 -1.</summary>
    public int Queued => _queued;

    /// <summary>못 튼 까닭 한 줄. 잘 돌고 있으면 빈 문자열.</summary>
    public string LastError { get; private set; } = "";

    /// <summary>지금 도는 곡 번호. 아무것도 안 틀고 있으면 -1.</summary>
    public int Track => _track;

    /// <summary>게임 폴더를 알려 준다. 그 밑의 <c>bgm</c> 을 본다.</summary>
    public void SetGameDirectory(string gameDir) => _dir = gameDir;

    /// <summary>곡을 틀지. 끄면 소리를 멈추고, 켜면 마지막으로 틀라던 곡부터 다시 돈다.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (!value) { Stop(); return; }
            int again = _wanted;
            _wanted = -1;
            if (again >= 0) Play(again);
        }
    }

    private bool _enabled = true;

    /// <summary>마지막으로 틀라고 한 곡. 껐다 켤 때 이 곡으로 돌아온다.</summary>
    private int _wanted = -1;

    /// <summary>
    /// 지금 곡을 끝까지 들려 준 뒤에 갈아탄다. <b>해상에서 해역이 바뀔 때</b> 쓴다 —
    /// 게임은 그때만 곡을 자르지 않고 기다린다.
    /// </summary>
    /// <remarks>
    /// 도시에 들어가거나 건물에 들어설 때는 이것이 아니라 <see cref="Play"/> 다.
    /// 그쪽은 곡을 그 자리에서 자른다 — 게임도 그렇다.
    ///
    /// 아무것도 안 돌고 있으면 기다릴 것이 없으므로 바로 튼다.
    /// </remarks>
    public void PlayWhenDone(int track)
    {
        if (_track == track) { _queued = -1; return; }
        if (_track < 0 || !_enabled) { Play(track); return; }
        _queued = track;
    }

    /// <summary>그 번호의 곡으로 갈아 튼다. 이미 그 곡이 돌고 있으면 그대로 둔다.</summary>
    public void Play(int track)
    {
        _queued = -1;   // 바로 갈아타라고 했으니 기다리던 것은 버린다
        if (_wanted == track && _track == track) return;
        _wanted = track;
        if (!_enabled) return;
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
