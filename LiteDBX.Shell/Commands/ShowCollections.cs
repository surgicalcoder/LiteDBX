using System;
using System.Linq;
using System.Threading.Tasks;

namespace LiteDbX.Shell.Commands;

[Help(
    Name = "show collections",
    Syntax = "show collections",
    Description = "List all collections inside datafile."
)]
internal class ShowCollections : IShellCommand
{
    public bool IsCommand(StringScanner s) => s.Match(@"show\scollections$");

    public async ValueTask Execute(StringScanner s, Env env)
    {
        if (env.Database == null)
            throw new Exception("Database not connected");

        var cols = await env.Database.GetCollectionNames().ToListAsync();
        var sorted = cols.OrderBy(x => x).ToArray();

        if (sorted.Length > 0)
        {
            env.Display.WriteLine(ConsoleColor.Cyan, string.Join(Environment.NewLine, sorted));
        }
    }
}