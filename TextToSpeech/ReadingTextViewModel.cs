using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace TextToSpeech
{
    class ReadingTextViewModel : INotifyPropertyChanged
    {
        private String text;
        public String Text
        {
            get
            {
                return text;
            }
            set
            {
                text = value;
                NotifyPropertyChanged();
            }
        }

        private String status = "";
        /// <summary>One line shown under the buttons, e.g. when a voice falls back.</summary>
        public String Status
        {
            get { return status; }
            set { status = value; NotifyPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public virtual void NotifyPropertyChanged([CallerMemberName] String propertyName = "")
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
