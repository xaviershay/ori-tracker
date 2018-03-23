using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace OriTracker
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public int MaxPoints = 200;
        private ViewModel Model;

        public MainWindow()
        {
            InitializeComponent();

            Model = new ViewModel();
            Model.Enabled = true;
            Model.TrackerUrl = "https://example.com";

            // TODO: Persist this locally
            Guid guid = Guid.NewGuid();

            Model.PlayerId = GuidEncoder.Encode(guid);
            Model.PlayerName = "xavier";
            this.DataContext = Model;
            var empty = Enumerable.Range(0, MaxPoints).Select(_ => 0.0);
            latencies = new LinkedList<double>(empty);
            queueSize = new LinkedList<int>(empty.Select(x => (int)x));
            var timer = FakeDataAsync();
        }

        private LinkedList<double> latencies;
        private LinkedList<int> queueSize;

        private Polyline CalculateLatencyLine()
        {
            var canvas = MetricsGraph;
            var dmargin = 0.0;
            var data = latencies;

            double dxmin = dmargin;
            double dxmax = canvas.ActualWidth - dmargin;
            double dymin = dmargin;
            double dymax = canvas.ActualHeight - dmargin;

            double wxmin = 0.0;
            double wxmax = MaxPoints;
            double wymin = data.Min();
            double wymax = data.Max();

            var matrix = Matrix.Identity;
            matrix.Translate(-wxmin, -wymin);
            matrix.Scale((dxmax - dxmin) / (wxmax - wxmin), (dymax - dymin) / (wymax - wymin));
            matrix.Translate(dxmin, dymin);

            var line = new Polyline();
            line.Stroke = (Brush)FindResource("SecondaryAccentBrush");
            line.StrokeThickness = 0.7;
            var t = 0;
            foreach (var metric in data)
            {
                var p = matrix.Transform(new Point(t, metric));
                line.Points.Add(p);
                t += 1;
            }
            return line;
        }

        private Polyline CalculateQueueSizeLine()
        {
            var canvas = MetricsGraph;
            var dmargin = 0.0;
            var data = queueSize;

            double dxmin = dmargin;
            double dxmax = canvas.ActualWidth - dmargin;
            double dymin = dmargin;
            double dymax = canvas.ActualHeight - dmargin;

            double wxmin = 0.0;
            double wxmax = MaxPoints;
            double wymin = 0;
            double wymax = Math.Max(20, data.Max());

            var matrix = Matrix.Identity;
            matrix.Translate(-wxmin, -wymin);
            matrix.Scale((dxmax - dxmin) / (wxmax - wxmin), (dymax - dymin) / (wymax - wymin));
            matrix.Translate(dxmin, dymin);

            var line = new Polyline();
            line.Stroke = (Brush)FindResource("PrimaryHueLightBrush");
            line.StrokeThickness = 0.7;
            var t = 0;
            foreach (var metric in data)
            {
                var p = matrix.Transform(new Point(t, metric));
                line.Points.Add(p);
                t += 1;
            }
            return line;
        }

        private void RedrawCanvas()
        {
            var canvas = MetricsGraph;
            canvas.Children.Clear();
            canvas.Children.Add(CalculateLatencyLine());
            canvas.Children.Add(CalculateQueueSizeLine());
        }

        async Task FakeDataAsync()
        {
            var rnd = new Random();
            while (true)
            {
                // TODO: Need to synchronize metrics concurrent access
                latencies.AddLast(1 - Math.Sqrt(1 - rnd.NextDouble()));
                if (latencies.Count > MaxPoints)
                {
                    latencies.RemoveFirst();
                }

                var size = 0;
                if (queueSize.Count > 0)
                {
                    size = queueSize.Last.Value;
                }

                size = Math.Max(0, size + rnd.Next(-1, 2));

                queueSize.AddLast(size);
                if (queueSize.Count > MaxPoints)
                {
                    queueSize.RemoveFirst();
                }

                MetricsGraph.Dispatcher.Invoke(new Action(() =>
                {
                    RedrawCanvas();
                }));
                await Task.Delay(200);
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RedrawCanvas();
        }
    }
}
