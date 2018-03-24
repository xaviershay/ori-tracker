using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
            Model.OriHooked = true;
            Model.Enabled = true;
            Model.TrackerUrl = "https://example.com";

            // TODO: Persist this locally
            Guid guid = Guid.NewGuid();

            Model.PlayerId = GuidEncoder.Encode(guid);
            Model.PlayerName = "xavier";
            this.DataContext = Model;
            foreach (var _ in Enumerable.Range(0, MaxPoints))
            {
                Model.Latencies.Add(0.0);
                Model.QueueSizes.Add(0.0);
            }
            var timer = FakeDataAsync();
        }

        async Task FakeDataAsync()
        {
            var rnd = new Random();
            var latencies = Model.Latencies;
            var queueSize = Model.QueueSizes;
            while (true)
            {
                // TODO: Need to synchronize metrics concurrent access
                latencies.Add(1 - Math.Sqrt(1 - rnd.NextDouble()));
                if (latencies.Count > MaxPoints)
                {
                    latencies.RemoveAt(0);
                }

                var size = 0.0;
                if (queueSize.Count > 0)
                {
                    size = queueSize.Last();
                }

                size = Math.Max(0, size + rnd.Next(-1, 2));

                queueSize.Add(size);
                if (queueSize.Count > MaxPoints)
                {
                    queueSize.RemoveAt(0);
                }

                await Task.Delay(500);
            }
        }
    }
}
