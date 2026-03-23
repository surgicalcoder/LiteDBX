using System;
using System.Threading.Tasks;

namespace LiteDbX.Shell;

internal class Display
{
    public Display()
    {
        Pretty = false;
    }

    public bool Pretty { get; set; }

    public void WriteWelcome()
    {
        WriteInfo("Welcome to LiteDBX Shell");
        WriteInfo("");
        WriteInfo("Getting started with `help`");
        WriteInfo("");
    }

    public void WritePrompt(string text) => Write(ConsoleColor.White, text);

    public void WriteInfo(string text) => WriteLine(ConsoleColor.Gray, text);

    public void WriteError(Exception ex)
    {
        WriteLine(ConsoleColor.Red, ex.Message);

        if (ex is LiteException le && le.ErrorCode == LiteException.UNEXPECTED_TOKEN)
        {
            WriteLine(ConsoleColor.DarkYellow, "> " + "^".PadLeft((int)le.Position + 1, ' '));
        }
    }

    /// <summary>
    /// Stream all results from an async <see cref="IBsonDataReader"/> to the console.
    /// The reader is disposed when enumeration completes.
    /// </summary>
    public async Task WriteResult(IBsonDataReader result, Env env)
    {
        var index = 0;
        var writer = new JsonWriter(Console.Out) { Pretty = Pretty, Indent = 2 };

        await using (result)
        {
            while (await result.Read())
            {
                if (!env.Running) return;

                Write(ConsoleColor.Cyan, string.Format("[{0}]: ", ++index));

                if (Pretty) Console.WriteLine();

                Console.ForegroundColor = ConsoleColor.DarkCyan;
                writer.Serialize(result.Current);
                Console.WriteLine();
            }
        }
    }

    #region Print public methods

    public void Write(string text) => Write(Console.ForegroundColor, text);

    public void WriteLine(string text) => WriteLine(Console.ForegroundColor, text);

    public void WriteLine(ConsoleColor color, string text) => Write(color, text + Environment.NewLine);

    public void Write(ConsoleColor color, string text)
    {
        Console.ForegroundColor = color;
        Console.Write(text);
    }

    #endregion
}