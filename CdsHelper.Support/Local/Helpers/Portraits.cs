using System.IO;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 게임 폴더의 MALE.CDS · FEMALE.CDS — 인물 초상화. 대사 창에 얼굴을 띄울 때 쓴다.
/// </summary>
/// <remarks>
/// 도시 그림과 같은 LS12 아카이브인데 훨씬 단순하다 — 파트 하나가 곧 얼굴 한 장이고,
/// 모두 정확히 7680바이트다.
/// <code>
///   파트 하나  7680바이트 = 80 x 96, 8bpp 색인, 위에서 아래로
///   MALE.CDS   414장 · FEMALE.CDS 144장
/// </code>
/// <b>제 팔레트가 없다.</b> 쓰이는 색인이 0~73 뿐이라 <see cref="GamePalette"/> 공용 색표만으로
/// 다 그려진다 — 그래서 도시 그림과 달리 팔레트 파트가 붙어 있지 않다.
///
/// 어느 얼굴을 쓰는지는 인물 표에 적혀 있다(<see cref="SponsorTable"/>).
/// </remarks>
public sealed class Portraits
{
    public const int Width = 80;
    public const int Height = 96;
    private const int Pixels = Width * Height;

    private readonly Ls12Reader _male;
    private readonly Ls12Reader _female;

    private Portraits(Ls12Reader male, Ls12Reader female)
    {
        _male = male;
        _female = female;
    }

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>남자 얼굴 장수.</summary>
    public int MaleCount => _male.PartCount;

    /// <summary>여자 얼굴 장수.</summary>
    public int FemaleCount => _female.PartCount;

    /// <summary>게임 폴더의 초상화 두 벌을 연다. 하나라도 없으면 null.</summary>
    public static Portraits? Open(string gameDirectory)
    {
        LastError = "";
        var male = OpenOne(gameDirectory, "MALE.CDS");
        if (male == null) return null;
        var female = OpenOne(gameDirectory, "FEMALE.CDS");
        if (female == null) return null;
        return new Portraits(male, female);
    }

    private static Ls12Reader? OpenOne(string gameDirectory, string file)
    {
        var path = Path.Combine(gameDirectory, file);
        if (!File.Exists(path)) { LastError = $"{path} 가 없습니다"; return null; }

        var archive = Ls12Reader.Open(path);
        if (archive == null) { LastError = $"{path} 를 읽지 못했습니다"; return null; }
        if (archive.PartCount == 0) { LastError = $"{file} 에 얼굴이 없습니다"; return null; }
        return archive;
    }

    /// <summary>
    /// 얼굴 한 장을 80x96 BGRA 로 푼다. 없는 번호이거나 못 풀면 null.
    /// </summary>
    /// <param name="face">얼굴 번호(인물 표의 값).</param>
    /// <param name="female">여자 얼굴이면 true.</param>
    public uint[]? TryGetBgra(int face, bool female)
    {
        var archive = female ? _female : _male;
        if (face < 0 || face >= archive.PartCount) return null;

        var idx = archive.Decode(face);
        if (idx == null || idx.Length < Pixels) return null;

        var bgra = new uint[Pixels];
        for (int i = 0; i < Pixels; i++)
        {
            int c = idx[i] * 3;
            bgra[i] = (uint)(0xFF << 24 | GamePalette.Rgb[c] << 16
                             | GamePalette.Rgb[c + 1] << 8 | GamePalette.Rgb[c + 2]);
        }
        return bgra;
    }
}
