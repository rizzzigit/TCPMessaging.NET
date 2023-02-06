using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RizzziGit.TCPMessaging;

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

public class Connection
{
  public enum Command
  {
    Hello, Message, Bye
  }

  internal delegate void ConnectionHandler(Connection connection);

  internal Connection(
    IPEndPoint ipEndPoint,
    Socket? socket
  )
  {
    Socket = socket ?? new(ipEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

    EndPoint = ipEndPoint;
    {
      byte[] randomId = new byte[8];
      Random.Shared.NextBytes(randomId);
      ID = BitConverter.ToString(randomId).Replace("-", "");
      MessageQueue = new();
    }
  }

  private Socket Socket { get; set; }

  public string ID { get; private set; }
  public IPEndPoint EndPoint { get; private set; }
  public IPEndPoint? RemoteEndpoint { get; private set; }

  public bool Running { get; private set; }
  public bool Ready { get; private set; }

  private TaskCompletionSource? ByeWaiter;
  private TaskCompletionSource<Buffer>? MessageWaiter;
  private Queue<Buffer> MessageQueue;

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
    switch ((Command)data[0])
    {
      case Command.Hello:
        Ready = true;
        break;

      case Command.Message:
        if (MessageWaiter != null)
        {
          MessageWaiter.SetResult(data.Slice(1, data.Length));
        }
        else
        {
          MessageQueue.Enqueue(data.Slice(1, data.Length));
        }
        break;

      case Command.Bye:
        try
        {
          await SendCommand(Command.Bye, Buffer.Allocate(0));
        }
        catch {}
        if (ByeWaiter != null)
        {
          ByeWaiter.SetResult();
        }

        await Disconnect(false);
        break;
    }
  }

  private async Task SendCommand(Command command, Buffer data)
  {
    Buffer buffer = Buffer.Concat(new Buffer[] { Buffer.FromByteArray(new byte[] { (byte)command }), data });
    Buffer lengthBuffer = Buffer.FromByteArray((BitConverter.IsLittleEndian ? BitConverter.GetBytes(buffer.Length).Reverse().ToArray() : BitConverter.GetBytes(buffer.Length)));

    await Write(Buffer.Concat(new Buffer[] { Buffer.FromByteArray(new byte[] { (byte)lengthBuffer.Length }), lengthBuffer, buffer }));
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
        catch {}
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
}
