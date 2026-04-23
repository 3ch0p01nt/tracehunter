using System.Security.Principal;
using AwesomeAssertions;
using TraceHunter.Capture;

namespace TraceHunter.Capture.Tests;

public class PrivilegeProbeTests
{
    [Fact]
    public void IsElevated_returns_a_boolean_without_throwing()
    {
        var probe = new PrivilegeProbe();
        var act = () => probe.IsElevated();
        act.Should().NotThrow();
    }

    [Fact]
    public void IsElevated_returns_consistent_value_on_repeat_calls()
    {
        var probe = new PrivilegeProbe();
        var first = probe.IsElevated();
        var second = probe.IsElevated();
        second.Should().Be(first);
    }

    [Fact]
    public void IsElevated_matches_WindowsIdentity_oracle()
    {
        // Catches "someone replaced impl with `return true;`"
        using var identity = WindowsIdentity.GetCurrent();
        var expected = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        new PrivilegeProbe().IsElevated().Should().Be(expected);
    }
}
