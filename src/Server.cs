using System.Net;
using System.Net.Sockets;

namespace RizzziGit.TCP.Messaging;

public class SimulatneousAcceptConnectionCallsException : Exception
{
  public SimulatneousAcceptConnectionCallsException() : base("AcceptConnection() calls may not be simultaneous.") { }
}

public class AcceptConnectionCancelledException : Exception
{
  public AcceptConnectionCancelledException() : base("Server listener was closed.") { }
}

public class Server
{
  public Server()
  {
    Listeners = new ServerListener[0];
    Connections = new();
    ConnectionQueue = new();
  }

  public ServerListener[] Listeners { get; private set; }
  public Dictionary<Buffer, Connection> Connections { get; private set; }
  private Queue<Connection> ConnectionQueue;
  private TaskCompletionSource<Connection>? ConnectionWaiter;

  public async Task<Connection> AcceptConnection()
  {
    if (ConnectionQueue.Count != 0)
    {
      return ConnectionQueue.Dequeue();
    }
    else if (ConnectionWaiter != null)
    {
      throw new SimulatneousAcceptConnectionCallsException();
    }

    try
    {
      return await (ConnectionWaiter = new()).Task;
    }
    finally
    {
      ConnectionWaiter = null;
    }
  }

  internal void OnClientConnected(Socket socket)
  {
    IPEndPoint? endpoint = (IPEndPoint?)socket.RemoteEndPoint;
    if (endpoint == null)
    {
      socket.Close();
      return;
    }

    Connection connection = new(endpoint, socket);
    string connectionId = connection.ID.ToString();
    connection.Disconnected += (sender, args) => Connections.Remove(connection.ID);
    Connections.Add(connection.ID, connection);
    connection.StartReceivingCommands();
    PushConnection(connection);
  }

  private void PushConnection(Connection connection)
  {
    if (ConnectionWaiter != null)
    {
      ConnectionWaiter.SetResult(connection);
    }
    else
    {
      ConnectionQueue.Enqueue(connection);
    }
  }

  public delegate void StartedListeningEventHandler(ServerListener listener);
  public event StartedListeningEventHandler? StartedListening;

  public delegate void StoppedListeningEventHandler(ServerListener listener);
  public event StoppedListeningEventHandler? StoppedListening;

  public ServerListener Listen(IPEndPoint endPoint)
  {
    ServerListener listener = new(this, endPoint);
    listener.StartedListening += () => {
      Listeners = Listeners.Append(listener).ToArray();
      StartedListening?.Invoke(listener);
    };
    listener.StoppedListening += () => {
      Listeners.Except(new ServerListener[] { listener }).ToArray();
      StoppedListening?.Invoke(listener);

      if ((Listeners.Length == 0) && (ConnectionWaiter != null))
      {
        ConnectionWaiter.SetException(new AcceptConnectionCancelledException());
      }
    };
    listener.ClientConnected += OnClientConnected;

    return listener;
  }
}
