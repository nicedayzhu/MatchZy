using SwiftlyS2.Shared;


class SourceSynchronizationContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state)
    {
        // In SwiftlyS2 this context is only used for lightweight callbacks,
        // so we can invoke the delegate directly on the current thread.
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
