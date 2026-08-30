using System.Diagnostics;
using CdsHelper.Game.Engine.Discovery;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;
using Prism.Ioc;

namespace CdsHelper.Game.Engine;

/// <summary>
/// 한 판. 게임 폴더와 거기서 읽어 온 표들, 제독, 주사위, 소리를 한자리에 든다.
/// </summary>
/// <remarks>
/// 화면(<see cref="UI.Views.ShipMapWindow"/> · <see cref="UI.Views.CityPicView"/>)들은 이것을
/// 받아 쓴다. 예전에는 화면마다 제 표를 열어, 이를테면 힌트 표가 네 군데서 따로 열렸다.
///
/// 표는 <b>처음 쓸 때</b> 연다 — 도시 그림만 20MB 라 미리 읽으면 첫 화면이 늦다.
/// 한 번 못 열면 다시 찾지 않는다(파일이 없는 폴더에서 틱마다 뒤지지 않게).
/// </remarks>
public sealed class Game
{
    /// <summary>게임 폴더. 아직 모르면 빈 문자열이다.</summary>
    public string Directory { get; private set; } = "";

    /// <summary>주인공 — 소지금과 가진 배. 조선소에서 배를 사면 여기서 돈이 빠진다.</summary>
    public Player Player { get; private set; } = new();

    /// <summary>
    /// 주인공을 새로 앉힌다 — <b>NEW GAME</b> 이 부른다.
    /// </summary>
    /// <remarks>
    /// 한 판을 하고 첫 화면으로 돌아온 뒤 다시 시작하면 <b>앞 판이 그대로 묻어 온다</b> —
    /// 소지금·날짜·배·소지품·발견물이 다 남는다. 판 하나를 통째로 갈아 끼우는 것이
    /// 칸을 하나씩 되돌리는 것보다 확실하다.
    ///
    /// 창들은 <c>_game.Player</c> 를 그때그때 물어보므로 갈아 끼워도 따라온다.
    /// </remarks>
    /// <returns>앉아 있던 주인공. 물러나면 <see cref="UsePlayer"/> 로 도로 앉힌다.</returns>
    public Player NewPlayer()
    {
        var before = Player;
        Player = new Player();
        return before;
    }

    /// <summary>주인공을 도로 앉힌다 — 새 놀이를 짓다 말고 물러났을 때다.</summary>
    public void UsePlayer(Player player) => Player = player;

    /// <summary>바다 사건 주사위.</summary>
    public Random Random { get; } = new();

    /// <summary>배경음악. 폴더를 잡을 때 그 폴더를 함께 일러 준다.</summary>
    public BgmPlayer Bgm { get; } = new();

    /// <summary>
    /// 게임 폴더를 잡는다. 폴더가 갈리면 열어 둔 표를 잊는다 — 다음에 쓸 때 새 폴더에서 연다.
    /// </summary>
    public void SetDirectory(string directory)
    {
        if (Directory == directory) return;

        Directory = directory;
        _cityPics = null; _cityPicsTried = false;
        _buildings = null; _buildingsTried = false;
        _books = null; _booksTried = false;
        _hints = null; _hintsTried = false;
        _sponsors = null; _sponsorsTried = false;
        _items = null; _itemsTried = false;
        _sails = null; _sailsTried = false;
        _speakers = null; _speakersTried = false;
        _nations = null; _nationsTried = false;
        _goods = null; _goodsTried = false;
        _cityRows = null; _cityRowsTried = false;
        _faces = null; _facesTried = false;
        _effects = null; _effectsTried = false;
        _guests = null; _guestsTried = false;
        _photos = null; _photosTried = false;
        _itemText = null; _itemTextTried = false;
        _itemArt = null;
        _discoveries = null; _discoveriesTried = false;
        _stills = null; _stillsTried = false;
        _fighters = null; _fightersTried = false;
        _barmaids = null; _barmaidsTried = false;

        Bgm.SetGameDirectory(directory);
    }

    /// <summary>효과음. 한 벌만 두고 나눠 쓴다(<see cref="SoundBank.Shared"/> 가 들고 있다).</summary>
    public SoundBank? Sfx => Directory.Length == 0 ? null : SoundBank.Shared(Directory);

    /// <summary>도시 표. 이름·문화권이 여기서 온다 — 게임 폴더가 아니라 우리 DB 것이다.</summary>
    public CityTable CityTable => _cities ??= Local.Helpers.CityTable.Open();

