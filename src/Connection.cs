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

public class ReceiveRequestCancelledException : Exception
{
  public ReceiveRequestCancelledException() : base("Socket connection was closed.") { }
}

public class PendingRequest
{
  public PendingRequest(Request request, TaskCompletionSource<Buffer> source)
  {
    Request = request;
    Source = source;
  }

  public Request Request { get; private set; }
  private TaskCompletionSource<Buffer> Source;

  public void SetResult(Buffer buffer)
  {
    Source.SetResult(buffer);
  }

  public void SetException(Exception exception)
  {
    Source.SetException(exception);
  }

  public Task<Buffer> GetTask()
  {
    return Source.Task;
  }
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

public class CommandReceivedEventHandlerArgs : EventArgs
{
  public bool PreventDefault { get; set; } = false;
}

public class Connection
{
  public enum Command
  {
    Hello, Message, Request, Response, Bye
  }

  internal delegate void ConnectionHandler(Connection connection);

  internal Connection(IPEndPoint ipEndPoint, Socket? socket)
  {
    ID = Buffer.Random(8);
    Socket = socket ?? new(ipEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
    MessageQueue = new();
    EndPoint = ipEndPoint;

    PendingRequests = new();
    RequestQueue = new();
    RequestWaiter = new();
  }

  private Socket Socket { get; set; }

  public Buffer ID { get; private set; }
  public IPEndPoint EndPoint { get; private set; }
  public IPEndPoint? RemoteEndpoint { get; private set; }

  public bool IsRunning { get; private set; }
  public bool IsReady { get; private set; }

  public event EventHandler? Connected;
  public async Task Connect()
  {
    if (IsRunning)
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

  private Dictionary<string, PendingRequest> PendingRequests;
  private Queue<PendingRequest> RequestQueue;
  private Queue<TaskCompletionSource<PendingRequest>> RequestWaiter;

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
    if (IsRunning)
    {
      return;
    }

    IsRunning = true;
    Buffer sink = Buffer.Allocate(0);
    await SendCommand(Command.Hello, Buffer.Allocate(0));
    RemoteEndpoint = (IPEndPoint?)Socket.RemoteEndPoint;
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
    IsRunning = false;
    IsReady = false;

    if (MessageWaiter != null)
    {
      MessageWaiter.SetException(new ReceiveMessageCancelledException());
      MessageWaiter = null;
    }

    if (ByeWaiter != null)
    {
      ByeWaiter.SetResult();
      ByeWaiter = null;
    }

    while (RequestWaiter.Count > 0)
    {
      RequestWaiter.Dequeue().SetException(new ReceiveRequestCancelledException());
    }

    await Disconnect(false);
  }

  public delegate void CommandReceivedEventHandler(Command command, Buffer data, CommandReceivedEventHandlerArgs args);
  public event CommandReceivedEventHandler? CommandReceived;

  private async void ReceiveCommand(Buffer data)
  {
    Command command = (Command)data[0];
    data = data.Slice(1, data.Length);
    CommandReceivedEventHandlerArgs args = new();
    CommandReceived?.Invoke(command, data, args);
    if (args.PreventDefault)
    {
      return;
    }

    switch (command)
    {
      case Command.Hello: IsReady = true; break;
      case Command.Request: PushRequest(data); break;
      case Command.Response: PushResponse(data); break;
      case Command.Message: Push(data); break;
      case Command.Bye: await PushBye(); break;
    }
  }

  public delegate void CommandSentEventHandler(Command command, Buffer data);
  public event CommandSentEventHandler? CommandSent;

  private async Task SendCommand(Command command, Buffer data)
  {
    Buffer buffer = Buffer.Concat(new Buffer[] { Buffer.FromByteArray(new byte[] { (byte)command }), data });
    Buffer lengthBuffer = Buffer.FromByteArray((BitConverter.IsLittleEndian ? BitConverter.GetBytes(buffer.Length).Reverse().ToArray() : BitConverter.GetBytes(buffer.Length)));

    await Write(Buffer.Concat(new Buffer[] { Buffer.FromByteArray(new byte[] { (byte)lengthBuffer.Length }), lengthBuffer, buffer }));
    CommandSent?.Invoke(command, data);
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
    PendingRequests.TryGetValue(responseId, out PendingRequest? PendingRequestEntry);

    if (PendingRequestEntry == null)
    {
      return;
    }

    PendingRequests.Remove(responseId);
    switch (response.Type)
    {
      case ResponseType.Data:
        PendingRequestEntry.SetResult(response.Result);
        break;

      case ResponseType.Exception:
        PendingRequestEntry.SetException(new Exception(response.Result.ToString()));
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
    TaskCompletionSource<Buffer> source = new();
    PendingRequest requestEntry = new(request, source);
    PendingRequests.Add(request.ID.ToString(), requestEntry);

    await SendCommand(Command.Request, Request.Serialize(request));
    return await source.Task;
  }

  public async Task<PendingRequest> ReceivePendingRequest()
  {
    if (RequestQueue.Count > 0)
    {
      return RequestQueue.Dequeue();
    }
    else
    {
      TaskCompletionSource<PendingRequest> source = new();
      RequestWaiter.Enqueue(source);

      return await source.Task;
    }
  }

  private async void PushRequest(Buffer buffer)
  {
    if (buffer.Length < 8)
    {
      return;
    }

    Request request = Request.Deserialize(buffer);
    TaskCompletionSource<Buffer> source = new();
    PendingRequest pendingRequest = new(request, source);

    if (RequestWaiter.Count > 0)
    {
      RequestWaiter.Dequeue().SetResult(pendingRequest);
    }
    else
    {
      RequestQueue.Enqueue(pendingRequest);
    }

    ResponseType type;
    Buffer result;
    try
    {
      result = await source.Task;
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
}
