namespace Palta.Core;

public enum JobState { Queued, Running, Done, Failed, Canceled }

public sealed class Job
{
    private readonly CancellationTokenSource _cts = new();

    internal Job(IOperation operation, IReadOnlyList<string> files)
    {
        Operation = operation;
        Files = files;
        var first = Path.GetFileName(files[0]);
        Title = files.Count > 1 ? $"{operation.Label} · {first} y {files.Count - 1} más" : $"{operation.Label} · {first}";
    }

    public Guid Id { get; } = Guid.NewGuid();
    public IOperation Operation { get; }
    public IReadOnlyList<string> Files { get; }
    public string Title { get; }
    public double Progress { get; private set; }
    public JobState State { get; private set; } = JobState.Queued;
    public string? Error { get; private set; }
    public IReadOnlyList<string> Outputs { get; private set; } = [];

    /// <summary>Se dispara en el hilo del trabajo; la UI tiene que pasarlo a su hilo.</summary>
    public event Action<Job>? Changed;

    internal CancellationToken Token => _cts.Token;

    public void Cancel() => _cts.Cancel();

    internal void Set(JobState state, double? progress = null, string? error = null, IReadOnlyList<string>? outputs = null)
    {
        State = state;
        if (progress is { } p)
            Progress = p;
        Error = error ?? Error;
        Outputs = outputs ?? Outputs;
        Changed?.Invoke(this);
    }

    internal void ReportProgress(double p)
    {
        Progress = Math.Clamp(p, 0, 1);
        Changed?.Invoke(this);
    }
}

/// <summary>Ejecuta las operaciones en segundo plano, como mucho <c>concurrency</c> a la vez.</summary>
public sealed class JobQueue(int concurrency)
{
    private readonly SemaphoreSlim _slots = new(Math.Max(1, concurrency));
    private readonly List<Task> _running = [];

    public static int DefaultConcurrency => Math.Max(1, Environment.ProcessorCount / 2);

    public event Action<Job>? JobAdded;

    public Job Enqueue(IOperation operation, IReadOnlyList<string> files)
    {
        var job = new Job(operation, files);
        JobAdded?.Invoke(job);
        var task = Task.Run(() => RunAsync(job));
        lock (_running)
        {
            _running.RemoveAll(t => t.IsCompleted);
            _running.Add(task);
        }
        return job;
    }

    public Task WhenIdle()
    {
        lock (_running)
            return Task.WhenAll(_running.ToArray());
    }

    private async Task RunAsync(Job job)
    {
        try
        {
            await _slots.WaitAsync(job.Token);
        }
        catch (OperationCanceledException)
        {
            job.Set(JobState.Canceled);
            return;
        }

        try
        {
            job.Set(JobState.Running, 0);
            var progress = new SyncProgress(job.ReportProgress);
            var outputs = await job.Operation.RunAsync(job.Files, progress, job.Token);
            job.Set(JobState.Done, 1, outputs: outputs);
        }
        catch (OperationCanceledException e) when (job.Token.IsCancellationRequested || e is CanceledByUserException)
        {
            job.Set(JobState.Canceled);
        }
        catch (Exception e)
        {
            Log.Error($"Falló «{job.Title}»", e);
            job.Set(JobState.Failed, error: UserMessage(e));
        }
        finally
        {
            _slots.Release();
        }
    }

    private static string UserMessage(Exception e) => e switch
    {
        Backends.ConversionException => e.Message,
        ImageMagick.MagickCorruptImageErrorException => "La imagen está dañada o incompleta.",
        ImageMagick.MagickException => $"No se pudo leer la imagen: {FirstLine(e.Message)}",
        UnauthorizedAccessException => "Sin permiso para escribir en esa carpeta.",
        _ => FirstLine(e.Message),
    };

    private static string FirstLine(string s) => s.Split('\n', 2)[0].Trim();

    /// <summary>Progress&lt;T&gt; publica en el SynchronizationContext; acá queremos el aviso inmediato.</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
