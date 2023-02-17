namespace RizzziGit.TCP.Messaging;

public class Request
{
  public static Buffer Serialize(Request request)
  {
    return Buffer.Concat(new Buffer[] { request.ID, request.Token, request.Parameters });
  }

  public static Request Deserialize(Buffer buffer)
  {
    return new(buffer.Slice(0, 4), buffer.Slice(4, 8), buffer.Slice(8, buffer.Length));
  }

  public Request(Buffer id, Buffer token, Buffer parameters)
  {
    Token = token;
    ID = id;
    Parameters = parameters;
  }

  public Buffer Token { get; private set; }
  public Buffer ID { get; private set; }
  public Buffer Parameters { get; private set; }
}
