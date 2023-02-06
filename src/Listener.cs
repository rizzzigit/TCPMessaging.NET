using System.Net;
using System.Net.Sockets;

namespace RizzziGit.TCPMessaging;

public class ServerListenException : Exception { public ServerListenException() : base("Server is already listening") { } }

public class ServerListener
{
  internal delegate void ListenerEventHandler();
  internal delegate void ClientConnectionEventHandler(Socket client);

  internal ServerListener(
    Server server,
    IPEndPoint endPoint
  )
  {
    Server = server;
    Socket = new(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
    EndPoint = endPoint;
  }

  private Socket Socket { get; set; }

  public Server Server { get; private set; }
  public IPEndPoint EndPoint { get; private set; }

  private async void BeginAccepting()
  {
    while (true)
    {
      Socket socket;
      try { socket = await Socket.AcceptAsync(); } catch { break; }

      ClientConnected?.Invoke(socket);
    }
    Stop();
  }

  public void Start()
  {
    Socket.Bind(EndPoint);
    Socket.Listen(EndPoint.Port);
    StartedListening?.Invoke();
    BeginAccepting();
  }

  public void Stop()
  {
    Socket.Close();
    StoppedListening?.Invoke();
  }

  internal event ListenerEventHandler? StartedListening;
  internal event ListenerEventHandler? StoppedListening;
  internal event ClientConnectionEventHandler? ClientConnected;
}
