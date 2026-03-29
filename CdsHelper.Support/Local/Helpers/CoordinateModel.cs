using TorchSharp;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>좌표 인식 결과.</summary>
public record CoordinatePrediction(
    bool IsNorth,   // true=북위, false=남위
    int LatValue,   // 0-90
    bool IsEast,    // true=동경, false=서경
    int LonValue    // 0-180
)
{
    public double ToLat() => IsNorth ? LatValue : -LatValue;
    public double ToLon() => IsEast ? LonValue : -LonValue;

    public override string ToString()
        => $"{(IsNorth ? "북위" : "남위")} {LatValue}  {(IsEast ? "동경" : "서경")} {LonValue}";
}

/// <summary>학습 데이터 라벨.</summary>
public record CoordinateLabel(
    string ImagePath,
    int LatDir,     // 0=북위, 1=남위
    int LatValue,   // 0-90
    int LonDir,     // 0=동경, 1=서경
    int LonValue    // 0-180
);

/// <summary>
/// TorchSharp 기반 좌표 인식 CNN 모델.
/// 입력: 1×32×160 그레이스케일 이미지 (상단바 좌표 영역 크롭)
/// 출력: 위도방향(2) + 위도값(91) + 경도방향(2) + 경도값(181) = 276
/// </summary>
public class CoordinateNet : Module<Tensor, Tensor>
{
    public const int InputHeight = 32;
    public const int InputWidth = 320;
    public const int LatDirClasses = 2;
    public const int LatValClasses = 91;   // 0~90
    public const int LonDirClasses = 2;
    public const int LonValClasses = 181;  // 0~180
    public const int TotalOutput = LatDirClasses + LatValClasses + LonDirClasses + LonValClasses; // 276

    private readonly Module<Tensor, Tensor> _features;
    private readonly Module<Tensor, Tensor> _latDirHead;
    private readonly Module<Tensor, Tensor> _latValHead;
    private readonly Module<Tensor, Tensor> _lonDirHead;
    private readonly Module<Tensor, Tensor> _lonValHead;

    public CoordinateNet(Device? device = null) : base("CoordinateNet")
    {
        var dev = device ?? CPU;

        _features = Sequential(
            Conv2d(1, 16, 3, padding: 1),
            BatchNorm2d(16),
            ReLU(),
            MaxPool2d(2),                    // → 16×16×80

            Conv2d(16, 32, 3, padding: 1),
            BatchNorm2d(32),
            ReLU(),
            MaxPool2d(2),                    // → 32×8×40

            Conv2d(32, 64, 3, padding: 1),
            BatchNorm2d(64),
            ReLU(),
            AdaptiveAvgPool2d(new long[] { 1, 1 }),  // → 64×1×1
            Flatten()                        // → 64
        );

        _latDirHead = Sequential(
            Linear(64, 32), ReLU(), Linear(32, LatDirClasses)
        );
        _latValHead = Sequential(
            Linear(64, 128), ReLU(), Linear(128, LatValClasses)
        );
        _lonDirHead = Sequential(
            Linear(64, 32), ReLU(), Linear(32, LonDirClasses)
        );
        _lonValHead = Sequential(
            Linear(64, 128), ReLU(), Linear(128, LonValClasses)
        );

        RegisterComponents();
        this.to(dev);
    }

    /// <summary>
    /// 순전파. 4개 헤드 출력을 concat하여 반환.
    /// [B, 276] = [latDir(2) | latVal(91) | lonDir(2) | lonVal(181)]
    /// </summary>
    public override Tensor forward(Tensor x)
    {
        var feat = _features.forward(x);
        var ld = _latDirHead.forward(feat);
        var lv = _latValHead.forward(feat);
        var od = _lonDirHead.forward(feat);
        var ov = _lonValHead.forward(feat);
        return cat([ld, lv, od, ov], dim: 1);
    }

    /// <summary>출력 텐서를 4개 헤드로 분리.</summary>
    public static (Tensor latDir, Tensor latVal, Tensor lonDir, Tensor lonVal) SplitOutput(Tensor output)
    {
        var ld = output[.., ..LatDirClasses];
        var lv = output[.., LatDirClasses..(LatDirClasses + LatValClasses)];
        var od = output[.., (LatDirClasses + LatValClasses)..(LatDirClasses + LatValClasses + LonDirClasses)];
        var ov = output[.., (LatDirClasses + LatValClasses + LonDirClasses)..];
        return (ld, lv, od, ov);
    }

    /// <summary>출력 텐서에서 예측 결과를 디코딩.</summary>
    public static CoordinatePrediction Decode(Tensor output)
    {
        var (ld, lv, od, ov) = SplitOutput(output);

        var latDir = ld.argmax(1).item<long>();
        var latVal = lv.argmax(1).item<long>();
        var lonDir = od.argmax(1).item<long>();
        var lonVal = ov.argmax(1).item<long>();

        return new CoordinatePrediction(
            IsNorth: latDir == 0,
            LatValue: (int)latVal,
            IsEast: lonDir == 0,
            LonValue: (int)lonVal
        );
    }
}
