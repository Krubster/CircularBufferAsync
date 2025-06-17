using System.Net.Sockets;
using System.Net;
using NETwork.ConnectionProcessors;

public class TcpWorkloadServer
{
    private readonly IPEndPoint _listenEndPoint;
    private readonly IConnectionProcessor _processor;
    private readonly CancellationToken _token;

    private Socket? _listener;
    private Thread? _acceptThread;

    public TcpWorkloadServer(IPEndPoint listenEndPoint, Func<IConnectionProcessor> processorFactory, CancellationToken token)
    {
        _listenEndPoint = listenEndPoint;
        _processor = processorFactory();
        _token = token;
    }

    public void Start()
    {
        _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };

        _listener.Bind(_listenEndPoint);
        _listener.Listen(128);

        _acceptThread = new Thread(AcceptLoop) { IsBackground = true };
        _acceptThread.Start();

        _processor.Start(_token); // Delegate main loop/thread logic to processor

        Console.WriteLine($"[Server] Listening on {_listenEndPoint}");
    }

    private void AcceptLoop()
    {
        while (!_token.IsCancellationRequested)
        {
            try
            {
                var socket = _listener!.Accept();
                socket.NoDelay = true;
                _processor.Add(socket); // Pass socket to processor
            }
            catch (SocketException se) when (se.SocketErrorCode == SocketError.Interrupted)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Accept] {ex.Message}");
            }
        }
    }

    public void Stop()
    {
        _listener?.Close();
        _processor?.Stop();
    }
}