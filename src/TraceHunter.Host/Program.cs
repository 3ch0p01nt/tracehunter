using System.CommandLine;
using TraceHunter.Host.Commands;

namespace TraceHunter.Host;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var root = new RootCommand("TraceHunter - .NET-native EDR for Windows");
        root.Subcommands.Add(CaptureCommand.Build());
        return await root.Parse(args).InvokeAsync().ConfigureAwait(false);
    }
}
