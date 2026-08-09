using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace QuiverLauncher
{
    public class DoubleScaleConverter : IValueConverter
    {
        public static readonly DoubleScaleConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            double doubleValue = value switch
            {
                double d => d,
                int i => i,
                float f => f,
                _ => 0d
            };

            if (parameter is string paramString &&
                double.TryParse(paramString, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale))
            {
                return doubleValue * scale;
            }

            return doubleValue;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
