using System;
using System.Threading;
namespace YolanLive.Core.Helper;
public static class RequestMetrics
{
    private static long requests;
    private static int active, peak;
    public static long Requests => Interlocked.Read(ref requests);
    public static int Active => Volatile.Read(ref active);
    public static int Peak => Volatile.Read(ref peak);
    public static IDisposable Begin()
    {
        Interlocked.Increment(ref requests);
        var count = Interlocked.Increment(ref active);
        int old;
        do { old = Volatile.Read(ref peak); if (old >= count) break; }
        while (Interlocked.CompareExchange(ref peak, count, old) != old);
        return new Lease();
    }
    private sealed class Lease : IDisposable
    {
        private int disposed;
        public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) Interlocked.Decrement(ref active); }
    }
}
