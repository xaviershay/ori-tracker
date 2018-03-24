using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows;
using System.ComponentModel;

namespace OriTracker
{
    class PointConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var data = (ObservableCollection<double>)value;
            var step = System.Convert.ToDouble(parameter);

            var dmargin = 0.0;
            int MaxPoints = 200; // TODO: De-dupe this

            double actualWidth = 100;
            double actualHeight = 100;
            double dxmin = dmargin;
            double dxmax = actualWidth - dmargin;
            double dymin = dmargin;
            double dymax = actualHeight - dmargin;

            double wxmin = 0.0;
            double wxmax = MaxPoints;
            double wymin = 0;
            double wymax = Math.Ceiling(data.Max() / step) * step;

            var matrix = Matrix.Identity;
            matrix.Translate(-wxmin, -wymin);
            matrix.Scale((dxmax - dxmin) / (wxmax - wxmin), (dymax - dymin) / (wymax - wymin));
            matrix.Translate(dxmin, dymin);

            return new PointCollection(data.Select((metric, t) => matrix.Transform(new Point(t, metric))));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
