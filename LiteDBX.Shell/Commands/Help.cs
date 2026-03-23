using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace LiteDbX.Shell.Commands;

internal class HelpAttribute : Attribute
{
    public string Name { get; set; }
    public string Syntax { get; set; }
    public string Description { get; set; }
    public string[] Examples { get; set; } = new string[0];
}

internal class Help : IShellCommand
{
    public bool IsCommand(StringScanner s) => s.Scan(@"help\s*").Length > 0;

    public ValueTask Execute(StringScanner s, Env env)
    {
        var param = s.Scan(".*");
        var d = env.Display;

        // getting all HelpAttributes inside assemblies
        var helps = AppDomain.CurrentDomain.GetAssemblies()
                             .SelectMany(x => x.GetTypes())
                             .Select(x => CustomAttributeExtensions.GetCustomAttributes(x, typeof(HelpAttribute), true).FirstOrDefault())
                             .Where(x => x != null)
                             .Select(x => x as HelpAttribute)
                             .ToArray();

        d.WriteLine(ConsoleColor.White, "# LiteDBX Shell Command Reference");

        foreach (var help in helps)
        {
            d.WriteLine("");
            d.WriteLine(ConsoleColor.Cyan, "> " + help.Syntax);
            d.WriteLine(ConsoleColor.DarkCyan, "  " + help.Description);

            // show examples only when named help command
            foreach (var example in help.Examples)
            {
                d.WriteLine(ConsoleColor.Gray, "  > " + example);
            }
        }

        return ValueTask.CompletedTask;
    }
}