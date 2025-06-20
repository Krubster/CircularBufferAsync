using NETwork;
using System.Net;
using System.Net.Sockets;
using System.Text;

public class MetricsBroadcastServer
{
    private readonly TcpListener _listener;
    private readonly List<TcpClient> _clients = new();
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;

    public MetricsBroadcastServer(int port = 12345)
    {
        _listener = new TcpListener(IPAddress.Any, port);
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener.Start();
        _ = AcceptLoop(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        lock (_lock)
        {
            foreach (var c in _clients)
                c.Close();
            _clients.Clear();
        }
        _listener.Stop();
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                lock (_lock) _clients.Add(client);
            }
            catch
            {
                // Listener stopped
                break;
            }
        }
    }

    public void Broadcast(ConnectionStats stats)
    {
        string json = stats.ToJson();
        byte[] payload = Encoding.UTF8.GetBytes(json + "\n");

        lock (_lock)
        {
            for (int i = _clients.Count - 1; i >= 0; i--)
            {
                var client = _clients[i];
                if (!client.Connected)
                {
                    _clients.RemoveAt(i);
                    continue;
                }

                try
                {
                    var stream = client.GetStream();
                    stream.Write(payload, 0, payload.Length);
                }
                catch
                {
                    _clients.RemoveAt(i);
                    client.Close();
                }
            }
        }
    }
}