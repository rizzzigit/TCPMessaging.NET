namespace RizzziGit.TCP.Messaging;

public class Response
{
  public static Buffer Serialize(Response response)
  {
    return Buffer.Concat(new Buffer[] { Buffer.FromByteArray(new byte[] { (byte)response.Type }), response.ID, response.Result });
  }

  public static Response Deserialize(Buffer buffer)
  {
    return new((ResponseType)buffer.Slice(0, 1)[0], buffer.Slice(1, 5), buffer.Slice(5, buffer.Length));
  }

  public Response(ResponseType type, Buffer id, Buffer result)
  {
    ID = id;
    Type = type;
    Result = result;
  }

  public Buffer ID { get; private set; }
  public ResponseType Type { get; private set; }
  public Buffer Result { get; private set; }
}
