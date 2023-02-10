using System.Net;
using System.Net.Sockets;

namespace RizzziGit.TCP.Messaging;

public class ServerConnection : Connection
{
  public ServerConnection(IPEndPoint ipEndPoint, Socket socket) : base(ipEndPoint, socket)
  {
  }

  protected override async Task<Buffer> ProcessRequest(Request request)
  {
    return Buffer.FromString("OK");
  }
}
