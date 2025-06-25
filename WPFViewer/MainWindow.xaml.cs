using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace WPFViewer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private TcpClient? _client;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private bool _running = true;
        public bool ServerEnabled { get; set; } = false;
        public bool ClientEnabled { get; set; } = false;
        public int ClientCount { get; set; } = 10;
        public string CommandToSend { get; set; } = string.Empty;
        private Dictionary<string, (string Value, string Type)> KnownArgumentTypes = new()
        {
            // Server arguments
            ["--mode"] = ("Async", "enum:Sync,Async,Triplex"),
            ["--cpuload"] = ("10", "int"),
            ["--sendChance"] = ("0.3", "double"),
            ["--lowBorder"] = ("20", "int"),
            ["--highBorder"] = ("300", "int"),
            ["--backgroundWorkload"] = ("10000", "int"),
            ["--workload"] = ("Cpu", "string"),

            // Client arguments
            ["--connections"] = ("10", "int"),
            ["--sendInterval"] = ("1000", "int"),
            ["--lowEnd"] = ("20", "int"),
            ["--highEnd"] = ("150", "int")
        };
        public ObservableCollection<MetricPoint> Points { get; set; } = new();
        public ObservableCollection<ChartViewModel> SeriesCollection { get; set; } = new();
        public ObservableCollection<string> AvailableMetrics { get; set; } = new();
        public ObservableCollection<string> SelectedMetrics { get; set; } = new();
        public ObservableCollection<LaunchPreset> ServerPresets { get; set; } = new();
        public ObservableCollection<LaunchPreset> ClientPresets { get; set; } = new();
        public ObservableCollection<string> Logs { get; set; } = new();

        private LaunchPreset? _selectedServerPreset;
        public LaunchPreset? SelectedServerPreset
        {
            get => _selectedServerPreset;
            set
            {
                _selectedServerPreset = value;
                OnPropertyChanged(nameof(SelectedServerPreset));
            }
        }

        private LaunchPreset? _selectedClientPreset;
        public LaunchPreset? SelectedClientPreset
        {
            get => _selectedClientPreset;
            set
            {
                _selectedClientPreset = value;
                OnPropertyChanged(nameof(SelectedClientPreset));
            }
        }

        private string _serverArguments = "";
        public string ServerArguments
        {
            get => _serverArguments;
            set
            {
                _serverArguments = value;
                OnPropertyChanged(nameof(ServerArguments));
            }
        }

        private string _clientArguments = "";
        public string ClientArguments
        {
            get => _clientArguments;
            set
            {
                _clientArguments = value;
                OnPropertyChanged(nameof(ClientArguments));
            }
        }
        public string ServerExePath { get; set; } = "";
        public string ClientExePath { get; set; } = "";
        private Process? _serverProcess;
        private Process? _clientProcess;
        public string ServerToggleButtonText => ServerRunning ? "Stop Server" : "Start Server";
        public string ClientToggleButtonText => ClientRunning ? "Stop Client" : "Start Client";

        private bool _serverRunning = false;
        public bool ServerRunning
        {
            get => _serverRunning;
            set
            {
                _serverRunning = value;
                OnPropertyChanged(nameof(ServerRunning));
                OnPropertyChanged(nameof(ServerToggleButtonText));
            }
        }

        private bool _clientRunning = false;
        public bool ClientRunning
        {
            get => _clientRunning;
            set
            {
                _clientRunning = value;
                OnPropertyChanged(nameof(ClientRunning));
                OnPropertyChanged(nameof(ClientToggleButtonText));
            }
        }

        private void OnBrowseServerExe(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            if (dlg.ShowDialog() == true)
            {
                ServerExePath = dlg.FileName;
                SaveSettings();
                OnPropertyChanged(nameof(ServerExePath));
            }
        }

        private void OnBrowseClientExe(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            if (dlg.ShowDialog() == true)
            {
                ClientExePath = dlg.FileName;
                SaveSettings();
                OnPropertyChanged(nameof(ClientExePath));
            }
        }

        private string serverMode = "Unknown";
        
        private const int ServerCommandPort = 23456;
        
        private const int ClientCommandPort = 23457;
        private TcpClient? _serverCommandClient;
        private StreamWriter? _serverWriter;

        private TcpClient? _clientCommandClient;
        private StreamWriter? _clientWriter;

        private const string SettingsFile = "settings.json";
        private AppSettings Settings = new();
        private ArgumentEditorWindow? _serverEditorWindow;
        private ArgumentEditorWindow? _clientEditorWindow;
        private StreamWriter? _csvWriter;
        private readonly object _csvLock = new();
        private void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }

                ServerExePath = Settings.ServerExePath;
                ClientExePath = Settings.ClientExePath;
            }
            catch (Exception ex)
            {
                Log($"Error loading settings: {ex.Message}");
            }
        }

        private void SaveSettings()
        {
            try
            {
                Settings.ServerExePath = ServerExePath;
                Settings.ClientExePath = ClientExePath;
                var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, json);
            }
            catch (Exception ex)
            {
                Log($"Error saving settings: {ex.Message}");
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            Closing += MainWindow_Closing;
            DataContext = this;
            LoadSettings();
            ServerPresets.Add(new LaunchPreset
            {
                Name = "Sync Server (Real)",
                Args = new Dictionary<string, string>
                {
                    { "mode", "Sync" },
                    { "infinite", "true" },
                    { "cpuload", "330" },
                    { "workload", "cpu" },
                    { "backgroundWorkload", "30" }
                }
            });
            ServerPresets.Add(new LaunchPreset
            {
                Name = "Async Server (Real)",
                Args = new Dictionary<string, string>
                {
                    { "mode", "Async" },
                    { "infinite", "true" },
                    { "cpuload", "330" },
                    { "workload", "cpu" },
                    { "backgroundWorkload", "30" }
                }
            });

            ServerPresets.Add(new LaunchPreset
            {
                Name = "Triplex Server (Real)",
                Args = new Dictionary<string, string>
                {
                    { "mode", "Triplex" },
                    { "infinite", "true" },
                    { "cpuload", "330" },
                    { "workload", "cpu" },
                    { "backgroundWorkload", "30" }
                }
            });

            ClientPresets.Add(new LaunchPreset
            {
                Name = "Default Frequent Client",
                Args = new Dictionary<string, string>
                {
                    { "connections", "10" },
                    { "sendInterval", "1" },
                    { "lowEnd", "20" },
                    { "highEnd", "150" },
                }
            });

            ClientPresets.Add(new LaunchPreset
            {
                Name = "Burst Frequent Clients",
                Args = new Dictionary<string, string>
                {
                    { "connections", "1000" },
                    { "sendInterval", "1" },
                    { "lowEnd", "20" },
                    { "highEnd", "150" },
                }
            });
            ClientPresets.Add(new LaunchPreset
            {
                Name = "Default Real Client",
                Args = new Dictionary<string, string>
                {
                    { "connections", "10" },
                    { "sendInterval", "225" },
                    { "lowEnd", "15" },
                    { "highEnd", "21" },
                }
            });

            ClientPresets.Add(new LaunchPreset
            {
                Name = "Burst Real Clients",
                Args = new Dictionary<string, string>
                {
                    { "connections", "1000" },
                    { "sendInterval", "225" },
                    { "lowEnd", "15" },
                    { "highEnd", "21" },
                }
            });
            Task.Run(ServerCommandLoop);
            Task.Run(ClientCommandLoop);
            SelectedMetrics.CollectionChanged += (_, _) => UpdateSeries();
            Task.Run(ListenLoop);
        }
        private void InitializeCsvWriter()
        {
            try
            {
                _csvWriter = new StreamWriter($"metrics_{serverMode}_{DateTime.Now:yyyyMMdd_HHmmss}.csv", false);
                _csvWriter.WriteLine("Timestamp,Metric,Value");
                _csvWriter.Flush();
            }
            catch (Exception ex)
            {
                Log($"CSV initialization failed: {ex.Message}");
            }
        }

        private void AppendToCsv(MetricPoint point)
        {
            lock (_csvLock)
            {
                try
                {
                    if (_csvWriter == null) InitializeCsvWriter();

                    foreach (var metric in point.Metrics)
                    {
                        _csvWriter?.WriteLine(
                            $"{point.Timestamp:o}," +
                            $"{metric.Key}," +
                            $"{metric.Value.ToString(CultureInfo.InvariantCulture)}"
                        );
                    }
                    _csvWriter?.Flush();
                }
                catch (Exception ex)
                {
                    Log($"CSV write error: {ex.Message}");
                }
            }
        }
        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            Log("Shutting down...");

            try
            {
                _serverCommandClient?.Close();
                _clientCommandClient?.Close();
                Log("Closed command connections.");
            }
            catch (Exception ex)
            {
                Log($"Error closing command channels: {ex.Message}");
            }

            try
            {
                if (_serverProcess != null && !_serverProcess.HasExited)
                {
                    _serverProcess.Kill(true);
                    Log("Server process terminated.");
                }
            }
            catch (Exception ex)
            {
                Log($"Error killing server: {ex.Message}");
            }

            try
            {
                if (_clientProcess != null && !_clientProcess.HasExited)
                {
                    _clientProcess.Kill(true);
                    Log("Client process terminated.");
                }
            }
            catch (Exception ex)
            {
                Log($"Error killing client: {ex.Message}");
            }
        }

        private async Task ServerCommandLoop()
        {
            while (true)
            {
                try
                {
                    Log("[Server] Connecting to command channel...");
                    _serverCommandClient = new TcpClient();
                    await _serverCommandClient.ConnectAsync("localhost", ServerCommandPort);
                    _serverWriter = new StreamWriter(_serverCommandClient.GetStream()) { AutoFlush = true };
                    Log("[Server] Connected to command channel.");
                    var args = ParseArgsWithTypes(ServerArguments);
                    ShowServerEditorWindow();

                    // поддержка живого соединения
                    while (_serverCommandClient.Connected)
                    {
                        await Task.Delay(1000); // держим живым
                    }
                }
                catch (Exception ex)
                {
                    Log($"[Server] Command channel error: {ex.Message}");
                }

                _serverCommandClient?.Dispose();
                _serverCommandClient = null;
                _serverWriter = null;

                Log("[Server] Disconnected. Retrying in 3 seconds...");
                await Task.Delay(3000);
            }
        }

        private async Task ClientCommandLoop()
        {
            while (true)
            {
                try
                {
                    Log("[Client] Connecting to command channel...");
                    _clientCommandClient = new TcpClient();
                    await _clientCommandClient.ConnectAsync("localhost", ClientCommandPort);
                    _clientWriter = new StreamWriter(_clientCommandClient.GetStream()) { AutoFlush = true };
                    Log("[Client] Connected to command channel.");

                    var args = ParseArgsWithTypes(ClientArguments);
                    ShowClientEditorWindow();

                    while (_clientCommandClient.Connected)
                    {
                        await Task.Delay(1000);
                    }
                }
                catch (Exception ex)
                {
                    Log($"[Client] Command channel error: {ex.Message}");
                }

                _clientCommandClient?.Dispose();
                _clientCommandClient = null;
                _clientWriter = null;

                Log("[Client] Disconnected. Retrying in 3 seconds...");
                await Task.Delay(3000);
            }
        }
        private void ShowServerEditorWindow()
        {
            Dispatcher.Invoke(() =>
            {
                if (_serverEditorWindow == null || !_serverEditorWindow.IsLoaded)
                {
                    var args = ParseArgsWithTypes(ServerArguments);
                    _serverEditorWindow = new ArgumentEditorWindow(args, SendCommandToServer)
                    {
                        Title = "Server Arguments",
                        Owner = this
                    };
                    _serverEditorWindow.Closed += (_, _) => _serverEditorWindow = null;
                    _serverEditorWindow.Show();
                }
                else
                {
                    _serverEditorWindow.Activate();
                }
            });
        }
        private void ShowClientEditorWindow()
        {
            Dispatcher.Invoke(() =>
            {
                if (_clientEditorWindow == null || !_clientEditorWindow.IsLoaded)
                {
                    var args = ParseArgsWithTypes(ClientArguments);
                    _clientEditorWindow = new ArgumentEditorWindow(args, SendCommandToClient)
                    {
                        Title = "Client Arguments",
                        Owner = this
                    };
                    _clientEditorWindow.Closed += (_, _) => _clientEditorWindow = null;
                    _clientEditorWindow.Show();
                }
                else
                {
                    _clientEditorWindow.Activate();
                }
            });
        }
        public void OnApplyServerPreset(object sender, RoutedEventArgs e)
        {
            if (SelectedServerPreset != null)
            {
                ServerArguments = string.Join(Environment.NewLine,
                    SelectedServerPreset.Args.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            }
        }

        public void OnApplyClientPreset(object sender, RoutedEventArgs e)
        {
            if (SelectedClientPreset != null)
            {
                ClientArguments = string.Join(Environment.NewLine,
                    SelectedClientPreset.Args.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            }
        }
        public void OnPropertyChanged(string propName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Content is string metric && !SelectedMetrics.Contains(metric))
                SelectedMetrics.Add(metric);
        }

        private void CheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Content is string metric && SelectedMetrics.Contains(metric))
                SelectedMetrics.Remove(metric);
        }

        private async Task ListenLoop()
        {
            while (_running)
            {
                try
                {
                    Log("Connecting to metrics server...");
                    _client = new TcpClient();
                    await _client.ConnectAsync("localhost", 12345);
                    Log("Connected to metrics server.");

                    _reader = new StreamReader(_client.GetStream());
                    _writer = new StreamWriter(_client.GetStream()) { AutoFlush = true };

                    while (_running && _client.Connected)
                    {
                        string? line = await _reader.ReadLineAsync();
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        var stat = JsonSerializer.Deserialize<ConnectionStatsDto>(line);
                        if (stat == null) continue;

                        Dispatcher.Invoke(() =>
                        {
                            var point = new MetricPoint
                            {
                                Timestamp = DateTime.UtcNow,
                                Metrics = stat.metrics
                            };
                            AppendToCsv(point); // Сохраняем в CSV
                            Points.Add(point);
                            if (Points.Count > 200) Points.RemoveAt(0);

                            foreach (var key in stat.metrics.Keys)
                            {
                                if (!AvailableMetrics.Contains(key))
                                    AvailableMetrics.Add(key);
                            }

                            UpdateSeries();
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log($"Connection error: {ex.Message}");
                }

                Log("Disconnected. Retrying in 3 seconds...");
                await Task.Delay(3000);
            }
        }

        private void UpdateSeries()
        {
            // Удаляем те, которые больше не выбраны
            for (int i = SeriesCollection.Count - 1; i >= 0; i--)
            {
                if (!SelectedMetrics.Contains(SeriesCollection[i].MetricName))
                {
                    SeriesCollection.RemoveAt(i);
                }
            }

            // Добавляем новые графики
            foreach (var key in SelectedMetrics)
            {
                if (!SeriesCollection.Any(c => c.MetricName == key))
                {
                    var series = new LineSeries<double>
                    {
                        Name = key,
                        Values = Points.Select(p => p.Metrics.TryGetValue(key, out var v) ? v : 0).ToArray(),
                        GeometrySize = 3,
                        Stroke = new SolidColorPaint(SKColors.DodgerBlue, 1.5f),
                        Fill = null
                    };

                    var chartVm = new ChartViewModel
                    {
                        MetricName = key,
                        Series = new ISeries[] { series },
                        XAxes = new[]
                        {
                            new Axis
                            {
                                Labels = Points.Select(p => p.Timestamp.ToString("HH:mm:ss")).ToArray(),
                                TextSize = 10,
                                SeparatorsPaint = null,
                                LabelsPaint = new SolidColorPaint(SKColors.LightGray)
                            }
                        },
                        YAxes = new[]
                        {
                            new Axis
                            {
                                TextSize = 10,
                                Name = key,
                                SeparatorsPaint = null,
                                LabelsPaint = new SolidColorPaint(SKColors.LightGray)
                            }
                        }
                    };

                    SeriesCollection.Add(chartVm);
                }
                else
                {
                    // обновляем значения
                    var existing = SeriesCollection.First(c => c.MetricName == key);
                    foreach (var s in existing.Series)
                    {
                        if (s is LineSeries<double> line)
                        {
                            line.Values = Points.Select(p => p.Metrics.TryGetValue(key, out var v) ? v : 0).ToArray();
                        }
                    }

                    existing.XAxes[0].Labels = Points.Select(p => p.Timestamp.ToString("HH:mm:ss")).ToArray();
                }
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SeriesCollection)));
        }
        private void Log(string message)
        {
            var timestamped = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Dispatcher.Invoke(() =>
            {
                Logs.Add(timestamped);
                if (Logs.Count > 500)
                    Logs.RemoveAt(0);

                // автоскроллинг
                if (LogListBox.Items.Count > 0)
                {
                    LogListBox.ScrollIntoView(LogListBox.Items[^1]);
                }
            });
        }
        private void SendCommandToServer(string command)
        {
            if (_serverWriter != null)
            {
                Log($"[Server] Sending: {command}");
                _serverWriter.WriteLine(command);
            }
            else
            {
                Log("[Server] Command not sent — not connected.");
            }
        }

        private void SendCommandToClient(string command)
        {
            if (_clientWriter != null)
            {
                Log($"[Client] Sending: {command}");
                _clientWriter.WriteLine(command);
            }
            else
            {
                Log("[Client] Command not sent — not connected.");
            }
        }

        private Dictionary<string, string> ParseArgs(string argsText)
        {
            return argsText
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split(new[] { '=' }, 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0].TrimStart('-'), parts => parts[1].Trim());
        }

        public void OnToggleServerClicked(object sender, RoutedEventArgs e)
        {
            if (ServerRunning)
            {
                try
                {
                    _serverProcess?.Kill(true);
                    _serverProcess = null;
                    ServerRunning = false;
                    Log("Server process stopped.");
                }
                catch (Exception ex)
                {
                    Log($"Error stopping server: {ex.Message}");
                }
            }
            else
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(ServerExePath) || !File.Exists(ServerExePath))
                    {
                        Log("Invalid server executable path.");
                        return;
                    }

                    var args = ParseArgs(ServerArguments);
                    serverMode = args["mode"];
                    var argLine = string.Join(" ", args.Select(kvp => $"--{kvp.Key}={kvp.Value}"));

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = ServerExePath,
                        Arguments = argLine,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    _serverProcess = Process.Start(startInfo);
                    ServerRunning = true;
                    Log("Server started with args: " + argLine);
                    Task.Run(() => ReadProcessOutput(_serverProcess!, "[Server]"));
                }
                catch (Exception ex)
                {
                    Log($"Server start failed: {ex.Message}");
                }
            }
        }

        public void OnToggleClientClicked(object sender, RoutedEventArgs e)
        {
            if (ClientRunning)
            {
                try
                {
                    _clientProcess?.Kill(true);
                    _clientProcess = null;
                    ClientRunning = false;
                    Log("Client process stopped.");
                }
                catch (Exception ex)
                {
                    Log($"Error stopping client: {ex.Message}");
                }
            }
            else
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(ClientExePath) || !File.Exists(ClientExePath))
                    {
                        Log("Invalid client executable path.");
                        return;
                    }

                    var args = ParseArgs(ClientArguments);
                    var argLine = string.Join(" ", args.Select(kvp => $"--{kvp.Key}={kvp.Value}"));

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = ClientExePath,
                        Arguments = argLine,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    _clientProcess = Process.Start(startInfo);
                    ClientRunning = true;
                    Log("Client started with args: " + argLine);
                    Task.Run(() => ReadProcessOutput(_clientProcess!, "[Client]"));
                }
                catch (Exception ex)
                {
                    Log($"Client start failed: {ex.Message}");
                }
            }
        }

        private async Task ReadProcessOutput(Process proc, string prefix)
        {
            try
            {
                var output = proc.StandardOutput;
                var error = proc.StandardError;

                while (!proc.HasExited)
                {
                    var line = await output.ReadLineAsync();
                    if (!string.IsNullOrWhiteSpace(line))
                        Log($"{prefix} {line}");
                }

                string? remainingErr;
                while ((remainingErr = await error.ReadLineAsync()) != null)
                {
                    if (!string.IsNullOrWhiteSpace(remainingErr))
                        Log($"{prefix} [stderr] {remainingErr}");
                }
            }
            catch (Exception ex)
            {
                Log($"{prefix} output read error: {ex.Message}");
            }
        }

        private List<LiveArgument> ParseArgsWithTypes(string argsText)
        {
            return argsText
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split('='))
                .Where(parts => parts.Length == 2)
                .Select(parts =>
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();
                    var rawType = KnownArgumentTypes.TryGetValue(key, out var val) ? val.Type : "string";

                    var type = rawType;
                    List<string>? options = null;

                    if (rawType.StartsWith("enum:"))
                    {
                        type = "enum";
                        options = rawType.Substring(5).Split(',').ToList();
                    }

                    return new LiveArgument
                    {
                        Key = key,
                        Value = value,
                        OriginalType = type,
                        Options = options
                    };
                }).ToList();
        }

        public void OnSendCommandClicked(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(CommandToSend))
            {
                SendCommandToServer(CommandToSend);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class ConnectionStatsDto
    {
        public Dictionary<string, double> metrics { get; set; } = new();
    }

    public class MetricPoint
    {
        public DateTime Timestamp { get; set; }
        public Dictionary<string, double> Metrics { get; set; } = new();
    }

    public class ChartViewModel
    {
        public string MetricName { get; set; } = string.Empty;
        public ISeries[] Series { get; set; } = Array.Empty<ISeries>();
        public Axis[] XAxes { get; set; } = Array.Empty<Axis>();
        public Axis[] YAxes { get; set; } = Array.Empty<Axis>();
    }
}