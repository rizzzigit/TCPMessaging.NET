using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RizzziGit.TCP.Messaging;

public class SimulatneousReceiveMessageCallsException : Exception
{
  public SimulatneousReceiveMessageCallsException() : base("ReceiveMessage() calls may not be simultaneous.") { }
}

public class DisconnectCallException : Exception
{
  public DisconnectCallException() : base("Disconnect() call may only be called once.") { }
}

public class ReceiveMessageCancelledException : Exception
{
  public ReceiveMessageCancelledException() : base("Socket connection was closed.") { }
}

public class PendingRequestEntry
{
  public PendingRequestEntry(Request request, TaskCompletionSource<Buffer> task)
  {
    Request = request;
    Task = task;
  }

  public Request Request { get; private set; }
  public TaskCompletionSource<Buffer> Task { get; private set; }
}

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

public enum ResponseType
{
  Data, Exception
}

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

public abstract class Connection
{
  public enum Command
  {
    Hello, Message, Request, Response, Bye
  }

  internal delegate void ConnectionHandler(Connection connection);

  internal protected Connection(IPEndPoint ipEndPoint, Socket? socket)
  {
    ID = Buffer.Random(8);
    Socket = socket ?? new(ipEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
    MessageQueue = new();
    EndPoint = ipEndPoint;
    PendingRequests = new();
  }

  private Socket Socket { get; set; }

  public Buffer ID { get; private set; }
  public IPEndPoint EndPoint { get; private set; }
  public IPEndPoint? RemoteEndpoint { get; private set; }

  public bool Running { get; private set; }
  public bool Ready { get; private set; }

  public event EventHandler? Connected;
  public async Task Connect()
  {
    if (Running)
    {
      return;
    }

    await Socket.ConnectAsync(EndPoint);

    Connected?.Invoke(this, new());
    StartReceivingCommands();
  }

  public event EventHandler? Disconnected;
  private async Task Disconnect(bool waitBye)
  {
    if (waitBye)
    {
      if (ByeWaiter != null)
      {
        throw new DisconnectCallException();
      }

      try
      {
        ByeWaiter = new();
        try
        {
          await SendCommand(Command.Bye, Buffer.Allocate(0));
        }
        catch { }
        await ByeWaiter.Task;
      }
      finally
      {
        ByeWaiter = null;
      }
    }

    Disconnected?.Invoke(this, new());
    await Socket.DisconnectAsync(false);
  }

  public async Task Disconnect()
  {
    await Disconnect(true);
  }

  private TaskCompletionSource? ByeWaiter;
  private TaskCompletionSource<Buffer>? MessageWaiter;
  private Queue<Buffer> MessageQueue;
  private Dictionary<string, PendingRequestEntry> PendingRequests;

  private async Task<Buffer?> Read()
  {
    byte[] receive;
    int receivedBytes = await Socket.ReceiveAsync(receive = new byte[4_096]);
    if (receivedBytes == 0)
    {
      return null;
    }

    return Buffer.FromByteArray(receive, 0, receivedBytes);
  }

  private async Task Write(Buffer buffer)
  {
    await Socket.SendAsync(buffer.ToByteArray());
  }

  public async void StartReceivingCommands()
  {
    if (Running)
    {
      return;
    }

    Running = true;
    Buffer sink = Buffer.Allocate(0);
    await SendCommand(Command.Hello, Buffer.Allocate(0));
    RemoteEndpoint = (IPEndPoint?)Socket.RemoteEndPoint;
    Console.WriteLine(this.ToString());
    while (true)
    {
      Buffer? read = await Read();
      if (read == null)
      {
        break;
      }

      sink = Buffer.Concat(new Buffer[] { sink, read });
      while (true)
      {
        if (
          (sink.Length == 0) ||
          ((1 + sink[0]) > (sink.Length))
        )
        {
          break;
        }

        int length;
        {
          byte[] lengthBuffer = sink.Slice(1, 1 + sink[0]).PadStart(4).ToByteArray();
          if (BitConverter.IsLittleEndian)
          {
            lengthBuffer = lengthBuffer.Reverse().ToArray();
          }

          length = BitConverter.ToInt32(lengthBuffer);
        }
        if ((1 + sink[0] + length) > sink.Length)
        {
          break;
        }

        ReceiveCommand(sink.Slice(1 + sink[0], 1 + sink[0] + length));
        sink = sink.Slice(1 + sink[0] + length, sink.Length);
      }
    }
    RemoteEndpoint = null;
    Running = false;
    Ready = false;

    if (MessageWaiter != null)
    {
      MessageWaiter.SetException(new ReceiveMessageCancelledException());
    }

    if (ByeWaiter != null)
    {
      ByeWaiter.SetResult();
    }

    await Disconnect(false);
  }

  private async void ReceiveCommand(Buffer data)
  {
    Console.WriteLine($"{ID.ToHex()} RECEIVED: {(Command)data[0]} -> {data.Slice(1, data.Length).ToHex()}");
    switch ((Command)data[0])
    {
      case Command.Hello: Ready = true; break;
      case Command.Request: PushRequest(data.Slice(1, data.Length)); break;
      case Command.Response: PushResponse(data.Slice(1, data.Length)); break;
      case Command.Message: Push(data.Slice(1, data.Length)); break;
      case Command.Bye: await PushBye(); break;
    }
  }

  private async Task SendCommand(Command command, Buffer data)
  {
    Buffer buffer = Buffer.Concat(new Buffer[] { Buffer.FromByteArray(new byte[] { (byte)command }), data });
    Buffer lengthBuffer = Buffer.FromByteArray((BitConverter.IsLittleEndian ? BitConverter.GetBytes(buffer.Length).Reverse().ToArray() : BitConverter.GetBytes(buffer.Length)));

    Console.WriteLine($"{ID.ToHex()} SENT: {command} -> {data.ToHex()}");
    await Write(Buffer.Concat(new Buffer[] { Buffer.FromByteArray(new byte[] { (byte)lengthBuffer.Length }), lengthBuffer, buffer }));
  }

  private void Push(Buffer message)
  {
    if (MessageWaiter != null)
    {
      MessageWaiter.SetResult(message);
    }
    else
    {
      MessageQueue.Enqueue(message);
    }
  }

  public async Task<Buffer> Receive()
  {
    if (MessageQueue.Count > 0)
    {
      return MessageQueue.Dequeue();
    }
    else if (MessageWaiter != null)
    {
      throw new SimulatneousReceiveMessageCallsException();
    }

    try
    {
      return await (MessageWaiter = new()).Task;
    }
    finally
    {
      MessageWaiter = null;
    }
  }

  public Task Send(Buffer buffer)
  {
    return SendCommand(Command.Message, buffer);
  }

  public Task Send(byte[] buffer)
  {
    return Send(Buffer.FromByteArray(buffer));
  }

  public Task Send(string buffer)
  {
    return Send(Buffer.FromString(buffer));
  }

  private async Task PushBye()
  {
    try { await SendCommand(Command.Bye, Buffer.Allocate(0)); } catch {}
    if (ByeWaiter != null)
    {
      ByeWaiter.SetResult();
    }

    await Disconnect(false);
  }

  private void PushResponse(Buffer buffer)
  {
    Response response = Response.Deserialize(buffer);
    string responseId = response.ID.ToString();
    PendingRequests.TryGetValue(responseId, out PendingRequestEntry? PendingRequestEntry);

    if (PendingRequestEntry == null)
    {
      return;
    }

    PendingRequests.Remove(responseId);
    switch (response.Type)
    {
      case ResponseType.Data:
        PendingRequestEntry.Task.SetResult(response.Result);
        break;

      case ResponseType.Exception:
        PendingRequestEntry.Task.SetException(new Exception(response.Result.ToString()));
        break;
    }
  }

  public async Task<Buffer> SendRequest(Buffer token, Buffer parameters)
  {
    if (token.Length != 4)
    {
      throw new Exception("Token length must be 4");
    }

    Request request = new(Buffer.Random(4), token, parameters);
    PendingRequestEntry requestEntry = new(request, new());
    PendingRequests.Add(request.ID.ToString(), requestEntry);

    await SendCommand(Command.Request, Request.Serialize(request));
    return await requestEntry.Task.Task;
  }

  private async void PushRequest(Buffer buffer)
  {
    if (buffer.Length < 8)
    {
      return;
    }

    Request request = Request.Deserialize(buffer);
    ResponseType type;
    Buffer result;

    try
    {
      result = await ProcessRequest(request);
      type = ResponseType.Data;
    }
    catch (Exception exception)
    {
      result = Buffer.FromString(exception.Message);
      type = ResponseType.Exception;
    }

    Response response = new(type, request.ID, result);
    await SendCommand(Command.Response, Response.Serialize(response));
  }

  protected abstract Task<Buffer> ProcessRequest(Request request);
}
