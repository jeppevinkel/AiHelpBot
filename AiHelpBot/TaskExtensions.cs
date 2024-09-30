namespace AiHelpBot;

public static class TaskExtensions
{
    public static void AsyncNoAwait(this Task task)
    {
        task.ContinueWith(OnAsyncMethodFailed, TaskContinuationOptions.OnlyOnFaulted);
    }

    private static void OnAsyncMethodFailed(Task task)
    {
        Exception? ex = task.Exception;

        switch (ex)
        {
            case null:
                return;
            case AggregateException aggregateException:
            {
                foreach (Exception innerException in aggregateException.InnerExceptions)
                {
                    Console.WriteLine(innerException.Message);
                }
                break;
            }
            default:
                Console.WriteLine(ex);
                break;
        }
    }
}