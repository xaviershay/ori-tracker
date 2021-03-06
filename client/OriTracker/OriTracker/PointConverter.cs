﻿using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows;

namespace OriTracker
{
    class PointConverter : DependencyObject, IValueConverter
    {
        public static readonly DependencyProperty StepProperty = 
            DependencyProperty.Register(
            "Step", typeof(double),
            typeof(PointConverter)
            );
        public double Step
        {
            get { return (double)GetValue(StepProperty); }
            set { SetValue(StepProperty, value); }
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var data = (ObservableCollection<double>)value;

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
            double wymax = Math.Ceiling(data.Max() / Step) * Step;

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
