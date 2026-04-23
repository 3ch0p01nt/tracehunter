using FluentAssertions;

namespace TraceHunter.Integration.Tests;

public class SmokeTests
{
    [Fact]
    public void TestRunnerIsAlive()
    {
        // This test exists to verify the test discovery + runner is wired.
        // Real tests replace it as logic lands in subsequent phases.
        true.Should().BeTrue();
    }
}
