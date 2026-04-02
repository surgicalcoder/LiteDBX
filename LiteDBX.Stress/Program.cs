using System;
using System.Threading.Tasks;

namespace LiteDbX.Stress;

public class Program
{
    private static async Task Main(string[] args)
    {
        var filename = args.Length >= 1 ? args[0] : "";
        var duration = TimeSpanEx.Parse(args.Length >= 2 ? args[1] : "60s");

        var e = new TestExecution(filename, duration);

        await e.Execute();

        Console.ReadKey();
    }
}