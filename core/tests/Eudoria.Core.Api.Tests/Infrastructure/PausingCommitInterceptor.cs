using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Eudoria.Core.Api.Tests.Infrastructure;

/// <summary>
/// Holds the next transaction commit of the core until the test releases it: lets a test run
/// another request at the exact moment a first one has written but not yet committed.
/// </summary>
internal sealed class PausingCommitInterceptor : DbTransactionInterceptor
{
    private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _armed;

    /// <summary>Completes when the paused commit is reached.</summary>
    public Task Reached => _reached.Task;

    /// <summary>The next commit (and only that one) will wait for <see cref="Release"/>.</summary>
    public void PauseNextCommit() => Interlocked.Exchange(ref _armed, 1);

    public void Release() => _release.TrySetResult();

    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _armed, 0) == 1)
        {
            _reached.TrySetResult();
            await _release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }

        return result;
    }
}
