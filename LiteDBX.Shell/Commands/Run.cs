using System;
using System.IO;
using System.Threading.Tasks;

namespace LiteDbX.Shell.Commands;

[Help(
    Name = "run",
    Syntax = "run <filename>",
    Description = "Queue shell commands inside filename to be run in order.",
    Examples = new[]
    {
        "run scripts.txt"
    }
)]
internal class Run : IShellCommand
{
    public bool IsCommand(StringScanner s)
    {
        return s.Scan(@"run\s+").Length > 0;
    }

    public ValueTask Execute(StringScanner s, Env env)
    {
        if (env.Database == null)
            throw new Exception("Database not connected");

        var filename = s.Scan(@".+").Trim();

        foreach (var line in File.ReadAllLines(filename))
        {
            env.Input.Queue.Enqueue(line);
        }

        return ValueTask.CompletedTask;
    }
}