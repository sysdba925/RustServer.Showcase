using Microsoft.Extensions.Options;
using RustServer.Plugins.Features.PluginManagement.Models.Operations;
using RustServer.Plugins.Features.PluginManagement.Operations;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class OperationHistoryStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "RustServer.Plugins.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void TrackerPersistsCompletedReportAcrossStoreRestart()
    {
        var reports = new PersistentOperationReports(CreateStore());
        var tracker = reports.Begin(
            OperationType.RevertRuntime,
            source: null,
            ["restoreRuntime"],
            "restoreRuntime");
        tracker.CompleteStep("restoreRuntime");
        tracker.Succeed();

        var restartedReports = new PersistentOperationReports(CreateStore());
        var report = restartedReports.Get(tracker.Id, CancellationToken.None);

        Assert.NotNull(report);
        Assert.Equal(OperationStatus.Succeeded, report.Status);
        Assert.NotNull(report.CompletedAt);
        Assert.Equal("completed", report.CurrentPhase);
    }

    [Fact]
    public void StartupRecoveryMarksOnlyUnfinishedReportsAsInterrupted()
    {
        var store = CreateStore();
        var report = CreateReport(Guid.NewGuid(), OperationStatus.InProgress, DateTimeOffset.UtcNow);
        store.Save(report, CancellationToken.None);

        CreateStore().MarkInProgressAsInterrupted(CancellationToken.None);
        var recovered = CreateStore().Get(report.Id, CancellationToken.None);

        Assert.NotNull(recovered);
        Assert.Equal(OperationStatus.Interrupted, recovered.Status);
        Assert.NotNull(recovered.CompletedAt);
        Assert.Equal("OperationInterrupted", recovered.Failure?.Code);
    }

    [Fact]
    public void CursorPaginationIsStableWhenNewerReportsAreAdded()
    {
        var store = CreateStore();
        var initial = Enumerable.Range(0, 20)
            .Select(index => CreateReport(
                Guid.ParseExact($"00000000-0000-0000-0000-{index + 1:000000000000}", "D"),
                OperationStatus.Succeeded,
                new DateTimeOffset(2026, 10, 10, 10, 0, 0, TimeSpan.Zero).AddMinutes(index)))
            .ToArray();
        foreach (var report in initial)
        {
            store.Save(report, CancellationToken.None);
        }

        var firstPage = store.List(10, cursor: null, CancellationToken.None);
        store.Save(
            CreateReport(Guid.NewGuid(), OperationStatus.Succeeded, firstPage.Items[0].StartedAt.AddMinutes(1)),
            CancellationToken.None);
        var secondPage = store.List(10, firstPage.NextCursor, CancellationToken.None);

        Assert.Equal(10, firstPage.Items.Count);
        Assert.Equal(10, secondPage.Items.Count);
        Assert.Empty(firstPage.Items.Select(item => item.OperationId).Intersect(secondPage.Items.Select(item => item.OperationId)));
        Assert.Equal(
            initial.OrderByDescending(report => report.StartedAt).ThenByDescending(report => report.Id.Value)
                .Skip(10).Select(report => report.Id),
            secondPage.Items.Select(item => item.OperationId));
    }

    [Fact]
    public void UsesOperationIdAsStableTieBreakerForEqualStartTimes()
    {
        var store = CreateStore();
        var startedAt = new DateTimeOffset(2026, 10, 10, 10, 0, 0, TimeSpan.Zero);
        var first = CreateReport(Guid.Parse("00000000-0000-0000-0000-000000000001"), OperationStatus.Succeeded, startedAt);
        var second = CreateReport(Guid.Parse("00000000-0000-0000-0000-000000000002"), OperationStatus.Succeeded, startedAt);
        store.Save(first, CancellationToken.None);
        store.Save(second, CancellationToken.None);

        var page = store.List(1, cursor: null, CancellationToken.None);
        var nextPage = store.List(1, page.NextCursor, CancellationToken.None);

        Assert.Equal(second.Id, Assert.Single(page.Items).OperationId);
        Assert.Equal(first.Id, Assert.Single(nextPage.Items).OperationId);
    }

    [Fact]
    public async Task SerializesConcurrentWritesWithoutLosingReports()
    {
        var store = CreateStore();
        var reports = Enumerable.Range(0, 20)
            .Select(index => CreateReport(Guid.NewGuid(), OperationStatus.Succeeded, DateTimeOffset.UtcNow.AddTicks(index)))
            .ToArray();

        await Task.WhenAll(reports.Select(report => Task.Run(() => store.Save(report, CancellationToken.None))));

        var page = store.List(200, cursor: null, CancellationToken.None);
        Assert.Equal(20, page.Items.Count);
    }

    [Fact]
    public void RejectsAnInvalidCursor()
    {
        var exception = Assert.Throws<OperationHistoryCursorException>(() =>
            CreateStore().List(20, "not-a-valid-cursor", CancellationToken.None));

        Assert.NotNull(exception);
    }

    [Fact]
    public void RejectsLimitsOutsideThePublishedRange()
    {
        var store = CreateStore();

        Assert.Throws<ArgumentOutOfRangeException>(() => store.List(0, cursor: null, CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.List(201, cursor: null, CancellationToken.None));
    }

    [Fact]
    public void DoesNotStartAnOperationWhenHistoryStorageIsUnavailable()
    {
        Directory.CreateDirectory(directory);
        var nonDirectoryPath = Path.Combine(directory, "not-a-directory");
        File.WriteAllText(nonDirectoryPath, "blocker");
        var reports = new PersistentOperationReports(new SqliteOperationHistoryStore(
            Options.Create(new OperationHistoryOptions
            {
                DatabasePath = Path.Combine(nonDirectoryPath, "operations.db")
            })));

        Assert.Throws<OperationHistoryException>(() => reports.Begin(
            OperationType.FetchGit,
            source: null,
            ["fetchPlugins"],
            "fetchPlugins"));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private SqliteOperationHistoryStore CreateStore() => new(
        Options.Create(new OperationHistoryOptions
        {
            DatabasePath = Path.Combine(directory, "operations.db")
        }));

    private static OperationReport CreateReport(Guid id, OperationStatus status, DateTimeOffset startedAt) =>
        new(
            new OperationId(id),
            OperationType.RevertRuntime,
            status,
            status is OperationStatus.Succeeded ? "completed" : "restoreRuntime",
            source: null,
            [new OperationStep("restoreRuntime", OperationStepStatus.Completed)],
            failure: null,
            rollback: new RollbackResult(RollbackStatus.NotAttempted),
            startedAt: startedAt,
            completedAt: status is OperationStatus.InProgress ? null : startedAt.AddSeconds(1));
}
