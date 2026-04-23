using AwesomeAssertions;
using TraceHunter.Capture;

namespace TraceHunter.Capture.Tests;

public class PrivilegeProbeTests
{
    [Fact]
    public void IsElevated_returns_a_boolean_without_throwing()
    {
        var probe = new PrivilegeProbe();
        // We don't assert true/false because the test runs in unknown elevation.
        // We just assert the call completes and returns a boolean.
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
}
