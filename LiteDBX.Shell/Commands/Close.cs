using System.Threading.Tasks;

namespace LiteDbX.Shell.Commands;

[Help(
    Name = "close",
    Syntax = "close",
    Description = "Close current datafile"
)]
internal class Close : IShellCommand
{
    public bool IsCommand(StringScanner s)
    {
        return s.Scan(@"close$").Length > 0;
    }

    public async ValueTask Execute(StringScanner s, Env env)
    {
        if (env.Database != null)
        {
            await env.Database.DisposeAsync();
            env.Database = null;
        }
    }
}