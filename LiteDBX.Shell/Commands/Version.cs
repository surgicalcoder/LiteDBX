using System.Threading.Tasks;

namespace LiteDbX.Shell.Commands;

[Help(
    Name = "version",
    Syntax = "ver",
    Description = "Show LiteDBX version"
)]
internal class Version : IShellCommand
{
    public bool IsCommand(StringScanner s)
    {
        return s.Scan(@"ver(sion)?$").Length > 0;
    }

    public ValueTask Execute(StringScanner s, Env env)
    {
        var assembly = typeof(ILiteDatabase).Assembly.GetName();

        env.Display.WriteLine(assembly.FullName);
        return ValueTask.CompletedTask;
    }
}