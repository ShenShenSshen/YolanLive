using System;
using System.Threading;
using System.Threading.Tasks;

namespace YolanLive.WinUI.Helper;
internal enum RecoveryOutcome { Recovered, Stopped, Failed, Cancelled }
internal sealed class PlaybackRecovery
{
    private CancellationTokenSource current;
    public bool IsRunning => current != null;
    public int Attempts { get; private set; }
    public Exception LastError { get; private set; }
    public void Cancel() { var pending = current; current = null; pending?.Cancel(); }
    public void Reset() { Cancel(); Attempts = 0; LastError = null; }
    public async Task<RecoveryOutcome> RunAsync(Func<CancellationToken, Task<RecoveryOutcome>> attempt, Action<int> progress)
    {
        if (IsRunning) return RecoveryOutcome.Cancelled;
        using var cancellation = new CancellationTokenSource();
        current = cancellation;
        try
        {
            var delays = new[] { 2, 5, 10 };
            while (Attempts < delays.Length)
            {
                var delay = delays[Attempts++];
                progress(Attempts);
                await Task.Delay(TimeSpan.FromSeconds(delay), cancellation.Token);
                try
                {
                    var result = await attempt(cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (result is RecoveryOutcome.Recovered or RecoveryOutcome.Stopped) return result;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    LastError = ex;
                    if (RequestErrorMessage.Kind(ex) == YolanLive.Core.Helper.LiveFailureKind.Authentication) return RecoveryOutcome.Failed;
                }
            }
            return RecoveryOutcome.Failed;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return RecoveryOutcome.Cancelled; }
        finally { if (ReferenceEquals(current, cancellation)) current = null; }
    }
}
