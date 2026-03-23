using System.Threading.Tasks;

namespace LiteDbX.Shell.Commands;

[Help(
    Name = "quit",
    Syntax = "quit|exit",
    Description = "Close shell application"
)]
internal class Quit : IShellCommand
{
    public bool IsCommand(StringScanner s)
    {
        return s.Match(@"(quit|exit)$");
    }

    public async ValueTask Execute(StringScanner s, Env env)
    {
        if (env.Database != null)
        {
            await env.Database.DisposeAsync();
            env.Database = null;
        }
        env.Input.Running = false;
    }
}