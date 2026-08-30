using System.IO;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 게임 폴더의 MPEFFECT.CDS — 화면에 겹쳐 도는 <b>동그란 애니메이션</b> 여섯 종.
/// </summary>
/// <remarks>
/// 자세한 것은 볼트 <c>22.분석-애니메이션(MPEFFECT·EVANIME)</c> 에 있다.
/// <code>
///   파트 = 애니 x 4 + 프레임(0~3)
///   한 장 6,400바이트 = 80 x 80 8bpp 색인
/// </code>
/// 팔레트 파트가 따로 없다 — 쓰는 색인이 10~74 안이라 <see cref="GamePalette"/> 공용 색표로
/// 다 풀린다. 동그란 테두리도 그림에 같이 그려져 있고, <b>원 바깥은 색인 74</b> 다(비침).
///
/// <list type="table">
///   <item><term>0</term><description>짐 싣기</description></item>
///   <item><term>1</term><description>책상의 서기</description></item>
///   <item><term>2</term><description>대포 발사</description></item>
///   <item><term>3</term><description>하트</description></item>
///   <item><term>4</term><description>동전 던지기</description></item>
///   <item><term>5</term><description>설득 — 무릎 꿇고 청하다가 엎어진다</description></item>
/// </list>
/// </remarks>
public sealed class EffectAnim
{
    /// <summary>한 장의 한 변. 게임이 늘 이 크기로 그린다.</summary>
    public const int Size = 80;
    private const int Pixels = Size * Size;

    /// <summary>애니 하나에 든 프레임 수.</summary>
    public const int FrameCount = 4;

    /// <summary>설득 애니메이션 번호. 후원자 건물의 명성 관문에서 돈다.</summary>
    /// <summary>대포 발사(파트 8~11). 자택 "후손을 남긴다" 가 이 벌을 돌린다.</summary>
    /// <remarks>
    /// 게임의 껍데기가 <c>0x004A6340</c> 이고, 인자가 1 이면 소리 <c>0x2A</c>, 아니면
    /// <c>0x2B</c> 를 함께 낸다 — 여섯 벌 가운데 소리를 넘기는 것은 이것뿐이다.
    /// </remarks>
    public const int Cannon = 2;

    /// <summary>
    /// 하트(파트 12~15) — 커졌다가 깨진다. 껍데기는 <c>0x004A6360</c> 이고 소리는 없다.
    /// </summary>
    /// <remarks>
    /// 후원자를 설득할 때 <b>마음이 동하는지</b>를 이 벌로 낸다 — <c>0x004AE7B7</c> 과
    /// <c>0x004AE815</c> 가 굴림 결과를 그대로 넘긴다.
    /// </remarks>
    public const int Heart = 3;

    public const int Persuade = 5;

    /// <summary>원 바깥(비침) 색인.</summary>
    private const byte Transparent = 74;

    /// <summary>파일 이름이 소문자다 — 대문자로 찾으면 못 찾는다.</summary>
    private const string FileName = "MPEffect.cds";

    private readonly Ls12Reader _archive;

    private EffectAnim(Ls12Reader archive) => _archive = archive;

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>게임 폴더의 MPEFFECT.CDS 를 연다. 없거나 모양이 다르면 null.</summary>
    public static EffectAnim? Open(string gameDirectory)
    {
        LastError = "";

        // 게임 폴더는 대소문자를 안 가리지만(NTFS) 이름을 그대로 적어 둔다.
        var path = Path.Combine(gameDirectory, FileName);
        if (!File.Exists(path)) { LastError = $"{path} 가 없습니다"; return null; }

        var archive = Ls12Reader.Open(path);
        if (archive == null) { LastError = $"{path} 를 읽지 못했습니다"; return null; }
        if (archive.PartCount < 6 * FrameCount)
        {
            LastError = "MPEFFECT.CDS 에 애니메이션이 모자랍니다";
            return null;
        }
        return new EffectAnim(archive);
    }

    /// <summary>
    /// 한 프레임을 80x80 BGRA 로 푼다. 원 바깥은 알파 0 이다. 못 풀면 null.
    /// </summary>
    public uint[]? TryGetBgra(int anim, int frame)
    {
        if (anim < 0 || frame < 0 || frame >= FrameCount) return null;

        var idx = _archive.Decode(anim * FrameCount + frame);
        if (idx == null || idx.Length < Pixels) return null;

        var bgra = new uint[Pixels];
        for (int i = 0; i < Pixels; i++)
        {
            byte v = idx[i];
            if (v == Transparent) continue;
            int k = v * 3;
            bgra[i] = (uint)(0xFF << 24 | GamePalette.Rgb[k] << 16
                             | GamePalette.Rgb[k + 1] << 8 | GamePalette.Rgb[k + 2]);
        }
        return bgra;
    }
}
