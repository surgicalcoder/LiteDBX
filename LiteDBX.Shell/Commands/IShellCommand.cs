using System.Threading.Tasks;

namespace LiteDbX.Shell;

internal interface IShellCommand
{
    bool IsCommand(StringScanner s);

    /// <summary>Execute this shell command asynchronously.</summary>
    ValueTask Execute(StringScanner s, Env env);
}