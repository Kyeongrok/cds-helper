using System.Runtime.InteropServices;
using CdsHelper.Support.Local.Helpers;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace CdsHelper.Main.UI.Views.D3D;

/// <summary>
/// 세계지도를 Direct3D 11 로 그린다. 칸 하나가 화면 몇 픽셀이든 픽셀 셰이더가 그때
/// OCEAN.CDS 타일에서 점을 뽑으므로 확대해도 원본 그림이 그대로 나온다.
/// </summary>
/// <remarks>
/// CPU 로 그리던 <see cref="WorldMapSurface"/> 와 그림은 같다. 다른 것은 배를 60fps 로
/// 움직일 때다 — 카메라가 배를 따라가면 프레임마다 화면을 다시 그려야 하는데, CPU 는
/// 그때마다 뷰포트 전체를 훑어야 하고 GPU 는 텍스처를 한 번 올려 두고 표본만 뽑으면 된다.
///
/// <para>텍스처 세 장으로 끝난다</para>
/// <list type="bullet">
///   <item>칸 지도 — 2500x1250 R16_UINT. WORLD.CDS 를 펼쳐 칸마다 타일 번호(하위 14비트)만 담는다.</item>
///   <item>타일 그림 — 2048x2048 R8_UINT. 16x16 타일 16,384장을 128x128 격자로 편 것.</item>
///   <item>팔레트 — 256x1 BGRA.</item>
/// </list>
/// 픽셀 셰이더는 화면 점 -> 칸 -> 타일 번호 -> 타일 안 점 -> 팔레트 순으로 짚는다.
/// 가로로 잇는 것(경도 -180/180 넘나들기)은 칸 좌표를 2500 으로 나눈 나머지로 처리한다.
/// </remarks>
public sealed unsafe class MapD3DRenderer : IDisposable
{
    /// <summary>타일 아틀라스 한 변에 들어가는 타일 수(128 x 128 = 16,384).</summary>
    private const int AtlasTiles = 128;
    private const int AtlasSize = AtlasTiles * OceanTiles.TileW;   // 2048

    // 셰이더 본문은 ASCII 로만 적는다. 한글 주석을 넣으면 컴파일이 깨진다 —
    // D3DCompile 이 원본을 cp949 로 받는데, 한글 음절의 끝바이트가 0x5C('\') 인 것이 많아
    // 줄 끝에 오면 줄이음으로 먹혀 다음 줄이 통째로 사라진다. 설명은 여기 바깥에 둔다.
    //
    //   VS  정점 버퍼 없이 삼각형 하나로 화면을 덮는다.
    //   PS  화면 점 -> 칸 좌표 -> 타일 번호 -> 타일 안 점 -> 팔레트 순으로 짚는다.
    //       cell.x 를 2500 으로 나눈 나머지로 접어 경도 -180/180 을 잇는다.
    //       배 그림(SpriteRect)이 있으면 그 자리는 지도 대신 배를 낸다. 색인 0 은 비침이다.
    private const string ShaderSource = """
        Texture2D<uint>   CellMap  : register(t0);
        Texture2D<uint>   Atlas    : register(t1);
        Texture2D<float4> Palette  : register(t2);
        Texture2D<float4> Sprite   : register(t3);
        Texture2D<float4> Avg1     : register(t4);
        Texture2D<float4> Avg2     : register(t5);
        Texture2D<float4> Avg4     : register(t6);

        cbuffer Frame : register(b0)
        {
            float2 OriginCell;
            float2 CellPerPixel;
            float4 SpriteRect;
            float2 MapCells;
            float  Detail;
            float  Pad;
        };

        struct VSOut { float4 pos : SV_Position; };

        VSOut VS(uint id : SV_VertexID)
        {
            VSOut o;
            float2 uv = float2((id << 1) & 2, id & 2);
            o.pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
            return o;
        }

        float4 PS(VSOut i) : SV_Target
        {
            if (SpriteRect.z > 0)
            {
                float2 s = (i.pos.xy - SpriteRect.xy) / SpriteRect.zw;
                if (all(s >= 0) && all(s < 1))
                {
                    float4 c = Sprite.Load(int3(int2(s * 48.0), 0));
                    if (c.a > 0) return c;
                }
            }

            float2 cell = OriginCell + i.pos.xy * CellPerPixel;
            cell.x = cell.x - floor(cell.x / MapCells.x) * MapCells.x;
            cell.y = clamp(cell.y, 0, MapCells.y - 0.001);

            int2 c    = int2(cell);
            uint tile = CellMap.Load(int3(c, 0));
            int2 org  = int2(tile % 128u, tile / 128u);
            float2 f  = frac(cell);

            if (Detail < 0.5) return Avg1.Load(int3(org, 0));
            if (Detail < 1.5) return Avg2.Load(int3(org * 2 + int2(f * 2.0), 0));
            if (Detail < 2.5) return Avg4.Load(int3(org * 4 + int2(f * 4.0), 0));

            uint pal = Atlas.Load(int3(org * 16 + int2(f * 16.0), 0));
            return Palette.Load(int3(int(pal), 0, 0));
        }
        """;

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameCb
    {
        public float OriginCellX, OriginCellY;
        public float CellPerPixelX, CellPerPixelY;
        public float SpriteX, SpriteY, SpriteW, SpriteH;
        public float MapCellsX, MapCellsY;
        public float Detail;
        public float Pad0;
    }

