using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class SystemResourceUsageTests
{
    [Theory]
    [InlineData(16, 12, 12, 20, 8, 2)]
    [InlineData(16, 12, 12, 80, 8, 1)]
    [InlineData(16, 5, 12, 20, 8, 1)]
    [InlineData(16, 12, 5, 20, 8, 1)]
    [InlineData(16, 12, 12, 20, 2, 1)]
    [InlineData(128, 96, 96, 10, 64, 2)]
    [InlineData(32, 6, 6, 79.9, 4, 2)]
    [InlineData(32, 6, 6, 80, 4, 1)]
    [InlineData(32, 5.99, 6, 20, 4, 1)]
    [InlineData(32, 6, 5.99, 20, 4, 1)]
    [InlineData(128, 6, 6, 20, 4, 1)]
    public void AutomaticAdmissionRespectsCpuMemoryAndEditorCap(double total, double available, double commit, double cpu, int processors, int expected)
        => Assert.Equal(expected, new SystemResourceUsage(total, available, commit, cpu, processors).SuggestedParallelism);

    [Fact]
    public void AutomaticAdmissionWaitsForCpuSampleBeforeStartingSecondEditor()
        => Assert.Equal(1, new SystemResourceUsage(32, 24, 24, null, 16).SuggestedParallelism);
}
