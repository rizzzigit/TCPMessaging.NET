using System.Net;

namespace RizzziGit.TCP.Messaging;

public class ClientConnection : Connection
{
  public ClientConnection(IPEndPoint ipEndPoint) : base(ipEndPoint, null)
  {
  }

  protected override async Task<Buffer> ProcessRequest(Request request)
  {
    return Buffer.Allocate(0);
  }
}
