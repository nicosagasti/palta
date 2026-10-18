using Mandarina.Core;
using Xunit;

namespace Mandarina.Tests;

public class JobQueueTests
{
    private static Operation Op(Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<string>>> run, string label = "Prueba") =>
        new("test", WheelKind.Tools, label, "", _ => true, (files, _, ct) => run(files, ct));

    [Fact]
    public async Task Job_Success_Done_WithOutputs()
    {
        var q = new JobQueue(2);
        var job = q.Enqueue(Op((_, _) => Task.FromResult<IReadOnlyList<string>>([@"C:\x\out.jpg"])), [@"C:\x\foto.png"]);
        await q.WhenIdle();
        Assert.Equal(JobState.Done, job.State);
        Assert.Equal([@"C:\x\out.jpg"], job.Outputs);
        Assert.Equal(1, job.Progress);
    }

    [Fact]
    public void Title_FirstFileAndCount()
    {
        var q = new JobQueue(1);
        var gate = new TaskCompletionSource();
        var job = q.Enqueue(Op(async (_, _) => { await gate.Task; return []; }, "JPG"), [@"C:\x\a.png", @"C:\x\b.png", @"C:\x\c.png"]);
        Assert.Equal("JPG · a.png y 2 más", job.Title);
        gate.SetResult();
    }

    [Fact]
    public async Task Job_Throws_Failed_WithMessage()
    {
        var q = new JobQueue(1);
        var job = q.Enqueue(Op((_, _) => throw new InvalidOperationException("se rompió")), [@"C:\x\a.png"]);
        await q.WhenIdle();
        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal("se rompió", job.Error);
    }

    [Fact]
    public async Task Cancel_SetsCanceled()
    {
        var q = new JobQueue(1);
        var started = new TaskCompletionSource();
        var job = q.Enqueue(Op(async (_, ct) => { started.SetResult(); await Task.Delay(Timeout.Infinite, ct); return []; }), [@"C:\x\a.png"]);
        await started.Task;
        job.Cancel();
        await q.WhenIdle();
        Assert.Equal(JobState.Canceled, job.State);
    }

    [Fact]
    public async Task Concurrency_Respected()
    {
        var q = new JobQueue(1);
        var running = 0;
        var maxRunning = 0;
        async Task<IReadOnlyList<string>> Work(IReadOnlyList<string> _, CancellationToken __)
        {
            var now = Interlocked.Increment(ref running);
            maxRunning = Math.Max(maxRunning, now);
            await Task.Delay(50);
            Interlocked.Decrement(ref running);
            return [];
        }
        for (var i = 0; i < 4; i++)
            q.Enqueue(Op(Work), [@"C:\x\a.png"]);
        await q.WhenIdle();
        Assert.Equal(1, maxRunning);
    }

    [Fact]
    public async Task Changed_RaisedOnStateChanges()
    {
        var q = new JobQueue(1);
        var states = new List<JobState>();
        Job? seen = null;
        q.JobAdded += j => { seen = j; j.Changed += x => { lock (states) states.Add(x.State); }; };
        q.Enqueue(Op((_, _) => Task.FromResult<IReadOnlyList<string>>([])), [@"C:\x\a.png"]);
        await q.WhenIdle();
        Assert.NotNull(seen);
        Assert.Contains(JobState.Running, states);
        Assert.Equal(JobState.Done, states[^1]);
    }

    [Fact]
    public async Task FailedJob_LeavesNoTempFiles()
    {
        using var d = new TempDir();
        var src = d.File("roto.png", "basura");
        var q = new JobQueue(1);
        var job = q.Enqueue(Catalog.CreateDefault(new FakeHost()).ById("img-to-jpg")!, [src]);
        await q.WhenIdle();
        Assert.Equal(JobState.Failed, job.State);
        Assert.False(string.IsNullOrWhiteSpace(job.Error));
        Assert.Equal(["roto.png"], d.Entries());
    }
}
