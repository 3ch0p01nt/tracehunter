using System.CommandLine;
using System.Runtime.Versioning;
using System.Text.Json;
using TraceHunter.Capture;
using TraceHunter.Core;

namespace TraceHunter.Host.Commands;

[SupportedOSPlatform("windows")]
internal static class CaptureCommand
{
    public static Command Build()
    {
        var rawOption = new Option<bool>("--raw")
        {
            Description = "Emit raw RawEvent envelopes as newline-delimited JSON instead of typed NormalizedEvent values.",
        };

        var cmd = new Command("capture", "Capture ETW events.");
        cmd.Options.Add(rawOption);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var raw = parseResult.GetValue(rawOption);

            var settings = new CaptureSettings();
            await using var coordinator = new CaptureCoordinator(settings, new PrivilegeProbe());
            await coordinator.StartAsync(ct).ConfigureAwait(false);

            var status = coordinator.GetStatus();
            if (status.NeedsElevation)
            {
                await Console.Error.WriteLineAsync("TraceHunter requires elevation to create ETW sessions.").ConfigureAwait(false);
                await Console.Error.WriteLineAsync("Either:").ConfigureAwait(false);
                await Console.Error.WriteLineAsync("  1. Re-run from an elevated terminal (right-click -> Run as administrator), OR").ConfigureAwait(false);
                await Console.Error.WriteLineAsync("  2. Add yourself to the Performance Log Users group:").ConfigureAwait(false);
                await Console.Error.WriteLineAsync("       net localgroup \"Performance Log Users\" %USERNAME% /add").ConfigureAwait(false);
                await Console.Error.WriteLineAsync("     (then sign out and back in for the group membership to take effect)").ConfigureAwait(false);
                return 3;
            }

            await Console.Error.WriteLineAsync(
                $"Capturing... press Ctrl+C to stop. Status: {JsonSerializer.Serialize(status)}").ConfigureAwait(false);

            if (raw)
            {
                await foreach (var ev in coordinator.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    Console.WriteLine(JsonSerializer.Serialize(ev));
                }
            }
            else
            {
                // Specify the base type so System.Text.Json emits the polymorphic
                // discriminator ($type) configured on NormalizedEvent.
                await foreach (var ev in coordinator.NormalizedReader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    Console.WriteLine(JsonSerializer.Serialize<NormalizedEvent>(ev));
                }
            }

            return 0;
        });

        return cmd;
    }
}
