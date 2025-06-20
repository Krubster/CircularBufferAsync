using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WPFViewer
{
    public class LiveArgument : INotifyPropertyChanged
    {
        public string Key { get; set; } = "";
        public string OriginalType { get; set; } = "string";

        private string _value = "";
        public string Value
        {
            get => _value;
            set
            {
                if (_value != value)
                {
                    _value = value;
                    OnPropertyChanged(nameof(Value));
                    ValueChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public List<string>? Options { get; set; } = null;

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? ValueChanged;

        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
