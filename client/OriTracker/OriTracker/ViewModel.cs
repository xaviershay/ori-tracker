using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OriTracker
{
    internal class ViewModel : INotifyPropertyChanged
    {
        public ViewModel()
        {
            Latencies = new ObservableCollection<double>();
            QueueSizes = new ObservableCollection<double>();
        }

        private bool oriHooked;
        public bool OriHooked
        {
            get => oriHooked;
            set => SetField(ref oriHooked, value);
        }

        private bool enabled;
        public bool Enabled
        {
            get => enabled;
            set => SetField(ref enabled, value);
        }

        private string trackerUrl;
        public string TrackerUrl
        {
            get => trackerUrl;
            set => SetField(ref trackerUrl, value);
        }

        private string playerId;
        public string PlayerId
        {
            get => playerId;
            set => SetField(ref playerId, value);
        }

        private string playerName;
        public string PlayerName
        {
            get => playerName;
            set => SetField(ref playerName, value);
        }

        private ObservableCollection<double> latencies;
        public ObservableCollection<double> Latencies
        {
            get => latencies;
            set {
                if (latencies != value)
                {
                    value.CollectionChanged += (sender, e) => NotifyPropertyChanged();
                }
                SetField(ref latencies, value);
            }
        }

        private ObservableCollection<double> queueSizes;
        public ObservableCollection<double> QueueSizes
        {
            get => queueSizes;
            set {
                if (queueSizes != value)
                {
                    value.CollectionChanged += (sender, e) => NotifyPropertyChanged();
                }
                SetField(ref queueSizes, value);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void NotifyPropertyChanged([CallerMemberName]string propName = null)
        {
            if (this.PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName]string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            NotifyPropertyChanged(propertyName);
            return true;
        }
    }
}