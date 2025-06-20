using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
using System.Windows.Shapes;

namespace WPFViewer
{
    /// <summary>
    /// Interaction logic for ArgumentEditorWindow.xaml
    /// </summary>
    public partial class ArgumentEditorWindow : Window, INotifyPropertyChanged
    {
        public ObservableCollection<LiveArgument> Arguments { get; set; } = new();
        public bool ImmediateSend { get; set; } = false;

        private Action<string> _sendCommand;

        public ArgumentEditorWindow(IEnumerable<LiveArgument> args, Action<string> sendCommand)
        {
            InitializeComponent();
            DataContext = this;
            _sendCommand = sendCommand;

            foreach (var arg in args)
            {
                arg.ValueChanged += OnArgChanged;
                Arguments.Add(arg);
                AddArgControl(arg);
            }
        }

        private void AddArgControl(LiveArgument arg)
        {
            var label = new TextBlock { Text = arg.Key };

            FrameworkElement input;
            if (arg.OriginalType == "bool")
            {
                var cb = new CheckBox { IsChecked = arg.Value == "true" };
                cb.Checked += (_, _) => arg.Value = "true";
                cb.Unchecked += (_, _) => arg.Value = "false";
                input = cb;
            }
            else if (arg.OriginalType == "enum" && arg.Options != null)
            {
                var combo = new ComboBox { ItemsSource = arg.Options, SelectedValue = arg.Value };
                combo.SelectionChanged += (_, _) => arg.Value = combo.SelectedValue?.ToString() ?? "";
                input = combo;
            }
            else
            {
                var tb = new TextBox { Text = arg.Value };
                tb.TextChanged += (_, _) => arg.Value = tb.Text;
                input = tb;
            }

            ArgsPanel.Children.Add(label);
            ArgsPanel.Children.Add(input);
        }

        private void OnArgChanged(object? sender, EventArgs e)
        {
            if (ImmediateSend && sender is LiveArgument arg)
            {
                _sendCommand($"set {arg.Key}={arg.Value}");
            }
        }

        private void OnSetClicked(object sender, RoutedEventArgs e)
        {
            foreach (var arg in Arguments)
            {
                _sendCommand($"set {arg.Key}={arg.Value}");
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
