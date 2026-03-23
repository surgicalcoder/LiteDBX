using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace LiteDbX.Shell.Commands;

[Help(
    Name = "ed",
    Syntax = "ed",
    Description = "Open your last command in notepad."
)]
internal class Ed : IShellCommand
{
    public bool IsCommand(StringScanner s) => s.Match(@"ed$");

    public ValueTask Execute(StringScanner s, Env env)
    {
        var temp = Path.GetTempPath() + "LiteDBX.Shell.txt";

        // remove "ed" command from history
        env.Input.History.RemoveAt(env.Input.History.Count - 1);

        var last = env.Input.History.Count > 0 ? env.Input.History[env.Input.History.Count - 1] : "";

        File.WriteAllText(temp, last.Replace("\n", Environment.NewLine));

        Process.Start("notepad.exe", temp).WaitForExit();

        var text = File.ReadAllText(temp);

        if (text != last)
        {
            env.Input.Queue.Enqueue(text);
        }

        return ValueTask.CompletedTask;
    }
}