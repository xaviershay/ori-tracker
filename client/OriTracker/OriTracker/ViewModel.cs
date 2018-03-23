using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OriTracker
{
    class ViewModel : INotifyPropertyChanged
    {
        private bool oriHooked;
        public bool OriHooked {
            get { return this.oriHooked;  }
            set
            {
                if (oriHooked != value)
                {
                    oriHooked = value;
                    NotifyPropertyChanged("OriHooked");
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void NotifyPropertyChanged(string propName)
        {
            if(this.PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propName));
        }
    }
}
