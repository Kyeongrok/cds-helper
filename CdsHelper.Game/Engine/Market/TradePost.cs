using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Market;

/// <summary>
/// 교역소 매매 규칙 — 무엇을 파는지, 얼마인지, 사고팔면 무엇이 바뀌는지.
/// </summary>
/// <remarks>
/// 게임 교역 대화(<c>0x00481580</c>)가 두 목록을 만든다.
/// <code>
///   0x480CC0  판매 목록(도시가 파는 것) — 지역 공통품 · 제 특산품 · 연결 내륙도시 특산품
///   0x481070  매각 목록(내 짐)          — 짐 여덟 칸 그대로, 품목 제한 없음
///   0x480890  매각가(종류) — 구입 단가는 그 3/2
///   0x481430  거래 성립 — 팔고 사고, 재고를 빼고, 오간 돈만큼 시세가 움직인다
///   0x4811E0  흥정 — 한 번에 90%, 성공 셋째면 그대로 성립, 실패 둘째면 결렬
/// </code>
/// 창(<see cref="UI.Views.TradePostDialog"/>)은 cds95-mod 의 MarketUtilKR 매매 창을 옮긴 것이고,
/// 규칙은 모두 여기서만 정한다.
///
/// <b>아직 안 옮긴 것</b> — 향신료(인도·향료제도 발견)와 커피·담배·카카오·차(신대륙 발견)의
/// 발견 뒤 햇수 보정(<c>0x004805D0</c> · <c>0x004807E0</c>), 도시상태 보정(<c>0x00480290</c>,
/// 우리 도시는 늘 통상이다), 매달 시세가 흔들리는 것, 판매 게이트를 켜는 발견 대본 명령.
/// </remarks>
public sealed class TradePost
{
    /// <summary>판매 목록 한 줄.</summary>
    /// <param name="Kind">교역품 종류(0~69).</param>
    /// <param name="Origin">원산지 도시. 공통품과 제 특산품은 이 도시, 수입품은 내륙도시다 — 재고도 여기서 뺀다.</param>
    /// <param name="Cell">재고 칸. 공통품 0~4, 특산품 <see cref="SpecialCell"/>.</param>
    /// <param name="Price">구입 단가.</param>
    /// <param name="Supply">공급량(남은 재고).</param>
    public readonly record struct Row(int Kind, int Origin, int Cell, int Price, int Supply);

    /// <summary>재고 칸 가운데 특산품 칸과, 재고를 적은 달 칸.</summary>
    public const int SpecialCell = 5, MonthCell = 6;

    /// <summary>재고 한도 표(<c>0x0053CE40</c>) — 공통품은 규모로, 특산품은 도시 표 <c>+0x38</c> 로 고른다.</summary>
    private static readonly int[] StockCaps = [20, 50, 100, 200, 350, 500, 700, 1000];

    /// <summary>귀금속(금·은) 분류 — 시세를 안 곱한다.</summary>
    private const int PreciousCategory = 2;

    /// <summary>시세가 움직일 수 있는 폭(<c>0x00481430</c>).</summary>
    private const int MinRate = 1, MaxRate = 250, MaxRateStep = 50;

    /// <summary>흥정 — 한 번 깎일 때마다 이만큼(%)이 된다(<c>0x00481287</c>).</summary>
    public const int BargainPct = 90;

    /// <summary>성공이 이만큼 쌓이면 더 못 깎고 그대로 산다(<c>0x004812D1</c>).</summary>
    public const int BargainWins = 3;

    /// <summary>실패가 이만큼이면 거래가 깨진다(<c>0x004812F6</c>).</summary>
    public const int BargainLosses = 2;

    /// <summary>흥정 메뉴가 뜨려면 그 나라 말이 이 자리 이상이어야 한다(<c>0x00481400</c>).</summary>
    private const int BargainTongue = 2;

    /// <summary>공급량 벌칙(%) — 결렬 · 세 번 넘게 걸고 결렬 · 흥정만 걸고 나감(<c>0x00481190</c>).</summary>
    public const int CutBreak = 50, CutHard = 100, CutQuit = 10;

    /// <summary>성공 확률표(<c>0x00569330</c>) — 기능 칸 9(회계) 0~3.</summary>
    private static readonly int[] BargainOdds = [40, 70, 85, 95];

    private readonly TradeTable _table;
    private readonly GoodsTable _goods;
    private readonly MarketRates _rates;
    private readonly CityExeTable? _cities;
    private readonly NationTable? _nations;

    public TradePost(TradeTable table, GoodsTable goods, MarketRates rates,
                     CityExeTable? cities, NationTable? nations)
    {
        _table = table;
        _goods = goods;
        _rates = rates;
        _cities = cities;
        _nations = nations;
    }

