using Harness.BusinessLogic.Layouts;
using Harness.DataAccess.Layouts;

namespace Harness.BusinessLogic.Tests.Layouts;

public sealed class WorkbenchLayoutServiceTests
{
    [Fact]
    public async Task Maps_missing_available_rejected_save_and_reset_states()
    {
        StubStore store = new();
        WorkbenchLayoutService service = new(store);

        Assert.Equal(WorkbenchLayoutLoadState.Missing, (await service.LoadAsync()).State);
        store.ReadResult = new(new("layout"), null, null);
        WorkbenchLayoutLoadResult available = await service.LoadAsync();
        Assert.Equal(WorkbenchLayoutLoadState.Available, available.State);
        Assert.Equal("layout", available.Layout?.Value);
        store.ReadResult = new(null, WorkbenchLayoutStoreFailure.IntegrityMismatch, "bad hash");
        WorkbenchLayoutLoadResult rejected = await service.LoadAsync();
        Assert.Equal(WorkbenchLayoutLoadState.Rejected, rejected.State);
        Assert.Equal("bad hash", rejected.Error);

        Assert.True((await service.SaveAsync(new("next"))).Succeeded);
        Assert.Equal("next", store.Written?.Value);
        Assert.True((await service.ResetAsync()).Succeeded);
        Assert.True(store.WasReset);
    }

    private sealed class StubStore : IWorkbenchLayoutStore
    {
        internal WorkbenchLayoutStoreReadResult ReadResult { get; set; } = new(null, null, null);
        internal WorkbenchLayoutContent? Written { get; private set; }
        internal bool WasReset { get; private set; }

        public ValueTask<WorkbenchLayoutStoreReadResult> ReadAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(ReadResult);

        public ValueTask<WorkbenchLayoutStoreWriteResult> WriteAsync(
            WorkbenchLayoutContent layout,
            CancellationToken cancellationToken = default)
        {
            Written = layout;
            return ValueTask.FromResult(new WorkbenchLayoutStoreWriteResult(true, null, null));
        }

        public ValueTask<WorkbenchLayoutStoreWriteResult> ResetAsync(
            CancellationToken cancellationToken = default)
        {
            WasReset = true;
            return ValueTask.FromResult(new WorkbenchLayoutStoreWriteResult(true, null, null));
        }
    }
}
