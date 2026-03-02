using System.Globalization;
using System.Windows.Data;

namespace Paste.UI.Converters;

public class RelativeTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTime dateTime)
        {
            var diff = DateTime.UtcNow - dateTime;

            if (diff.TotalSeconds < 60)
                return "刚刚";
            if (diff.TotalMinutes < 60)
                return $"{(int)diff.TotalMinutes}分钟前";
            if (diff.TotalHours < 24)
                return $"{(int)diff.TotalHours}小时前";
            if (diff.TotalDays < 7)
                return $"{(int)diff.TotalDays}天前";

            return dateTime.ToLocalTime().ToString("MM月dd日", culture);
        }
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
