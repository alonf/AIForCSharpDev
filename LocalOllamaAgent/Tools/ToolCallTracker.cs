namespace LocalOllamaAgent.Tools;

public static class ToolCallTracker
{
    private static int _compileCallCount;
    private static int _executeCallCount;

    public static int CompileCalls => Volatile.Read(ref _compileCallCount);
    public static int ExecuteCalls => Volatile.Read(ref _executeCallCount);

    public static void RegisterCompileCall()
    {
        Interlocked.Increment(ref _compileCallCount);
    }

    public static void RegisterExecuteCall()
    {
        Interlocked.Increment(ref _executeCallCount);
    }
}
