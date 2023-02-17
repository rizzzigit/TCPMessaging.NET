namespace RizzziGit.TCP.Messaging;

public class PendingRequest
{
  public PendingRequest(Request request, TaskCompletionSource<Buffer> source)
  {
    Request = request;
    Source = source;
    IsPending = true;
  }

  public Request Request { get; private set; }
  public bool IsPending { get; private set; }
  private TaskCompletionSource<Buffer> Source;

  public void SetResult(Buffer buffer)
  {
    Source.SetResult(buffer);
    IsPending = false;
  }

  public void SetException(Exception exception)
  {
    Source.SetException(exception);
    IsPending = false;
  }

  public Task<Buffer> GetTask()
  {
    return Source.Task;
  }
}