    /// <summary>도시 그림(CITYCG.CDS). 20MB 라 입항을 처음 할 때에야 연다.</summary>
    public CityPictures? CityPics =>
        Once(ref _cityPics, ref _cityPicsTried, CityPictures.Open,
             () => CityPictures.LastError, "도시 그림");

    /// <summary>건물 표(CDS_95.EXE). 건물 자리·이름·가르치는 기능이 여기서 온다.</summary>
    public CityBuildingTable? Buildings =>
        Once(ref _buildings, ref _buildingsTried, CityBuildingTable.Open,
             () => CityBuildingTable.LastError, "건물 표");

    /// <summary>책 표(CDS_95.EXE). 도서관 서가를 채운다.</summary>
    public BookTable? Books =>
        Once(ref _books, ref _booksTried, BookTable.Open, () => BookTable.LastError, "책 표");

    /// <summary>힌트 표(CDS_95.EXE). 이름·등급·자금·기한이 여기서 온다.</summary>
    public HintTable? Hints =>
        Once(ref _hints, ref _hintsTried, HintTable.Open, () => HintTable.LastError, "힌트 표");

    /// <summary>후원자 표(CDS_95.EXE). 어느 자리에 누가 앉았는지와 얼굴 번호가 여기서 온다.</summary>
    public SponsorTable? Sponsors =>
        Once(ref _sponsors, ref _sponsorsTried, SponsorTable.Open,
             () => SponsorTable.LastError, "후원자 표");

    /// <summary>아이템 표(CDS_95.EXE). 발견물이 주는 물건 이름을 여기서 얻는다.</summary>
    public ItemTable? Items =>
        Once(ref _items, ref _itemsTried, ItemTable.Open, () => ItemTable.LastError, "아이템 표");

    /// <summary>나라 표(CDS_95.EXE). 도시 정보 창의 나라 칸에 쓴다.</summary>
    public NationTable? Nations =>
        Once(ref _nations, ref _nationsTried, NationTable.Open,
             () => NationTable.LastError, "나라 표");

    /// <summary>교역품 표(CDS_95.EXE). 도시 특산품을 낼 때 쓴다.</summary>
    public GoodsTable? Goods =>
        Once(ref _goods, ref _goodsTried, GoodsTable.Open, () => GoodsTable.LastError, "교역품 표");

    /// <summary>EXE 도시 표(문화권·시장 물건). 시장과 여관이 같이 쓴다.</summary>
    public CityExeTable? CityRows =>
        Once(ref _cityRows, ref _cityRowsTried, CityExeTable.Open,
             () => CityExeTable.LastError, "EXE 도시 표");

    /// <summary>
    /// 시설 화자표(CDS_95.EXE). 어느 건물에서 누가 말을 거는지 — 문화권마다 다르다.
    /// </summary>
    public SpeakerFaceTable? Speakers =>
        Once(ref _speakers, ref _speakersTried, SpeakerFaceTable.Open,
             () => SpeakerFaceTable.LastError, "화자표");

    /// <summary>돛 효율표(CDS_95.EXE). 배 속도를 잴 때 쓴다.</summary>
    public SailTable? Sails =>
        Once(ref _sails, ref _sailsTried, SailTable.Open, () => SailTable.LastError, "돛 효율표");

    /// <summary>
    /// 발견물. 배가 어디에 서면 무엇이 발견되는지가 여기서 온다.
    /// </summary>
    /// <remarks>힌트 표는 없어도 연다 — 그때는 힌트로 열리는 것만 안 뜬다.</remarks>
    public DiscoveryLog? Discoveries
    {
        get
        {
            if (_discoveries != null || _discoveriesTried) return _discoveries;
            _discoveriesTried = true;
            if (Directory.Length == 0) return null;

            var table = DiscoveryTable.Open(Directory);
            if (table == null)
            {
                Debug.WriteLine($"[Game] 발견물 표 없음: {DiscoveryTable.LastError}");
                return null;
            }
            return _discoveries = new DiscoveryLog(table, Hints);
        }
    }

    /// <summary>여급 표 — 술집에 서는 127명. 궁합이 여기서 나온다.</summary>
    public BarmaidTable? Barmaids =>
        Once(ref _barmaids, ref _barmaidsTried, BarmaidTable.Open,
             () => BarmaidTable.LastError, "여급 표");

    /// <summary>발견했을 때 뜨는 그림(DSTILL.CDS). 못 읽으면 글만 낸다.</summary>
    /// <summary>일기토 그림(FIGHTER.CDS) — 제독 한 벌과 상대 여덟 벌.</summary>
    public FighterSprites? Fighters =>
        Once(ref _fighters, ref _fightersTried, FighterSprites.Open,
             () => FighterSprites.LastError, "일기토 그림");

