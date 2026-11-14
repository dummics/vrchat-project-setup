using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class SystemResourceUsageTests
{
    [Theory]
    [InlineData(16, 12, 12, 20, 8, 2)]
    [InlineData(16, 12, 12, 100, 8, 2)]
    [InlineData(16, 5, 12, 20, 8, 0)]
    [InlineData(16, 12, 5, 20, 8, 0)]
    [InlineData(16, 12, 12, 20, 2, 2)]
    [InlineData(128, 96, 96, 10, 64, 8)]
    [InlineData(32, 6, 6, 79.9, 4, 0)]
    [InlineData(32, 6, 6, 100, 4, 0)]
    [InlineData(64, 24, 24, 100, 24, 4)]
    [InlineData(64, 40, 15, 100, 24, 2)]
    [InlineData(128, 6, 6, 20, 4, 0)]
    public void AutomaticAdmissionRespectsMemoryReserveAndEightImportCeiling(double total, double available, double commit, double cpu, int processors, int expected)
        => Assert.Equal(expected, new SystemResourceUsage(total, available, commit, cpu, processors).SuggestedParallelism);

    [Fact]
    public void AutomaticAdmissionDoesNotNeedCpuSample()
        => Assert.Equal(5, new SystemResourceUsage(32, 24, 24, null, 16).SuggestedParallelism);

    [Fact]
    public void InvalidResourceSamplesUseSingleImport()
    {
        Assert.Equal(1, new SystemResourceUsage(32, double.NaN, 24, 10, 8).SuggestedParallelism);
        Assert.Equal(1, new SystemResourceUsage(32, 24, double.PositiveInfinity, 10, 8).SuggestedParallelism);
        Assert.Equal(1, new SystemResourceUsage(0, 24, 24, 10, 8).SuggestedParallelism);
        Assert.Equal(1, new SystemResourceUsage(16, 24, 24, 10, 8).SuggestedParallelism);
    }
}
