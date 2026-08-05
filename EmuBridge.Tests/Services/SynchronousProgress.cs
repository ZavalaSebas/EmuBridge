namespace EmuBridge.Tests.Services;

// System.Progress<T>'s dispatch is asynchronous relative to the calling code (fine for real WPF
// use, where the Dispatcher naturally serializes it; not deterministic in a test with no
// SynchronizationContext, which can make a report land after the awaited call already returned).
internal class SynchronousProgress<T> : IProgress<T>
{
    public List<T> Reports { get; } = [];
    public void Report(T value) => Reports.Add(value);
}
