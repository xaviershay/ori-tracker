using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using System;

namespace OriTracker
{
    internal class ViewModel : INotifyPropertyChanged
    {
        public ViewModel()
        {
            Latencies = new ObservableCollection<double>();
            QueueSizes = new ObservableCollection<double>();

            // TODO: This is duplicated in XAML because I can't figure out how to bind them to converter values
            QueueSizesStep = 20;
            LatencyStep = 0.2;
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

        private double latencyStep;
        public double LatencyStep
        {
            get => latencyStep;
            set => SetField(ref latencyStep, value);
        }

        private double queueSizesStep;
        public double QueueSizesStep
        {
            get => queueSizesStep;
            set => SetField(ref queueSizesStep, value);
        }

        private double maxLatencyStep;
        public double MaxLatencyStep
        {
            get => maxLatencyStep;
            set => SetField(ref maxLatencyStep, value);
        }

        private double maxQueueSizesStep;
        public double MaxQueueSizesStep
        {
            get => maxQueueSizesStep;
            set => SetField(ref maxQueueSizesStep, value);
        }

        private ObservableCollection<double> latencies;
        public ObservableCollection<double> Latencies
        {
            get => latencies;
            set {
                if (latencies != value)
                {
                    value.CollectionChanged += (sender, e) =>
                    {
                        if (value.Any())
                            MaxLatencyStep = Math.Ceiling(value.Max() / LatencyStep) * LatencyStep;
                        NotifyPropertyChanged();
                    };
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
                    value.CollectionChanged += (sender, e) =>
                    {
                        if (value.Any())
                            MaxQueueSizesStep = Math.Ceiling(value.Max() / QueueSizesStep) * QueueSizesStep;
                        NotifyPropertyChanged();
                    };
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