    private ID3D11Device _device = null!;
    private ID3D11DeviceContext _ctx = null!;
    private ID3D11VertexShader _vs = null!;
    private ID3D11PixelShader _ps = null!;
    private ID3D11Buffer _cb = null!;
    private ID3D11ShaderResourceView _cellSrv = null!;
    private ID3D11ShaderResourceView _atlasSrv = null!;
    private ID3D11ShaderResourceView _paletteSrv = null!;
    /// <summary>축소용 평균색 단계. 타일 한 변을 1/2/4 등분한 것.</summary>
    private static readonly int[] AvgLevels = [1, 2, 4];
    private readonly ID3D11ShaderResourceView[] _avgSrv = new ID3D11ShaderResourceView[AvgLevels.Length];
    private ID3D11Texture2D _spriteTex = null!;
    private ID3D11ShaderResourceView _spriteSrv = null!;

    private ID3D11Texture2D? _target;
    private ID3D11RenderTargetView? _rtv;
    private int _targetW, _targetH;

    public ID3D11Device Device => _device;

    /// <summary>지금 그리는 대상 텍스처. D3DImage 와 나눠 쓸 수 있도록 공유로 만든다.</summary>
    public ID3D11Texture2D? Target => _target;

    public int TargetWidth => _targetW;
    public int TargetHeight => _targetH;

    /// <summary>
    /// 장치와 텍스처를 올린다. <paramref name="worldData"/> 는 WORLD.CDS 원본,
    /// <paramref name="ocean"/> 은 풀어 둔 OCEAN.CDS 타일.
    /// </summary>
    public void Initialize(byte[] worldData, OceanTiles ocean)
    {
        var flags = DeviceCreationFlags.BgraSupport;
        var levels = new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };
        // out 인자의 형을 적어 둬야 한다 — var 로 두면 FeatureLevel 을 내는 오버로드와 헷갈린다.
        D3D11.D3D11CreateDevice(null, DriverType.Hardware, flags, levels,
                                out ID3D11Device dev, out ID3D11DeviceContext ctx).CheckError();
        _device = dev;
        _ctx = ctx;

        var vsBlob = Compiler.Compile(ShaderSource, "VS", "map.hlsl", "vs_4_0");
        var psBlob = Compiler.Compile(ShaderSource, "PS", "map.hlsl", "ps_4_0");
        _vs = _device.CreateVertexShader(vsBlob.Span);
        _ps = _device.CreatePixelShader(psBlob.Span);

        _cb = _device.CreateBuffer((uint)Marshal.SizeOf<FrameCb>(), BindFlags.ConstantBuffer,
                                   ResourceUsage.Dynamic, CpuAccessFlags.Write);