    public GoodsTable Goods => _goods;

    // ── 목록 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 그 도시의 특산품(<c>0x0042A030</c>) — 세워져 있고 파는 품목이어야 한다.
    /// </summary>
    /// <remarks>
    /// 게임은 도시 형편 비트 0(아는 도시)도 본다. 그 비트는 항해하다 다가서면 켜지는데 우리 쪽은
    /// 판 첫값만 들고 있어, 그대로 보면 나중에 알게 된 도시의 특산품이 영영 안 나온다. 그래서 뺐다.
    /// </remarks>
    public int SpecialOf(int city)
    {
        int kind = _table.SpecialOf(city);
        if (kind < 0 || !_table.OnSale(kind)) return -1;
        if (_cities is { } rows && (rows.FlagsOf(city) & CityExeTable.UnfoundedBit) != 0) return -1;
        return kind;
    }

    /// <summary>
    /// 그 도시가 파는 것(<c>0x00480CC0</c>). 교역소가 없는 곳이면 빈 목록이다.
    /// </summary>
    /// <remarks>
    /// 지역 공통품에서 <b>그 도시 특산품과 같은 종류는 빠지고 칸도 안 차지한다</b>
    /// (<c>0x0042A160</c>). 판매 게이트에 걸린 품목은 줄만 빠지고 칸은 그대로다(<c>0x00480D5C</c>).
    /// </remarks>
    public List<Row> RowsOf(Player player, int city)
    {
        var rows = new List<Row>();
        int region = _table.RegionOf(city);
        if (region < 0) return rows;

        var stock = StockOf(player, city);
        int special = SpecialOf(city);
        int cell = 0;
        foreach (int kind in _table.CommonOf(region))
        {
            if (kind == special) continue;
            if (cell >= TradeTable.CommonSlots) break;
            if (_table.OnSale(kind))
                rows.Add(new Row(kind, city, cell, BuyPrice(city, kind), stock[cell]));
            cell++;
        }

        AddSpecial(player, city, city, rows);
        foreach (int inland in _table.InlandOf(city)) AddSpecial(player, inland, city, rows);
        return rows;
    }

    private void AddSpecial(Player player, int from, int here, List<Row> rows)
    {
        int kind = SpecialOf(from);
        if (kind < 0) return;
        rows.Add(new Row(kind, from, SpecialCell, BuyPrice(here, kind), StockOf(player, from)[SpecialCell]));
    }

    /// <summary>그 도시에 교역소 물건이 하나라도 있는지.</summary>
    public bool HasGoods(Player player, int city) => RowsOf(player, city).Count > 0;

    // ── 값 ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 그 도시에서 그 교역품을 팔 때 받는 값(<c>0x00480890</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   기준가 = 교역품 표[지역]
    ///   그 도시 특산품이면        기준가 = min(기준가, 도시 특산가)        0x480530
    ///   연결 내륙도시 특산품이면  기준가 = min(기준가, 그 도시 특산가)     0x480560
    ///   금·은(분류 2)             값 = 기준가                             시세를 안 곱한다
    ///   그 밖                     값 = 기준가 x 시세 / 100 (0 이면 1)
    /// </code>
    /// cds95-mod 가 리스본에서 대 본 값: 시세 130 · 대포 155 → 매각 201 · 구입 301.
    /// </remarks>
    public int SellPrice(int city, int kind)
    {
        int basis = _table.BasePrice(_table.RegionOf(city), kind);
        if (SpecialOf(city) == kind) basis = Math.Min(basis, _table.SpecialPriceOf(city));
        foreach (int inland in _table.InlandOf(city))
            if (SpecialOf(inland) == kind)
            {
                basis = Math.Min(basis, _table.SpecialPriceOf(inland));
                break;
            }

        int price = _goods.Find(kind)?.Category == PreciousCategory
            ? basis
            : (int)((long)basis * _rates.Of(city) / MarketRates.Par);
        return Math.Max(1, price);
    }

    /// <summary>구입 단가 — 매각가의 3/2.</summary>
    public int BuyPrice(int city, int kind) => SellPrice(city, kind) * 3 / 2;

    /// <summary>한 개 무게.</summary>
    public int WeightOf(int kind) => _goods.Find(kind)?.Weight ?? 0;

    /// <summary>교역품 이름.</summary>
    public string NameOf(int kind) => _goods.Find(kind)?.Name ?? "?";

    // ── 재고 ────────────────────────────────────────────────────────────────

    /// <summary>재고를 적은 달을 가리는 값.</summary>
    private static int MonthKey(DateTime date) => date.Year * 12 + date.Month;

