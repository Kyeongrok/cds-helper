using System.Globalization;
using System.Windows.Data;

namespace CdsHelper.Main.Local.Converters;

/// <summary>
/// 스킬 레벨을 투명도로 변환 (0이면 투명, 1-3이면 레벨에 따라 밝기)
/// </summary>
public class LevelToOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is byte level)
        {
            return level switch
            {
                0 => 0.15,  // 거의 안 보임
                1 => 0.5,   // 반투명
                2 => 0.75,  // 조금 더 진하게
                _ => 1.0    // 완전히 보임 (3 이상)
            };
        }
        return 0.15;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