        CreateCellMap(worldData);
        CreateAtlas(ocean);
        CreatePalette(ocean);
        CreateSpriteTexture();
    }

    /// <summary>WORLD.CDS 를 펼쳐 칸마다 타일 번호만 담은 텍스처를 만든다.</summary>
    private void CreateCellMap(byte[] world)
    {
        int w = WorldMapRenderer.UnfoldedW, h = WorldMapRenderer.CellH;
        var cells = new ushort[w * h];
        for (int ry = 0; ry < h; ry++)
        {
            int even = ry * 2 * WorldMapRenderer.RawStride;
            int odd = even + WorldMapRenderer.RawStride;
            int row = ry * w;
            for (int cx = 0; cx < WorldMapRenderer.CellW; cx++)
            {
                // 짝수 행이 지도의 왼쪽 절반, 홀수 행이 오른쪽 절반이다.
                cells[row + cx] = (ushort)((world[even + cx * 2] | (world[even + cx * 2 + 1] << 8)) & OceanTiles.TileMask);
                cells[row + cx + WorldMapRenderer.CellW] =
                    (ushort)((world[odd + cx * 2] | (world[odd + cx * 2 + 1] << 8)) & OceanTiles.TileMask);
            }
        }
        _cellSrv = CreateImmutable(cells, w, h, Format.R16_UInt, sizeof(ushort));
    }

    /// <summary>16x16 타일 16,384장을 128x128 격자로 펴서 한 장으로 만든다.</summary>
    private void CreateAtlas(OceanTiles ocean)
    {
        var tiles = ocean.TileData;
        var atlas = new byte[AtlasSize * AtlasSize];
        for (int t = 0; t < OceanTiles.TileCount; t++)
        {
            int ax = (t % AtlasTiles) * OceanTiles.TileW;
            int ay = (t / AtlasTiles) * OceanTiles.TileW;
            int src = t * OceanTiles.TilePixels;
            for (int y = 0; y < OceanTiles.TileW; y++)
                Array.Copy(tiles, src + y * OceanTiles.TileW,
                           atlas, (ay + y) * AtlasSize + ax, OceanTiles.TileW);
        }
        _atlasSrv = CreateImmutable(atlas, AtlasSize, AtlasSize, Format.R8_UInt, 1);
    }

    private void CreatePalette(OceanTiles ocean)
    {
        var pal = new uint[256];
        for (int i = 0; i < 256; i++) pal[i] = ToBgra(ocean.PaletteRgb[i]);
        _paletteSrv = CreateImmutable(pal, 256, 1, Format.B8G8R8A8_UNorm, sizeof(uint));

        // 축소했을 때 쓸 평균색. 타일이 화면 한 점보다 작아지면 16x16 중 한 점만 뽑는 것이
        // 바다 디더링 무늬와 어긋나 물결(모아레)이 인다 — CPU 렌더러와 같은 표를 쓴다.
        for (int i = 0; i < AvgLevels.Length; i++)
        {
            int d = AvgLevels[i];
            var src = ocean.GetAverages(d);
            int side = AtlasTiles * d;
            var tex = new uint[side * side];
            for (int t = 0; t < OceanTiles.TileCount; t++)
            {
                int ax = (t % AtlasTiles) * d, ay = (t / AtlasTiles) * d;
                for (int qy = 0; qy < d; qy++)
                    for (int qx = 0; qx < d; qx++)
                        tex[(ay + qy) * side + ax + qx] = ToBgra(src[t * d * d + qy * d + qx]);
            }
            _avgSrv[i] = CreateImmutable(tex, side, side, Format.B8G8R8A8_UNorm, sizeof(uint));
        }
    }

    /// <summary>0xRRGGBB 를 B8G8R8A8 텍스처가 기대하는 배치로. 메모리 첫 바이트가 파랑이다.</summary>
    private static uint ToBgra(int rgb) => 0xFF000000u | (uint)(rgb & 0xFFFFFF);

    /// <summary>지금 배율에 맞는 잘게보기 단계를 고른다. 3 이면 원본 16x16 을 그대로 뽑는다.</summary>
    private static int PickDetail(double cellsPerPixel)
    {
        double pxPerCell = cellsPerPixel > 0 ? 1.0 / cellsPerPixel : OceanTiles.TileW;
        if (pxPerCell <= 1.0) return 0;   // 타일이 한 점보다 작다 -> 통째 평균
        if (pxPerCell <= 2.0) return 1;
        if (pxPerCell <= 4.0) return 2;
        return 3;
    }

    /// <summary>배/말 그림 48x48 한 장. 매 프레임 갈아 끼우므로 쓰기 가능으로 잡는다.</summary>
    private void CreateSpriteTexture()
    {
        _spriteTex = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = 48,
            Height = 48,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.Write,
        });
        _spriteSrv = _device.CreateShaderResourceView(_spriteTex);
    }

    private ID3D11ShaderResourceView CreateImmutable<T>(T[] data, int w, int h, Format fmt, int stride)
        where T : unmanaged
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var desc = new Texture2DDescription
            {
                Width = (uint)w,
                Height = (uint)h,
                MipLevels = 1,
                ArraySize = 1,
                Format = fmt,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Immutable,
                BindFlags = BindFlags.ShaderResource,
            };
            var sub = new SubresourceData(handle.AddrOfPinnedObject(), (uint)(w * stride));
            using var tex = _device.CreateTexture2D(desc, [sub]);
            return _device.CreateShaderResourceView(tex);
        }
        finally { handle.Free(); }
    }

    /// <summary>배 그림을 갈아 끼운다. 48x48 BGRA 이고 알파 0 이 비침이다.</summary>
    public void SetSprite(ReadOnlySpan<uint> bgra48X48)
    {
        if (bgra48X48.Length < 48 * 48) return;
        var map = _ctx.Map(_spriteTex, 0, Vortice.Direct3D11.MapMode.WriteDiscard);
        try
        {
            for (int y = 0; y < 48; y++)
            {
                var dst = new Span<uint>((void*)(map.DataPointer + y * map.RowPitch), 48);
                bgra48X48.Slice(y * 48, 48).CopyTo(dst);
            }
        }
        finally { _ctx.Unmap(_spriteTex, 0); }
    }

    /// <summary>그릴 대상 크기를 맞춘다. 바뀌었으면 새 텍스처를 만들고 true.</summary>
    public bool EnsureTarget(int width, int height)
    {
        if (_target != null && _targetW == width && _targetH == height) return false;

        _rtv?.Dispose();
        _target?.Dispose();
        _target = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            // D3DImage 는 D3D9 표면을 받으므로 공유 텍스처로 만들어 건네준다.
            MiscFlags = ResourceOptionFlags.Shared,
        });
        _rtv = _device.CreateRenderTargetView(_target);
        _targetW = width;
        _targetH = height;
        return true;
    }

    /// <summary>
    /// 한 프레임을 그린다. <paramref name="originCell"/> 은 화면 왼쪽 위가 가리키는 칸 좌표,
    /// <paramref name="cellsPerPixel"/> 은 화면 한 점이 나아가는 칸 수다.
    /// <paramref name="spriteRect"/> 는 배 그림이 놓일 화면 사각형(폭이 0 이하면 안 그린다).
    /// </summary>
    public void Render((double X, double Y) originCell, (double X, double Y) cellsPerPixel,
                       (float X, float Y, float W, float H) spriteRect)
    {
        if (_rtv == null) return;
        RenderTo(_rtv, _targetW, _targetH, originCell, cellsPerPixel, spriteRect);
        _ctx.Flush();
    }

    /// <summary>
    /// 밖에서 준 대상에 그린다. 스왑체인 백버퍼에 곧바로 그릴 때 쓴다 — D3DImage 를 거치지
    /// 않으므로 공유 표면 복사가 없다.
    /// </summary>
    public void RenderTo(ID3D11RenderTargetView rtv, int width, int height,
                         (double X, double Y) originCell, (double X, double Y) cellsPerPixel,
                         (float X, float Y, float W, float H) spriteRect)
    {
        var cb = new FrameCb
        {
            OriginCellX = (float)originCell.X,
            OriginCellY = (float)originCell.Y,
            CellPerPixelX = (float)cellsPerPixel.X,
            CellPerPixelY = (float)cellsPerPixel.Y,
            SpriteX = spriteRect.X,
            SpriteY = spriteRect.Y,
            SpriteW = spriteRect.W,
            SpriteH = spriteRect.H,
            MapCellsX = WorldMapRenderer.UnfoldedW,
            MapCellsY = WorldMapRenderer.CellH,
            Detail = PickDetail(cellsPerPixel.X),
        };
        var map = _ctx.Map(_cb, 0, Vortice.Direct3D11.MapMode.WriteDiscard);
        *(FrameCb*)map.DataPointer = cb;
        _ctx.Unmap(_cb, 0);

        _ctx.OMSetRenderTargets(rtv);
        _ctx.RSSetViewport(0, 0, width, height);
        _ctx.ClearRenderTargetView(rtv, new Color4(0, 0, 0, 1));
        _ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _ctx.VSSetShader(_vs);
        _ctx.PSSetShader(_ps);
        _ctx.PSSetConstantBuffer(0, _cb);
        _ctx.PSSetShaderResources(0, [_cellSrv, _atlasSrv, _paletteSrv, _spriteSrv,
                                      _avgSrv[0], _avgSrv[1], _avgSrv[2]]);
        _ctx.Draw(3, 0);
    }

    /// <summary>대상 텍스처를 CPU 로 내려받는다(BGRA). 시험용.</summary>
    public byte[]? ReadBack()
    {
        if (_target == null) return null;
        using var staging = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)_targetW,
            Height = (uint)_targetH,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read,
        });
        _ctx.CopyResource(staging, _target);
        var map = _ctx.Map(staging, 0, Vortice.Direct3D11.MapMode.Read);
        try
        {
            var outBuf = new byte[_targetW * _targetH * 4];
            for (int y = 0; y < _targetH; y++)
            {
                var src = new ReadOnlySpan<byte>((void*)(map.DataPointer + y * map.RowPitch), _targetW * 4);
                src.CopyTo(outBuf.AsSpan(y * _targetW * 4));
            }
            return outBuf;
        }
        finally { _ctx.Unmap(staging, 0); }
    }

    public void Dispose()
    {
        _rtv?.Dispose();
        _target?.Dispose();
        _spriteSrv?.Dispose();
        _spriteTex?.Dispose();
        foreach (var a in _avgSrv) a?.Dispose();
        _paletteSrv?.Dispose();
        _atlasSrv?.Dispose();
        _cellSrv?.Dispose();
        _cb?.Dispose();
        _ps?.Dispose();
        _vs?.Dispose();
        _ctx?.Dispose();
        _device?.Dispose();
    }
}
