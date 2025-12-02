using SwiftlyS2.Shared;


class SourceSynchronizationContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state)
    {
        // TODO: Implement NextWorldUpdate equivalent in SwiftlyS2
        // Core.Scheduler.NextFrame(() => d(state));
        d(state);
    }

    public override SynchronizationContext CreateCopy()
    {
        return this;
    }
}

class SyncContextScope : IDisposable
{
    private static SynchronizationContext _sourceContext = new SourceSynchronizationContext();
    private SynchronizationContext? _oldContext;

    public SyncContextScope()
    {
        _oldContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(_sourceContext);
    }

    public void Dispose()
    {
        if (_oldContext != null)
            SynchronizationContext.SetSynchronizationContext(_oldContext);
    }
}
