using AwesomeAssertions;
using TraceHunter.Core;

namespace TraceHunter.Capture.Tests;

public class CaptureCoordinatorTests
{
    private sealed class FakePrivilegeProbe(bool elevated) : IPrivilegeProbe
    {
        public bool IsElevated() => elevated;
    }

    [Fact]
    public async Task StartAsync_when_not_elevated_starts_no_sessions_and_marks_all_providers_not_configured()
    {
        var settings = new CaptureSettings
        {
            EnableKernelSession = true,
            EnableUserSession = true,
            UserSessionName = $"TH-Test-{Guid.NewGuid():N}",
        };

        await using var coordinator = new CaptureCoordinator(settings, new FakePrivilegeProbe(elevated: false));
        await coordinator.StartAsync(CancellationToken.None);

        var status = coordinator.GetStatus();
        status.NeedsElevation.Should().BeTrue();
        status.ProviderStates.Values.Should().AllSatisfy(s => s.Should().Be(ProviderState.NotConfigured));
    }

    [Fact]
    public async Task Reader_is_available_regardless_of_elevation()
    {
        var settings = new CaptureSettings { UserSessionName = $"TH-Test-{Guid.NewGuid():N}" };
        await using var coordinator = new CaptureCoordinator(settings, new FakePrivilegeProbe(elevated: false));
        await coordinator.StartAsync(CancellationToken.None);

        coordinator.Reader.Should().NotBeNull();
    }

    [SkippableFact]
    public async Task StartAsync_when_elevated_starts_user_session_providers()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        Skip.IfNot(new PrivilegeProbe().IsElevated(),
            "Requires elevated process - ETW session creation needs admin or Performance Log Users");

        var settings = new CaptureSettings
        {
            EnableKernelSession = false, // isolate user-session test from kernel
            EnableUserSession = true,
            UserSessionName = $"TH-Test-{Guid.NewGuid():N}",
        };

        await using var coordinator = new CaptureCoordinator(settings, new PrivilegeProbe());
        await coordinator.StartAsync(CancellationToken.None);

        var status = coordinator.GetStatus();
        status.NeedsElevation.Should().BeFalse();
        status.ProviderStates[ProviderId.PowerShell].Should().Be(ProviderState.Running);
    }
}