    public DiscoveryStills? Stills =>
        Once(ref _stills, ref _stillsTried, DiscoveryStills.Open,
             () => DiscoveryStills.LastError, "발견물 그림");

    /// <summary>화면에 겹쳐 도는 동그란 애니메이션(MPEFFECT.CDS).</summary>
    public EffectAnim? Effects =>
        Once(ref _effects, ref _effectsTried, EffectAnim.Open,
             () => EffectAnim.LastError, "애니메이션");

    /// <summary>술집 손님 그림. 못 읽으면 손님만 안 선다.</summary>
    public TavernGuests? Guests =>
        Once(ref _guests, ref _guestsTried, TavernGuests.Open,
             () => TavernGuests.LastError, "손님 그림");

    /// <summary>건물 사진(MPCG.CDS). 건물에 들어갈 때 뜨는 타원 사진이다.</summary>
    public BuildingPhoto? Photos =>
        Once(ref _photos, ref _photosTried, BuildingPhoto.Open,
             () => BuildingPhoto.LastError, "건물 사진");

    /// <summary>아이템 설명문. 없으면 설명 자리가 빈 채로 뜬다.</summary>
    public ItemDescriptions? ItemText =>
        Once(ref _itemText, ref _itemTextTried, ItemDescriptions.Open,
             () => ItemDescriptions.LastError, "아이템 설명문");

    /// <summary>아이템 그림. asset/item 만 있으면 게임 폴더가 없어도 나온다.</summary>
    public ItemArt? ItemPictures => _itemArt ??= ItemArt.Open(Directory);

    /// <summary>
    /// 초상화(MALE.CDS · FEMALE.CDS). 게임 폴더를 몰라도 연다 — 우리 asset 폴더에도 있다.
    /// </summary>
    public Portraits? Faces
    {
        get
        {
            if (_faces != null || _facesTried) return _faces;
            _facesTried = true;

            _faces = Portraits.Open(Directory);
            if (_faces == null) Debug.WriteLine($"[Game] 초상화 없음: {Portraits.LastError}");
            return _faces;
        }
    }

    /// <summary>
    /// 게임 세이브의 인물표 — 술집에 앉은 사람과 부하의 신상이 여기서 온다.
    /// </summary>
    /// <remarks>
    /// 이것만은 게임 폴더가 아니라 <b>세이브 파일</b>에서 읽는다. 사람은 판마다 다르다.
    /// </remarks>
    public TavernRoster? Roster
    {
        get
        {
            if (_roster != null || _rosterTried) return _roster;
            _rosterTried = true;

            string? path = AppSettings.LastSaveFilePath;
            if (string.IsNullOrEmpty(path)) return null;

            _roster = TavernRoster.Open(path);
            if (_roster == null) Debug.WriteLine($"[Game] 술집 인물 없음: {TavernRoster.LastError}");
            return _roster;
        }
    }

    /// <summary>
    /// 그 건물에서 말을 거는 사람의 얼굴. 없으면 null 이다.
    /// </summary>
    /// <remarks>
    /// 화자표(<see cref="Speakers"/>)에서 번호를 집어 초상화를 푼다. 게임도 시설에
    /// 들어설 때 <c>[건물코드][문화권]</c> 으로 한 번 집어 시설 객체에 넣어 둔다
    /// (<c>0x004A2500</c>).
    /// </remarks>
    /// <param name="buildingCode">건물 코드(항구 0 · 조선소 6 · 도서관 8 …).</param>
    /// <param name="culture">그 마을 문화권 번호.</param>
    public uint[]? SpeakerFace(int buildingCode, int culture)
    {
        if (Speakers is not { } speakers) return null;

        int face = speakers.FaceOf(buildingCode, culture);
        // 여관 주인은 여자다 — 표의 성별 칸이 어느 CDS 에서 꺼낼지 일러 준다.
        return face < 0 ? null : Faces?.TryGetBgra(face, speakers.IsFemale(buildingCode));
    }

    /// <summary>
    /// 그 부하의 신상. 우리 세이브에 적어 둔 것을 먼저 보고, 없으면(판 20 앞에 들인 부하)
    /// 게임 세이브의 인물표에서 채워 <b>그 자리에서 적어 둔다</b> — 한 번 채우면 다음부터는
    /// 우리 것만으로 뜬다.
    /// </summary>
    public Player.MateInfo? MateInfo(string name)
    {
        if (Player.MateInfoOf(name) is { } mine) return mine;
        if (Roster?.Find(name) is not { } person) return null;

        var filled = Town.Tavern.MateInfoOf(person);
        Player.RememberMate(filled);
        return filled;
    }

