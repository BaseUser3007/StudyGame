public class ExecutionResult
{
    public bool IsSuccess { get; private set; }
    public bool IsCancelled { get; private set; }
    public string ErrorMessage { get; private set; }

    private ExecutionResult() { }

    public static ExecutionResult Success()
        => new ExecutionResult { IsSuccess = true };

    public static ExecutionResult CompileError(string message)
        => new ExecutionResult { ErrorMessage = $"Compile error: {message}" };

    public static ExecutionResult RuntimeError(string message)
        => new ExecutionResult { ErrorMessage = $"Runtime error: {message}" };

    public static ExecutionResult Cancelled()
        => new ExecutionResult { IsCancelled = true };
}