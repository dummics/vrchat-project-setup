using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Core.Tests;

public class EditorDiscoveryTests
{
    [Fact] public void PinnedEditorWinsAcrossCustomInstallPathsAndDuplicates()
    {
        var candidates = new[] { "D:/Custom/Unity.exe", "C:/New/Unity.exe", "d:/custom/Unity.exe", "C:/stale/Unity.exe" };
        var editors = UnityEditorDiscovery.Inspect(candidates, path => path.Contains("Custom", StringComparison.OrdinalIgnoreCase) ? "2022.3.22f1" : path.Contains("New") ? "6000.0.1f1" : null);
        Assert.Equal(2, editors.Length); Assert.Equal("D:/Custom/Unity.exe", editors[0].Path);
        Assert.Equal(UnityEditorDiscovery.PinnedVersion, editors[0].Version);
    }
    [Fact] public void MissingAndOtherExecutablesAreNotAcceptedAsUnity()
    {
        Assert.Null(UnityEditorDiscovery.ReadVersion("missing/Unity.exe"));
        Assert.Null(UnityEditorDiscovery.ReadVersion(Environment.ProcessPath!));
        Assert.False(UnityEditorDiscovery.IsPinned(null));
    }
}
