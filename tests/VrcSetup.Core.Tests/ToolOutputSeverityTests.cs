using VrcSetup.Core;
using Xunit;

namespace VrcSetup.Tests;

public sealed class ToolOutputSeverityTests
{
    [Theory]
    [InlineData("w: error reading directory Packages/tool", EventSeverity.Warning)]
    [InlineData("[2026-10-28 WARN vrc_get] error reading directory", EventSeverity.Warning)]
    [InlineData("e: failed to download package", EventSeverity.Error)]
    [InlineData("ERROR failed to resolve package", EventSeverity.Error)]
    [InlineData("Package installed", EventSeverity.Info)]
    public void RespectsExplicitToolSeverity(string line, EventSeverity expected) => Assert.Equal(expected, ToolOutputSeverity.Classify(line));
}