    /// <summary>
    /// 힌트 이름. 게임 표를 읽었으면 그것으로, 아니면 우리 DB 것으로, 그것도 없으면 번호로 낸다.
    /// </summary>
    public string HintName(int id)
    {
        if (Hints?.Find(id)?.Name is { Length: > 0 } name) return name;

        if (_hintNames == null)
        {
            _hintNames = [];
            try
            {
                var service = ContainerLocator.Container.Resolve<HintService>();
                service.InitializeAsync(System.IO.Path.Combine(AppContext.BaseDirectory,
                                                               "cdshelper.db")).Wait();
                _hintNames = service.GetAllHintNames();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Game] 힌트 이름 로드 실패: {ex.Message}");
            }
        }
        return _hintNames.TryGetValue(id, out var found) && found.Length > 0 ? found : $"힌트 {id}";
    }

    /// <summary>그 도시의 문화권("이슬람" · "북유럽" …). 모르면 빈 문자열.</summary>
    /// <remarks>
    /// 이름은 앱 DB 에서 나온다. 도구 창에서 손으로 갈아 둔 것이 있으면 그 번호의
    /// 이름으로 낸다(<see cref="CityCultureEdits"/>) — 건물 사진과 술집 손님이 번호가
    /// 아니라 <b>이름</b>으로 갈리기 때문에, 여기까지 따라오지 않으면 얼굴만 바뀌고
    /// 사진은 그대로인 어정쩡한 마을이 된다.
    /// </remarks>
    public string CultureOf(int city)
    {
        int changed = CityCultureEdits.Of(city);
        return changed == CityCultureEdits.None
            ? CityTable.CultureOf(city)
            : CityCultureEdits.NameOf(changed);
    }

    /// <summary>그 도시의 이름. 표에 없으면 번호로 물러선다.</summary>
    public string CityName(int city) => CityTable.NameOf(city);

    /// <summary>
    /// 화면을 닫을 때 부른다 — 소리를 끄고, 20MB 짜리 도시 그림을 놓는다.
    /// </summary>
    public void Close()
    {
        Bgm.Dispose();
        _cityPics = null;
        _cityPicsTried = false;
    }

    /// <summary>지금 판을 적는다. 적히는 자리는 <see cref="GameSave"/> 참고.</summary>
    public string Save() => GameSave.Save(Player);

    /// <summary>
    /// 표를 처음 쓸 때 한 번만 연다. 폴더를 모르거나 못 열면 <c>null</c> 인 채로 둔다.
    /// </summary>
    private T? Once<T>(ref T? slot, ref bool tried, Func<string, T?> open,
                       Func<string> lastError, string what) where T : class
    {
        if (slot != null || tried) return slot;
        tried = true;
        if (Directory.Length == 0) return null;

        slot = open(Directory);
        if (slot == null) Debug.WriteLine($"[Game] {what} 없음: {lastError()}");
        return slot;
    }

    private CityTable? _cities;
    private CityPictures? _cityPics;
    private CityBuildingTable? _buildings;
    private BookTable? _books;
    private HintTable? _hints;
    private SponsorTable? _sponsors;
    private ItemTable? _items;
    private SailTable? _sails;
    private SpeakerFaceTable? _speakers;
    private NationTable? _nations;
    private GoodsTable? _goods;
    private CityExeTable? _cityRows;
    private DiscoveryLog? _discoveries;
    private FighterSprites? _fighters;
    private bool _fightersTried;
    private DiscoveryStills? _stills;
    private bool _stillsTried;
    private BarmaidTable? _barmaids;
    private bool _barmaidsTried;
    private TavernRoster? _roster;
    private Portraits? _faces;
    private EffectAnim? _effects;
    private TavernGuests? _guests;
    private BuildingPhoto? _photos;
    private ItemDescriptions? _itemText;
    private ItemArt? _itemArt;
    private Dictionary<int, string>? _hintNames;

    private bool _cityPicsTried, _buildingsTried, _booksTried, _hintsTried;
    private bool _sponsorsTried, _itemsTried, _sailsTried, _discoveriesTried;
    private bool _nationsTried, _goodsTried, _cityRowsTried, _facesTried, _speakersTried;
    private bool _effectsTried, _guestsTried, _photosTried, _itemTextTried, _rosterTried;
}
