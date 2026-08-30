using System.IO;
using System.Media;
using CdsHelper.Support.Local.Helpers;

using CdsHelper.Support.Local.Settings;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 게임 효과음(WAVES.CDS)을 틀어 준다. 놀이 중에 짧게 소리를 낼 때 쓴다.
/// </summary>
/// <remarks>
/// <see cref="WaveBank"/> 가 파트를 RIFF WAVE 통째로 풀어 주므로 그대로
/// <see cref="SoundPlayer"/> 에 넘기면 된다. 효과음 목록을 보는 창
/// (<c>WaveBankDialog</c>)과 같은 길이다.
///
/// 배경음악(<see cref="BgmPlayer"/>)과 따로 논다 — 효과음은 곡을 끊지 않고 겹쳐 난다.
/// 효과음은 <see cref="GameSettings.SfxEnabled"/> 하나로 갈린다 — 배경음악과 따로 켜고 끈다.
/// </remarks>
public sealed class SoundBank : IDisposable
{
    /// <summary>
    /// 닻을 내리고 올릴 때 나는 소리. 효과음 창의 <b>파트 1</b>(사운드 ID 29) 이다.
    /// </summary>
    /// <remarks>
    /// 파트 번호로 적는다 — 사운드 ID 는 여기에 28 을 더한 값이라 헷갈리기 쉽다
    /// (<see cref="WaveBank.FirstSoundId"/>).
    /// </remarks>
    public const int AnchorPart = 1;

    /// <summary>
    /// 집사가 문 앞에서 돌려보낼 때 나는 소리("…님은 바쁘셔서 만나실 수 없습니다").
    /// 닻과 같은 파트 1 이다.
    /// </summary>
    public const int TurnedAwayPart = 1;

    private readonly WaveBank _bank;
    private readonly SoundPlayer _player = new();

    private SoundBank(WaveBank bank) => _bank = bank;

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>게임 폴더의 WAVES.CDS 를 연다. 없으면 null — 소리가 안 날 뿐이다.</summary>
    public static SoundBank? Open(string gameDirectory)
    {
        LastError = "";
        var bank = WaveBank.LoadFromDirectory(gameDirectory);
        if (bank == null) { LastError = WaveBank.LastError; return null; }
        return new SoundBank(bank);
    }

    private static SoundBank? _shared;
    private static string _sharedDirectory = "";

    /// <summary>
    /// 앱이 함께 쓰는 한 벌. 여러 창이 소리를 내므로 파일을 창마다 다시 풀지 않는다.
    /// 게임 폴더가 바뀌면 그때 다시 연다.
    /// </summary>
    public static SoundBank? Shared(string gameDirectory)
    {
        if (string.IsNullOrEmpty(gameDirectory)) return null;
        if (_shared != null && _sharedDirectory == gameDirectory) return _shared;

        _shared?.Dispose();
        _sharedDirectory = gameDirectory;
        _shared = Open(gameDirectory);
        if (_shared == null)
            System.Diagnostics.Debug.WriteLine($"[SoundBank] 효과음 없음: {LastError}");
        return _shared;
    }

    /// <summary>
    /// 효과음 하나를 낸다. 못 풀거나 못 틀면 조용히 넘어간다 — 소리 때문에 놀이가 멎을 일은 없다.
    /// </summary>
    public void Play(int part)
    {
        if (!GameSettings.SfxEnabled) return;
        try
        {
            var wav = _bank.Wav(part);
            if (wav == null) return;

            // SoundPlayer 는 스트림을 물고 있으므로 틀 때마다 새로 잡아 넘긴다.
            _player.Stream = new MemoryStream(Scaled(wav, GameSettings.SfxVolume));
            _player.Play();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SoundBank] 파트 {part} 를 틀지 못했습니다 — {ex.Message}");
        }
    }

    /// <summary>
    /// 소리를 그 크기로 줄인 벌을 낸다 — <see cref="SoundPlayer"/> 에는 크기 손잡이가 없다.
    /// </summary>
    /// <remarks>
    /// 게임 효과음은 22kHz <b>8비트 부호 없는</b> 소리라 128 이 무음이다. 그 자리를 밑삼아
    /// 폭만 줄이면 된다. WAV 머리(44바이트)는 그대로 두고 소리 알맹이만 손댄다.
    /// </remarks>
    private static byte[] Scaled(byte[] wav, int volume)
    {
        if (volume >= GameSettings.MaxVolume) return wav;
        if (wav.Length <= WavHeader) return wav;

        var made = (byte[])wav.Clone();
        for (int i = WavHeader; i < made.Length; i++)
            made[i] = (byte)(Silence + (made[i] - Silence) * volume / GameSettings.MaxVolume);
        return made;
    }

    /// <summary>WAV 머리 길이와 8비트 소리의 무음 자리.</summary>
    private const int WavHeader = 44, Silence = 128;

    public void Dispose() => _player.Dispose();
}