    /// <summary>
    /// 그 도시 재고 칸. 이번 달에 손대지 않은 도시는 한도까지 차 있다 —
    /// 게임은 매달 1일에 재고를 통째로 한도로 되돌린다(<c>0x0042A280</c>).
    /// </summary>
    public int[] StockOf(Player player, int city) =>
        player.TradeStock.TryGetValue(city, out var cells) && cells[MonthCell] == MonthKey(player.Date)
            ? [.. cells]
            : FullStock(player, city);

    /// <summary>한도까지 찬 재고 — 공통품은 규모로, 특산품은 등급으로 표를 탄다(<c>0x004299A0</c>).</summary>
    private int[] FullStock(Player player, int city)
    {
        var cells = new int[Player.TradeStockCells];
        int common = Cap(_cities?.ScaleOf(city) ?? 0);
        for (int i = 0; i < TradeTable.CommonSlots; i++) cells[i] = common;
        cells[SpecialCell] = _table.SpecialOf(city) >= 0 ? Cap(_table.SpecialGradeOf(city)) : 0;
        cells[MonthCell] = MonthKey(player.Date);
        return cells;
    }

    private static int Cap(int index) => StockCaps[Math.Clamp(index, 0, StockCaps.Length - 1)];

    /// <summary>그 도시 재고 칸에서 뺀다(<c>0x00481100</c>). 0 밑으로는 안 내려간다.</summary>
    private void Take(Player player, int city, int cell, int count)
    {
        var cells = StockOf(player, city);
        cells[cell] = Math.Max(0, cells[cell] - count);
        player.SetTradeStock(city, cells);
    }

    // ── 사고팔기 ─────────────────────────────────────────────────────────────

    /// <summary>한 번의 [결정] 이 할 일.</summary>
    /// <param name="Buys">살 줄과 수량.</param>
    /// <param name="Sells">팔 짐 칸과 수량.</param>
    /// <param name="Pct">흥정으로 깎인 값(%). 100 이면 제값.</param>
    public sealed record Deal(IReadOnlyList<(Row Row, int Count)> Buys,
                              IReadOnlyList<(int Slot, int Count)> Sells, int Pct = 100);

    public enum Outcome { Ok, Nothing, NotEnoughGold, HoldFull, TooHeavy, NoSlot, NoSupply }

    /// <summary>흥정이 걸린 단가. 1 닢 밑으로는 안 내려간다.</summary>
    public static int Discounted(int price, int pct) =>
        price <= 0 ? price : Math.Max(1, price * pct / 100);

    /// <summary>살 것의 총액.</summary>
    public static int CostOf(Deal deal) => deal.Buys.Sum(b => b.Count * Discounted(b.Row.Price, deal.Pct));

    /// <summary>팔 것의 총액 — 매각가는 원산지와 상관없이 이 도시 값이다.</summary>
    public int GainOf(Player player, int city, Deal deal) =>
        deal.Sells.Sum(s => s.Slot >= 0 && s.Slot < player.CargoHold.Count
                                ? s.Count * SellPrice(city, player.CargoHold[s.Slot].Kind) : 0);

    /// <summary>
    /// 거래를 따져 본다 — 먼저 팔고(번 돈으로 산다) 그 다음 산다. 아무것도 바꾸지 않는다.
    /// </summary>
    public Outcome Check(Player player, int city, Deal deal)
    {
        if (deal.Buys.All(b => b.Count <= 0) && deal.Sells.All(s => s.Count <= 0)) return Outcome.Nothing;
        if (deal.Buys.Any(b => b.Count > b.Row.Supply)) return Outcome.NoSupply;
        if ((long)player.Gold + GainOf(player, city, deal) < CostOf(deal)) return Outcome.NotEnoughGold;

        int count = player.LoadedBarrels, weight = player.LoadedWeight;
        var kinds = player.CargoHold.Select(c => (c.Kind, c.Origin, c.Count)).ToList();
        foreach (var (slot, n) in deal.Sells)
        {
            if (slot < 0 || slot >= kinds.Count || n <= 0) continue;
            count -= n;
            weight -= n * player.CargoHold[slot].UnitWeight;
            kinds[slot] = kinds[slot] with { Count = kinds[slot].Count - n };
        }
        kinds.RemoveAll(k => k.Count <= 0);
        foreach (var (row, n) in deal.Buys)
        {
            if (n <= 0) continue;
            count += n;
            weight += n * WeightOf(row.Kind);
            if (!kinds.Any(k => k.Kind == row.Kind && k.Origin == row.Origin))
                kinds.Add((row.Kind, row.Origin, n));
        }
        if (kinds.Count > Player.CargoSlots) return Outcome.NoSlot;
        if (count > player.Capacity) return Outcome.HoldFull;
        if (weight > player.Tonnage) return Outcome.TooHeavy;
        return Outcome.Ok;
    }

