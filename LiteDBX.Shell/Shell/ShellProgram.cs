using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LiteDbX.Shell;

internal class ShellProgram
{
    /// <summary>
    /// Async shell entry point.
    /// Command dispatch and SQL execution are fully async — no .Wait() or .Result.
    /// </summary>
    public static async Task StartAsync(InputCommand input, Display display)
    {
        var env = new Env { Input = input, Display = display };

        display.WriteWelcome();

        Console.CancelKeyPress += (o, e) =>
        {
            e.Cancel = true;
            env.Running = false;
        };

        while (input.Running)
        {
            var cmd = input.ReadCommand();

            if (string.IsNullOrEmpty(cmd)) continue;

            try
            {
                var scmd = GetCommand(cmd);

                if (scmd != null)
                {
                    await scmd(env);
                    continue;
                }

                if (env.Database == null)
                    throw new Exception("Database not connected");

                env.Running = true;

                // Execute SQL and stream results asynchronously
                var reader = await env.Database.Execute(cmd);
                await display.WriteResult(reader, env);
            }
            catch (Exception ex)
            {
                display.WriteError(ex);
            }
        }
    }

    #region Shell Commands

    private static readonly List<IShellCommand> _commands = new();

    static ShellProgram()
    {
        var type  = typeof(IShellCommand);
        var types = typeof(ShellProgram).Assembly
                                        .GetTypes()
                                        .Where(p => type.IsAssignableFrom(p) && p.IsClass);

        foreach (var cmd in types)
        {
            _commands.Add(Activator.CreateInstance(cmd) as IShellCommand);
        }
    }

    public static Func<Env, ValueTask> GetCommand(string cmd)
    {
        var s = new StringScanner(cmd);

        foreach (var command in _commands)
        {
            if (!command.IsCommand(s)) continue;

            // capture s position for the Execute call
            var captured = command;
            var capturedScanner = s;
            return env => captured.Execute(capturedScanner, env);
        }

        return null;
    }

    #endregion
}