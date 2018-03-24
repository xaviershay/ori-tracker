using System;
using System.Globalization;
using System.Windows.Data;

namespace OriTracker
{
    class ScaleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var scale = System.Convert.ToDouble(parameter);

            return (double)value * scale;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