    /// <summary>거래를 한다(<c>0x00481430</c>). 따져 보아 안 되면 아무것도 안 바꾼다.</summary>
    /// <remarks>
    /// 판 것은 그 도시 재고에 <b>안 더한다</b> — 게임도 그렇다. 대신 오간 돈이 시세를 민다.
    /// <code>
    ///   순지출 = 산 돈 - 번 돈
    ///   시세  += clamp(순지출 / (200 x (5 x 규모 + 10)), -50, 50)      ; 1~250
    /// </code>
    /// 사면 오르고 팔면 내린다 — 규모 4 인 리스본은 순지출 6만 닢에 10 오른다.
    /// </remarks>
    public Outcome Apply(Player player, int city, Deal deal)
    {
        var why = Check(player, city, deal);
        if (why != Outcome.Ok) return why;

        int gain = GainOf(player, city, deal);
        // 뒤 칸부터 판다 — 다 판 칸이 빠지며 당겨져도 앞 번호가 안 어긋난다.
        foreach (var (slot, n) in deal.Sells.Where(s => s.Count > 0).OrderByDescending(s => s.Slot))
            player.UnloadCargo(slot, n);
        player.Earn(gain);

        int cost = CostOf(deal);
        foreach (var (row, n) in deal.Buys)
        {
            if (n <= 0) continue;
            player.LoadCargo(row.Kind, n, row.Origin, WeightOf(row.Kind));
            Take(player, row.Origin, row.Cell, n);
        }
        player.SetGold(player.Gold - cost);

        int scale = _cities?.ScaleOf(city) ?? 0;
        int step = Math.Clamp((cost - gain) / (200 * (5 * scale + 10)), -MaxRateStep, MaxRateStep);
        if (step != 0) _rates.Set(city, Math.Clamp(_rates.Of(city) + step, MinRate, MaxRate));
        return Outcome.Ok;
    }

    // ── 흥정 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 흥정 메뉴가 뜨는지(<c>0x00481400</c>) — 값이 있고, 그 도시를 가진 <b>나라의 말</b>이 2 이상.
    /// </summary>
    /// <remarks>
    /// 게임은 주인공과 동승 인물 가운데 가장 높은 자리를 본다(<c>0x00468FE0</c>). 우리 부하 자료에는
    /// 언어가 없어 주인공 것만 본다.
    /// </remarks>
    public bool CanBargain(Player player, int city, int cost)
    {
        if (cost <= 0 || _cities == null || _nations?.Find(_cities.NationOf(city)) is not { } nation)
            return false;
        int language = nation.Language;
        return language >= 0 && language < Skill.Languages.Length
               && player.TongueOf(Skill.Languages[language]) >= BargainTongue;
    }

    /// <summary>한 번 깎아 본다(<c>0x00481330</c>) — 회계 자리로 확률표를 고른다.</summary>
    public static bool RollBargain(Player player, Random random)
    {
        int level = Math.Clamp(player.LevelOf(Skill.Names[Skill.Accounting]), 0, BargainOdds.Length - 1);
        return random.Next(100) < BargainOdds[level];
    }

    /// <summary>상인 대사(<c>0x00481380</c>). 성공 대사만 깎인 값을 받는다.</summary>
    public static string BargainLine(bool ok, int tries, int price) =>
        (ok, Math.Clamp(tries, 0, 2)) switch
        {
            (true, 0) => $"으음, 그래 좋소. 금화 {price}닢에 타협해 봅시다.",
            (true, 1) => $"어쩔 수 없군. 금화 {price}닢에 어떤가?",
            (true, _) => $"카-앗. 곤란하군! 금화 {price}닢! 이 이상은 무리라네!",
            (false, 0) => "무리한 얘기다. 이 가격에 봐 주게.",
            (false, 1) => "이쪽은 바쁘다네. 살 마음이 없으면 돌아가게.",
            _ => "구두쇠에게는 팔 마음 없네! 두번 다시 오지 말게!",
        };

    /// <summary>흥정만 걸고 안 사고 나갈 때(<c>0x005328C0</c>).</summary>
    public const string QuitLine = "날 바보 취급하는 건가? 살 건지 안 살 건지 빨리하게!";

    /// <summary>
    /// 상인이 물건을 거둬 간다(<c>0x00481190</c>) — 목록 줄마다 공급량의 pct% 를 그 도시 재고에서 뺀다.
    /// </summary>
    public void CutSupply(Player player, int city, int pct)
    {
        foreach (var row in RowsOf(player, city))
        {
            int cut = row.Supply * pct / 100;
            if (cut > 0) Take(player, row.Origin, row.Cell, cut);
        }
    }
}
