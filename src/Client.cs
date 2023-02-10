using System.Net;

namespace RizzziGit.TCP.Messaging;

public class Client
{

  public Client()
  {
    Connections = new ClientConnection[0];
  }

  private ClientConnection[] Connections;

  public async Task<ClientConnection> Connect(IPEndPoint ipEndPoint)
  {
    ClientConnection connection = new(ipEndPoint);
    connection.Connected += (sender, args) => Connections = Connections.Append(connection).ToArray();
    connection.Disconnected  += (sender, args) => Connections = Connections.Except(new ClientConnection[] { connection }).ToArray();

    await connection.Connect();
    return connection;
  }
}
