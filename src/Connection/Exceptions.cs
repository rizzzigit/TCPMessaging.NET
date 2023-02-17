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

public class ReceivePendingRequestCancelledException : Exception
{
  public ReceivePendingRequestCancelledException() : base("Socket connection was closed.") { }
}
