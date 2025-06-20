using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WPFViewer
{
    public class MetricsContainsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var selected = value as ObservableCollection<string>;
            var key = parameter as string;
            return selected != null && key != null && selected.Contains(key);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException(); // не нужен
        }
    }
}
