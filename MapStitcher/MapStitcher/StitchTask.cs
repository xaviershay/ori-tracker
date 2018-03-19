using System;
using System.ComponentModel;

namespace MapStitcher
{
    internal class StitchTask : IProgress<double>, INotifyPropertyChanged
    {
        public string Name { get; protected set; }
        public double Progress { get; internal set; }
        public DateTime StartTime { get; internal set; }
        public DateTime? FinishTime { get; internal set; }
        public string Result { get; internal set; }
        public bool? Cached { get; internal set; }

        public Action Preview { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public StitchTask(string name = "")
        {
            Name = name;
            Reset();
        }

        protected virtual void Reset()
        {
            Progress = 0.0;
            FinishTime = null;
            StartTime = DateTime.Now;
            Result = "";
            Cached = null;

            NotifyPropertyChanged("Progress");
            NotifyPropertyChanged("StartTime");
            NotifyPropertyChanged("Duration");
            NotifyPropertyChanged("Result");
            NotifyPropertyChanged("Cached");
        }
        
        public virtual void ShowPreview(Renderer renderer)
        {

        }

        public virtual void Run() { }

        public void Complete(string result, bool cached)
        {
            Report(1.0);
            Result = result;
            Cached = cached;
            FinishTime = DateTime.Now;
            NotifyPropertyChanged("Duration");
            NotifyPropertyChanged("Result");
            NotifyPropertyChanged("Cached");
        }

        public void Report(double value)
        {
            if (value > 1.0)
            {
                throw new ArgumentException();
            }
            Progress = value;
            NotifyPropertyChanged("Progress");
        }

        private void NotifyPropertyChanged(String propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public TimeSpan? Duration { get
            {
                if (FinishTime.HasValue)
                {
                    return FinishTime.Value - StartTime;
                } else
                {
                    return null;
                }
            }
        }

        internal virtual void ClearCache()
        {
        }
    }
}