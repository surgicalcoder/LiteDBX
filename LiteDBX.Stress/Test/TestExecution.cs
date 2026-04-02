using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX.Stress;

public class TestExecution
{
    private readonly TestFile _file;
    private readonly ConcurrentDictionary<int, ThreadInfo> _threads = new();
    private LiteDatabase _db;
    private long _maxRam;
    private bool _running = true;

    public TestExecution(string filename, TimeSpan duration)
    {
        Duration = duration;

        _file = new TestFile(filename);
    }

    public TimeSpan Duration { get; }
    public Stopwatch Timer { get; } = new();

    public async Task Execute()
    {
        if (_file.Delete)
        {
            DeleteFiles();
        }

        _db = await LiteDatabase.Open(_file.Filename).ConfigureAwait(false);
        await _db.Pragma("TIMEOUT", (int)_file.Timeout.TotalSeconds).ConfigureAwait(false);

        foreach (var setup in _file.Setup)
        {
            await using var reader = await _db.Execute(setup).ConfigureAwait(false);
            while (await reader.Read().ConfigureAwait(false)) { }
        }

        // create all threads
        CreateThreads();

        await ReportThread().ConfigureAwait(false);
    }

    private void DeleteFiles()
    {
        var searchPattern = Path.GetFileNameWithoutExtension(_file.Filename);
        var filesToDelete = Directory.GetFiles(".", $"{searchPattern}*" + Path.GetExtension(_file.Filename));

        foreach (var deleteFile in filesToDelete)
        {
            File.Delete(deleteFile);
        }

        File.Delete(_file.Output);
    }

    private void CreateThreads()
    {
        var workerId = 0;

        foreach (var task in _file.Tasks)
        {
            for (var i = 0; i < task.TaskCount; i++)
            {
                var currentWorkerId = Interlocked.Increment(ref workerId);

                var info = new ThreadInfo
                {
                    Task = task
                };

                info.WorkerTask = Task.Run(async () =>
                {
                    while (true)
                    {
                        try
                        {
                            await Task.Delay(task.Sleep, info.CancellationTokenSource.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }

                        if (info.CancellationTokenSource.Token.IsCancellationRequested)
                        {
                            break;
                        }

                        info.Elapsed.Restart();
                        info.Running = true;

                        try
                        {
                            info.Result = await task.Execute(_db).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            info.Exception = ex;
                            _running = false;

                            break;
                        }

                        info.Running = false;
                        info.Elapsed.Stop();

                        info.TotalRun += info.Elapsed.Elapsed;

                        if (info.Result.IsInt32)
                        {
                            info.ResultSum += info.Result.AsInt32;
                        }

                        info.Counter++;
                        info.LastRun = DateTime.Now;
                    }
                }, info.CancellationTokenSource.Token);

                _threads[currentWorkerId] = info;
            }
        }
    }

    private async Task ReportThread()
    {
        Timer.Start();

        var output = new StringBuilder();

        while (Timer.Elapsed < Duration && _running)
        {
            await Task.Delay(Math.Min(1000, (int)Duration.Subtract(Timer.Elapsed).TotalMilliseconds)).ConfigureAwait(false);

            ReportPrint(output);

            Console.Clear();
            Console.WriteLine(output.ToString());
        }

        await StopRunning().ConfigureAwait(false);

        Timer.Stop();

        await _db.DisposeAsync().ConfigureAwait(false);

        ReportPrint(output);
        ReportSummary(output);

        Console.Clear();
        Console.WriteLine(output.ToString());

        File.AppendAllText(_file.Output, output.ToString());
    }

    private void ReportPrint(StringBuilder output)
    {
        output.Clear();

        var process = Process.GetCurrentProcess();

        var ram = process.WorkingSet64 / 1024 / 1024;

        _maxRam = Math.Max(_maxRam, ram);

        output.AppendLine($"LiteDBX Multithreaded: {_threads.Count}, running for {Timer.Elapsed}");
        output.AppendLine($"Garbage Collector: gen0: {GC.CollectionCount(0)}, gen1: {GC.CollectionCount(1)}, gen2: {GC.CollectionCount(2)}");
        output.AppendLine($"Memory usage: {ram.ToString("n0")} Mb (max: {_maxRam.ToString("n0")} Mb)");
        output.AppendLine();

        foreach (var thread in _threads)
        {
            var howLong = DateTime.Now - thread.Value.LastRun;

            var id = thread.Key.ToString("00");
            var name = (thread.Value.Task.Name + (thread.Value.Running ? "*" : "")).PadRight(15, ' ');
            var counter = thread.Value.Counter.ToString().PadRight(5, ' ');
            var timer = howLong.TotalSeconds > 60 ? ((int)howLong.TotalMinutes).ToString().PadLeft(2, ' ') + " minutes" : ((int)howLong.TotalSeconds).ToString().PadLeft(2, ' ') + " seconds";
            var result = thread.Value.Result != null ? $"[{thread.Value.Result}]" : "";
            var running = thread.Value.Elapsed.Elapsed.TotalSeconds > 1 ? $"<LAST RUN {(int)thread.Value.Elapsed.Elapsed.TotalSeconds}s> " : "";
            var ex = thread.Value.Exception != null ? " ERROR: " + thread.Value.Exception.Message : "";

            output.AppendLine($"{id}. {name} :: {counter} >> {timer} {running}{result}{ex}");
        }
    }

    private void ReportSummary(StringBuilder output)
    {
        output.AppendLine("\n=====\n");
        output.AppendLine("Summary Report");
        output.AppendLine();

        foreach (var task in _file.Tasks)
        {
            var name = task.Name.PadRight(15, ' ');
            var count = _threads.Values.Where(x => x.Task == task).Sum(x => (long)x.Counter).ToString().PadLeft(5, ' ');
            var sum = _threads.Values.Where(x => x.Task == task).Sum(x => x.ResultSum);
            var ssum = sum == 0 ? "" : $"[{sum.ToString("n0")}] - ";
            var meanRuntime = TimeSpan.FromMilliseconds(_threads.Values
                                                                .Where(x => x.Task == task)
                                                                .Select(x => x.TotalRun.TotalMilliseconds)
                                                                .Average());

            output.AppendLine($"{name} :: {count} executions >> {ssum}Runtime: {meanRuntime}");
        }
    }

    private async Task StopRunning()
    {
        foreach (var t in _threads.Values)
        {
            t.CancellationTokenSource.Cancel();
        }

        foreach (var t in _threads.Values)
        {
            if (t.WorkerTask != null)
            {
                await t.WorkerTask.ConfigureAwait(false);
            }
        }
    }
}