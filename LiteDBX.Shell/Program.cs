using System.Threading.Tasks;

namespace LiteDbX.Shell;

internal class Program
{
    /// <summary>
    /// Opens console shell app (async entry point). Usage:
    /// LiteDBX.Shell [myfile.db] --param1 value1
    /// --exec "command"   : Execute a shell command
    /// --run script.txt   : Run script commands file
    /// --pretty           : Show JSON multiline + indented
    /// --exit             : Exit after last command
    /// </summary>
    private static async Task Main(string[] args)
    {
        var input   = new InputCommand();
        var display = new Display();
        var o       = new OptionSet();

        o.Register(v => input.Queue.Enqueue("open " + v));
        o.Register("pretty", () => display.Pretty = true);
        o.Register("exit", () => input.AutoExit = true);
        o.Register<string>("run",  v => input.Queue.Enqueue("run " + v));
        o.Register<string>("exec", v => input.Queue.Enqueue(v));

        o.Parse(args);

        await ShellProgram.StartAsync(input, display);
    }
}