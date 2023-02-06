using System.Net;

namespace RizzziGit.TCP.Messaging;

public class Client
{

  public Client()
  {
    Connections = new Connection[0];
  }

  private Connection[] Connections;

  public async Task<Connection> Connect(IPEndPoint ipEndPoint)
  {
    Connection connection = new(ipEndPoint, null);
    connection.Connected += (sender, args) => Connections = Connections.Append(connection).ToArray();
    connection.Disconnected  += (sender, args) => Connections = Connections.Except(new Connection[] { connection }).ToArray();

    await connection.Connect();
    return connection;
  }
}
